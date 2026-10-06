using System.Globalization;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

public enum TrickplayGenerateResult
{
    Generated,

    /// <summary>A set for the main file's current identity already exists.</summary>
    AlreadyCurrent,

    /// <summary>The main file hasn't been probed yet, so it has no identity.</summary>
    NotProbed,

    /// <summary>The main file isn't reachable locally.</summary>
    NoFile,

    /// <summary>The highlight is long enough for the movie's own set.</summary>
    NotNeeded,

    Failed,
}

/// <summary>Generates one movie's trickplay set with ffmpeg: a 320 px wide thumbnail
/// every 10 s, 10x10 per WebP sheet — Jellyfin's geometry — cropped to the left eye for
/// side-by-side VR. Full decode unless the KeyframeOnly setting is on. Each ffmpeg run, and its
/// progress, is reported to the TrickplayGenerationTracker.</summary>
public interface ITrickplayGenerator
{
    /// <param name="progress">Receives ffmpeg's progress through the video while it runs.</param>
    Task<TrickplayGenerateResult> GenerateAsync(int movieId, IProgress<FfmpegProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Generates a highlight's own denser set for [start, end] of the movie's
    /// main file, at HighlightTrickplay's interval. Always a full decode: keyframes are too far apart
    /// for thumbnails a second or two apart, and a clip decodes quickly anyway.</summary>
    Task<TrickplayGenerateResult> GenerateClipAsync(int movieId, double startSeconds, double endSeconds, CancellationToken ct = default);
}

public sealed class TrickplayGenerator(
    IDbContextFactory<AppDbContext> dbFactory,
    IMovieStreamService streamService,
    IFfmpegClient ffmpegClient,
    ITrickplayStore store,
    ITrickplaySettingsService settingsService,
    TrickplayGenerationTracker tracker,
    ILogger<TrickplayGenerator> logger) : ITrickplayGenerator
{
    public const int IntervalSeconds = 10;
    public const int ThumbnailWidth = 320;
    public const int TilesPerSide = 10;

    public async Task<TrickplayGenerateResult> GenerateAsync(int movieId, IProgress<FfmpegProgress>? progress = null, CancellationToken ct = default)
    {
        TrickplayMainFile? main;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            main = await TrickplayMainFiles.LoadAsync(db, movieId, ct);
        }
        if (main?.Identity is not { } identity) return TrickplayGenerateResult.NotProbed;
        if (await store.GetSetAsync(main.Code, identity, ct) is not null) return TrickplayGenerateResult.AlreadyCurrent;

        var settings = await settingsService.GetEffectiveAsync(ct);
        return await GenerateSetAsync(new TrickplayTarget(movieId), main, identity, null, IntervalSeconds, 0, main.DurationSeconds!.Value, settings.KeyframeOnly, progress, ct);
    }

    public async Task<TrickplayGenerateResult> GenerateClipAsync(int movieId, double startSeconds, double endSeconds, CancellationToken ct = default)
    {
        if (HighlightTrickplay.IntervalSeconds(endSeconds - startSeconds) is not { } interval) return TrickplayGenerateResult.NotNeeded;

        TrickplayMainFile? main;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            main = await TrickplayMainFiles.LoadAsync(db, movieId, ct);
        }
        if (main?.Identity is not { } fileIdentity) return TrickplayGenerateResult.NotProbed;
        var identity = HighlightTrickplay.Identity(fileIdentity, startSeconds, endSeconds)!;
        if (await store.GetSetAsync(main.Code, identity, ct) is not null) return TrickplayGenerateResult.AlreadyCurrent;

        // A highlight running past the end of the file covers only what's there.
        var length = Math.Min(endSeconds, main.DurationSeconds!.Value) - startSeconds;
        if (!(length > 0)) return TrickplayGenerateResult.Failed;
        return await GenerateSetAsync(TrickplayTarget.ForHighlight(movieId, startSeconds, endSeconds), main, identity, fileIdentity,
            interval, startSeconds, length, keyframeOnly: false, null, ct);
    }

    /// <summary>Generates and saves a set for [start, start + length] of the main file, as
    /// <paramref name="identity"/>; <paramref name="sourceIdentity"/> is the file's identity for a
    /// clip's set, null for the whole file's.</summary>
    private async Task<TrickplayGenerateResult> GenerateSetAsync(TrickplayTarget target, TrickplayMainFile main, string identity, string? sourceIdentity,
        int intervalSeconds, double startSeconds, double lengthSeconds, bool keyframeOnly, IProgress<FfmpegProgress>? progress, CancellationToken ct)
    {
        // The file the player streams; if the recorded main file has gone, MovieVideoFiles falls back
        // to another video in the folder, which this identity doesn't describe.
        var path = await streamService.GetMainFilePathAsync(target.MovieId, ct);
        if (path is null || !string.Equals(Path.GetFileName(path), main.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return TrickplayGenerateResult.NoFile;
        }

        var clip = sourceIdentity is not null;
        var leftEyeOnly = SideBySideVideo.IsSideBySide(main.Width, main.Height);
        var request = new FfmpegTrickplayRequest(
            intervalSeconds, ThumbnailWidth, ThumbnailHeight(main.Width!.Value, main.Height!.Value, leftEyeOnly),
            TilesPerSide, TilesPerSide, leftEyeOnly, keyframeOnly,
            clip ? startSeconds : null, clip ? lengthSeconds : null);

        // Tracked until the set is saved, so a player picking it up once the run ends finds it.
        using var generating = tracker.Start(target);
        var reporting = new ImmediateProgress<FfmpegProgress>(report =>
        {
            generating.Report(report.PercentComplete);
            progress?.Report(report);
        });

        // ffmpeg needs a real folder to write into: a local temp one, whose sheets are then saved
        // to the store.
        var staging = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "javbuddy-trickplay", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var run = await ffmpegClient.GenerateTrickplayAsync(path, request, staging, TimeSpan.FromSeconds(lengthSeconds), reporting, ct);
            var sheets = SheetFiles(staging);
            if (!run.Success || sheets.Count == 0)
            {
                logger.LogWarning("Generating trickplay for {Code} failed: {Error}", main.Code, run.ErrorMessage ?? "no tile sheets written");
                return TrickplayGenerateResult.Failed;
            }

            var set = new TrickplaySet
            {
                Identity = identity,
                Width = ThumbnailWidth,
                Height = request.ThumbnailHeight,
                TileWidth = TilesPerSide,
                TileHeight = TilesPerSide,
                ThumbnailCount = Math.Min(ThumbnailCount(lengthSeconds, intervalSeconds), sheets.Count * TilesPerSide * TilesPerSide),
                IntervalMs = intervalSeconds * 1000,
                DurationSeconds = lengthSeconds,
                LeftEyeOnly = leftEyeOnly,
                KeyframeOnly = keyframeOnly,
                GeneratedAt = DateTime.UtcNow,
                FileName = main.FileName!,
                SourceIdentity = sourceIdentity,
                StartSeconds = startSeconds,
            };
            await store.SaveAsync(main.Code, set, sheets.Select(file => (Func<Stream>)(() => File.OpenRead(file))).ToList(), ct);
            return TrickplayGenerateResult.Generated;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>ffmpeg's numbered sheets (<c>0.webp, 1.webp, …</c>) in index order.</summary>
    private static List<string> SheetFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.webp")
            .Select(file => (File: file, Index: int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : -1))
            .Where(sheet => sheet.Index >= 0)
            .OrderBy(sheet => sheet.Index)
            .Select(sheet => sheet.File)
            .ToList();

    /// <summary>The thumbnail height keeping the (cropped) frame's aspect, rounded to an even number
    /// as video scaling needs.</summary>
    public static int ThumbnailHeight(int width, int height, bool leftEyeOnly)
    {
        var sourceWidth = leftEyeOnly ? width / 2.0 : width;
        return Math.Max(2, (int)Math.Round(ThumbnailWidth * height / sourceWidth / 2) * 2);
    }

    /// <summary>One thumbnail per started interval: ffmpeg's fps filter emits one for t = 0, 10, 20, …</summary>
    public static int ThumbnailCount(double durationSeconds, int intervalSeconds = IntervalSeconds) =>
        Math.Max(1, (int)Math.Ceiling(durationSeconds / intervalSeconds));

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
