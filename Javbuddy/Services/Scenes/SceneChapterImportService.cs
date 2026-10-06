using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>The embedded chapters of a movie's main video file.</summary>
public sealed record FileChapters(string FileName, IReadOnlyList<FfprobeChapterInfo> Chapters);

public sealed record SceneImportResult(bool Success, int ImportedCount = 0, string? ErrorMessage = null)
{
    public static SceneImportResult Ok(int count) => new(true, count);
    public static SceneImportResult Fail(string error) => new(false, 0, error);
}

/// <summary>A chapter converted to a scene: start, optional end, optional title.</summary>
public sealed record ImportedSceneRange(double StartSeconds, double? EndSeconds, string? Title);

public interface ISceneChapterImportService
{
    /// <summary>The main video file's chapters, or null when the file can't be found or probed.</summary>
    Task<FileChapters?> GetFileChaptersAsync(int movieId, CancellationToken ct = default);

    /// <summary>Imports the main video file's chapters as the movie's scenes. Only allowed while the
    /// movie has no scenes; all-or-nothing when a chapter fails scene validation.</summary>
    Task<SceneImportResult> ImportFileChaptersAsync(int movieId, CancellationToken ct = default);
}

/// <summary>Imports a movie file's embedded chapters — including the VR part chapters Javbuddy's own
/// merge writes (<c>CODE-A</c>, <c>CODE-B</c>, …) — as its initial scenes. Manual
/// only: triggered from the scene editor, never by a library rescan. The file is found by
/// MovieVideoFiles.FindMainAsync.</summary>
public class SceneChapterImportService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient) : ISceneChapterImportService
{
    // Chapter ends within this of the next chapter's start are treated as contiguous.
    private const double ContiguousToleranceSeconds = 0.05;

    // A last chapter ending within this of the movie's duration runs to the end.
    private const double EndOfMovieToleranceSeconds = 1.0;

    public async Task<FileChapters?> GetFileChaptersAsync(int movieId, CancellationToken ct = default)
    {
        var probed = await ProbeMainFileAsync(movieId, ct);
        return probed is null ? null : new FileChapters(probed.Value.FileName, probed.Value.Info.Chapters);
    }

    public async Task<SceneImportResult> ImportFileChaptersAsync(int movieId, CancellationToken ct = default)
    {
        var probed = await ProbeMainFileAsync(movieId, ct);
        if (probed is null)
        {
            return SceneImportResult.Fail("The movie's video file couldn't be found or read.");
        }
        if (probed.Value.Info.Chapters.Count == 0)
        {
            return SceneImportResult.Fail("The video file has no chapters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieDuration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
        if (await db.Scenes.AnyAsync(s => s.MovieId == movieId, ct))
        {
            return SceneImportResult.Fail("This movie already has scenes.");
        }

        var fileDuration = probed.Value.Info.DurationSeconds > 0 ? probed.Value.Info.DurationSeconds : (double?)null;
        var ranges = ToSceneRanges(probed.Value.Info.Chapters, movieDuration ?? fileDuration);
        var accepted = new List<Scene>();
        for (var i = 0; i < ranges.Count; i++)
        {
            var scene = new Scene
            {
                MovieId = movieId,
                StartSeconds = ranges[i].StartSeconds,
                EndSeconds = ranges[i].EndSeconds,
                Title = ranges[i].Title,
                CreatedAt = DateTime.UtcNow
            };
            if (SceneRanges.Validate(scene, accepted, movieDuration) is { } error)
            {
                return SceneImportResult.Fail($"Chapter {i + 1} can't be imported: {error}");
            }
            accepted.Add(scene);
        }

        db.Scenes.AddRange(accepted);
        await db.SaveChangesAsync(ct);
        return SceneImportResult.Ok(accepted.Count);
    }

    /// <summary>Converts chapters to scene ranges: ordered by start, empty chapters dropped, titles
    /// longer than a scene title allows truncated. An end that meets the next chapter's start — or,
    /// for the last chapter, the movie's end — becomes null, so the scenes stay contiguous when one
    /// is later split or moved; any other end is kept (and clamped to the duration).</summary>
    public static IReadOnlyList<ImportedSceneRange> ToSceneRanges(IEnumerable<FfprobeChapterInfo> chapters, double? durationSeconds)
    {
        var ordered = chapters
            .Where(c => c.EndSeconds > c.StartSeconds)
            .OrderBy(c => c.StartSeconds)
            .ToList();

        var ranges = new List<ImportedSceneRange>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var chapter = ordered[i];
            var start = Math.Max(chapter.StartSeconds, 0);
            double? end = chapter.EndSeconds;
            if (i + 1 < ordered.Count)
            {
                if (Math.Abs(end.Value - ordered[i + 1].StartSeconds) <= ContiguousToleranceSeconds)
                {
                    end = null;
                }
            }
            else if (durationSeconds is { } duration && end.Value >= duration - EndOfMovieToleranceSeconds)
            {
                end = null;
            }

            if (end is { } explicitEnd && durationSeconds is { } limit)
            {
                end = Math.Min(explicitEnd, limit);
            }

            var title = chapter.Title is { Length: > 200 } longTitle ? longTitle[..200] : chapter.Title;
            ranges.Add(new ImportedSceneRange(start, end, title));
        }
        return ranges;
    }

    private async Task<(string FileName, FfprobeMediaInfo Info)?> ProbeMainFileAsync(int movieId, CancellationToken ct)
    {
        var file = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct);
        if (file is null)
        {
            return null;
        }

        var info = await ffmpegClient.ProbeAsync(file.Path, ct);
        return info is null ? null : (Path.GetFileName(file.Path), info);
    }
}
