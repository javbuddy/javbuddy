using Javbuddy.Data;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.ClipExport;

/// <summary>What preparing an export did. On success <see cref="Token"/> downloads the clip (<c>/api/clip-exports/{token}</c>) as
/// <see cref="FileName"/>, and <see cref="ActualStartSeconds"/> is where it really starts: stream copy begins on the keyframe at or
/// before <see cref="RequestedStartSeconds"/>.</summary>
public sealed record ClipExportResult(bool Success, string? Token, string? FileName, double RequestedStartSeconds, double ActualStartSeconds, double EndSeconds, string? Error)
{
    /// <summary>How many seconds before the requested start the file begins (0 when the start was a keyframe).</summary>
    public double LeadSeconds => Math.Max(RequestedStartSeconds - ActualStartSeconds, 0);

    /// <summary>Where the browser downloads the clip from.</summary>
    public string? DownloadUrl => Token is null ? null : ClipExportService.DownloadUrl(Token);

    public static ClipExportResult Failed(string error) => new(false, null, null, 0, 0, 0, error);
}

public interface IClipExportService
{
    /// <summary>Whether ffmpeg and ffprobe are installed, without which nothing can be exported.</summary>
    bool IsAvailable { get; }

    /// <summary>Losslessly cuts one scene, highlight or apex out of its movie's main video file into a temp file the browser then
    /// downloads once (<see cref="ClipExportResult.DownloadUrl"/>). The range is the one the clip player plays: a scene's
    /// effective range, a highlight's start to end, an apex's lead-in to tail.</summary>
    Task<ClipExportResult> PrepareAsync(ClipKind kind, int id, CancellationToken ct = default);
}

public sealed class ClipExportService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    IFfmpegBinaryResolver binaryResolver,
    IApexPlaybackSettingsService apexPlayback,
    ClipExportDownloads downloads,
    ILogger<ClipExportService> logger) : IClipExportService
{
    public static string DownloadUrl(string token) => $"/api/clip-exports/{token}";

    public bool IsAvailable => binaryResolver.IsAvailable;

    public async Task<ClipExportResult> PrepareAsync(ClipKind kind, int id, CancellationToken ct = default)
    {
        try
        {
            return await PrepareCoreAsync(kind, id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cache volume full or read-only, the database busy, …: a failed export, not a crashed editor.
            logger.LogError(ex, "Preparing {Kind} {Id} for download failed", kind, id);
            return ClipExportResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<ClipExportResult> PrepareCoreAsync(ClipKind kind, int id, CancellationToken ct)
    {
        downloads.Sweep();

        var clip = await LoadClipAsync(kind, id, ct);
        if (clip is null) return ClipExportResult.Failed("That clip no longer exists.");

        var file = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, clip.MovieId, ct);
        if (file is null) return ClipExportResult.Failed("The movie's video file wasn't found.");

        var probe = await ffmpegClient.ProbeAsync(file.Path, ct);
        if (probe is null) return ClipExportResult.Failed("ffprobe couldn't read the movie's video file.");
        var duration = probe.DurationSeconds > 0 ? probe.DurationSeconds : clip.MovieDurationSeconds;

        var resolved = await ResolveAsync(clip, id, duration, ct);
        if (resolved is not { } r) return ClipExportResult.Failed("The clip's end is unknown (the movie's duration couldn't be read).");
        if (r.End - r.Start <= 0) return ClipExportResult.Failed("The clip is empty: it ends where it starts, or past the movie's end.");

        // The container that fits the source's codecs (MP4 / WebM), else Matroska; and Matroska again if that copy fails anyway
        // (say, a second audio track in a codec the first container can't hold).
        var preferred = ClipContainer.For(probe.VideoCodec, probe.AudioCodec);
        var attempt = await CopyAsync(file.Path, r.Start, r.End, preferred, ct);
        if (!attempt.Success && preferred != ClipContainer.Matroska)
        {
            logger.LogInformation("Copying {Kind} {Id} of {Code} into {Muxer} failed ({Error}); retrying as Matroska", kind, id, file.Code, preferred.Muxer, attempt.Error);
            attempt = await CopyAsync(file.Path, r.Start, r.End, ClipContainer.Matroska, ct);
        }
        if (!attempt.Success) return ClipExportResult.Failed(attempt.Error!);

        var fileName = ClipExportNaming.FileName(file.Code, r.Label, attempt.ActualStart, r.End, attempt.Container.Extension);
        var token = downloads.Add(attempt.Path, fileName, attempt.Container.ContentType);
        logger.LogInformation("Prepared {Kind} {Id} of {Code} for download ({Start:0.###}-{End:0.###} s, from {ActualStart:0.###} s) as {FileName}",
            kind, id, file.Code, r.Start, r.End, attempt.ActualStart, fileName);
        return new ClipExportResult(true, token, fileName, r.Start, attempt.ActualStart, r.End, null);
    }

    private sealed record CopyAttempt(bool Success, string Path, ClipContainer Container, double ActualStart, string? Error);

    /// <summary>Stream-copies [start, end) into a new temp file of <paramref name="container"/>; a failed copy leaves no file.</summary>
    private async Task<CopyAttempt> CopyAsync(string sourcePath, double start, double end, ClipContainer container, CancellationToken ct)
    {
        var tempPath = downloads.NewTempPath();
        var keep = false;
        try
        {
            var result = await ffmpegClient.ExportClipCopyAsync(sourcePath, start, end - start, container.Muxer, tempPath, ct);
            if (!result.Success || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                return new CopyAttempt(false, tempPath, container, start, result.ErrorMessage ?? "ffmpeg wrote no output.");
            }

            // The end is exact and the start snaps back to a keyframe, so the output's length says where it really begins.
            var output = await ffmpegClient.ProbeAsync(tempPath, ct);
            var actualStart = output is { DurationSeconds: > 0 } ? Math.Clamp(end - output.DurationSeconds, 0, start) : start;
            keep = true;
            return new CopyAttempt(true, tempPath, container, actualStart, null);
        }
        finally
        {
            if (!keep) BestEffortFile.TryDeleteFile(tempPath);
        }
    }

    /// <summary>A clip as loaded: its movie, and either its own range (highlight), its apex time and window overrides (apex),
    /// or every scene of the movie (scene: position, title and effective end depend on the others).</summary>
    private sealed record ClipRow(
        ClipKind Kind, int MovieId, double? MovieDurationSeconds, string Label, double Start, double End,
        double ApexSeconds, double? LeadIn, double? Tail, IReadOnlyList<Models.Scene> Scenes);

    private async Task<ClipRow?> LoadClipAsync(ClipKind kind, int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        switch (kind)
        {
            case ClipKind.Scene:
                {
                    var scene = await db.Scenes.AsNoTracking().Where(s => s.Id == id)
                        .Select(s => new { s.MovieId, s.Movie.MediaDurationSeconds }).FirstOrDefaultAsync(ct);
                    if (scene is null) return null;
                    var scenes = await db.Scenes.AsNoTracking().Where(s => s.MovieId == scene.MovieId).ToListAsync(ct);
                    return new ClipRow(kind, scene.MovieId, scene.MediaDurationSeconds, "", 0, 0, 0, null, null, scenes);
                }
            case ClipKind.Highlight:
                {
                    var highlight = await db.MovieHighlights.AsNoTracking().Where(h => h.Id == id)
                        .Select(h => new { h.MovieId, h.Movie.MediaDurationSeconds, h.Title, h.StartSeconds, h.EndSeconds }).FirstOrDefaultAsync(ct);
                    if (highlight is null) return null;
                    var starts = await db.MovieHighlights.AsNoTracking().Where(h => h.MovieId == highlight.MovieId)
                        .Select(h => new { h.Id, h.StartSeconds }).ToListAsync(ct);
                    var position = starts.OrderBy(h => h.StartSeconds).ThenBy(h => h.Id).Select(h => h.Id).ToList().IndexOf(id) + 1;
                    var label = string.IsNullOrWhiteSpace(highlight.Title) ? $"Highlight {position}" : highlight.Title;
                    return new ClipRow(kind, highlight.MovieId, highlight.MediaDurationSeconds, label, highlight.StartSeconds, highlight.EndSeconds, 0, null, null, []);
                }
            case ClipKind.Apex:
                {
                    var apex = await db.MovieApexes.AsNoTracking().Where(a => a.Id == id)
                        .Select(a => new { a.MovieId, a.Movie.MediaDurationSeconds, a.Seconds, a.LeadInSeconds, a.TailSeconds }).FirstOrDefaultAsync(ct);
                    if (apex is null) return null;
                    var times = await db.MovieApexes.AsNoTracking().Where(a => a.MovieId == apex.MovieId)
                        .Select(a => new { a.Id, a.Seconds }).ToListAsync(ct);
                    var position = times.OrderBy(a => a.Seconds).ThenBy(a => a.Id).Select(a => a.Id).ToList().IndexOf(id) + 1;
                    return new ClipRow(kind, apex.MovieId, apex.MediaDurationSeconds, $"Apex {position}", 0, 0, apex.Seconds, apex.LeadInSeconds, apex.TailSeconds, []);
                }
            default:
                return null;
        }
    }

    /// <summary>The clip's label and the range the clip player plays, cut at the movie's end; null when a scene runs to an
    /// unknown end.</summary>
    private async Task<(string Label, double Start, double End)?> ResolveAsync(ClipRow clip, int id, double? durationSeconds, CancellationToken ct)
    {
        switch (clip.Kind)
        {
            case ClipKind.Scene:
                {
                    // Deleted between the two queries: no range.
                    var scene = SceneRanges.ResolveEffectiveRanges(clip.Scenes, durationSeconds).FirstOrDefault(s => s.Scene.Id == id);
                    if (scene?.EffectiveEndSeconds is not { } end) return null;
                    return (scene.DisplayTitle, scene.Scene.StartSeconds, CutAtEnd(end, durationSeconds));
                }
            case ClipKind.Apex:
                {
                    var window = ApexRanges.Resolve(clip.LeadIn, clip.Tail, await apexPlayback.GetEffectiveAsync(ct));
                    var (start, end) = ApexRanges.ClipFor(clip.ApexSeconds, window, durationSeconds);
                    return (clip.Label, start, end);
                }
            default:
                return (clip.Label, clip.Start, CutAtEnd(clip.End, durationSeconds));
        }
    }

    private static double CutAtEnd(double end, double? durationSeconds) => durationSeconds is > 0 and var d ? Math.Min(end, d) : end;
}
