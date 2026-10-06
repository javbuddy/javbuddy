using Javbuddy.Data;
using Javbuddy.Services.Common;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.VideoRepair;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>What "Write chapters to file" would do, for its confirmation.</summary>
public sealed record ChapterWritePlan(string FileName, IReadOnlyList<PlannedChapter> Chapters, IReadOnlyList<FfprobeChapterInfo> CurrentChapters)
{
    public int GapCount => Chapters.Count(c => c.IsGap);

    /// <summary>True when the file already has chapters that differ from the ones to be written
    /// (e.g. VR part chapters) — they'll be replaced, which the confirmation must say.</summary>
    public bool ReplacesDifferentChapters => CurrentChapters.Count > 0 && !SceneChapterWriteService.SameChapters(CurrentChapters, Chapters);
}

public sealed record ChapterWritePlanResult(ChapterWritePlan? Plan, string? ErrorMessage = null);

public interface ISceneChapterWriteService
{
    Task<ChapterWritePlanResult> GetPlanAsync(int movieId, CancellationToken ct = default);

    /// <summary>Starts (or returns the already-running) background job writing the movie's scenes
    /// into its primary video file. Throws InvalidOperationException when it can't start.</summary>
    Task<VideoRepairJob> StartAsync(int movieId, CancellationToken ct = default);

    /// <summary>The movie's running chapter-writing job, if any.</summary>
    VideoRepairJob? GetRunningJob(int movieId);
}

/// <summary>Writes a movie's scenes into its primary video file as embedded chapters
/// — an explicit, opt-in lossless remux; nothing writes chapters automatically. Gaps between scenes
/// become untitled filler chapters (SceneChapterPlan) and every existing chapter is replaced,
/// including VR part chapters. Runs through Video Repair's job tracker (one remux at a time,
/// Activity entry, progress) and its safe file replacement (VideoFileReplacer), verifying the new
/// file's chapter count before swapping it in; then refreshes the stored media info and asks the
/// media server to refresh the item so it picks the chapters up.</summary>
public class SceneChapterWriteService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    IVideoRepairJobTracker jobTracker,
    IMediaServerClient? mediaServerClient = null,
    MovieChangeNotifier? movieChangeNotifier = null,
    ILogger<SceneChapterWriteService>? logger = null) : ISceneChapterWriteService
{
    private const double SameBoundaryToleranceSeconds = 0.05;

    public async Task<ChapterWritePlanResult> GetPlanAsync(int movieId, CancellationToken ct = default)
    {
        var (plan, _, error) = await BuildPlanAsync(movieId, ct);
        return new ChapterWritePlanResult(plan, error);
    }

    public VideoRepairJob? GetRunningJob(int movieId) =>
        jobTracker.Get(movieId) is { Kind: VideoFileJobKind.Chapters, Task.IsCompleted: false } job ? job : null;

    public async Task<VideoRepairJob> StartAsync(int movieId, CancellationToken ct = default)
    {
        var (plan, file, error) = await BuildPlanAsync(movieId, ct);
        if (plan is null || file is null)
        {
            throw new InvalidOperationException(error ?? "Chapters can't be written for this movie.");
        }

        var job = jobTracker.GetOrStart(
            movieId,
            movieFileId: null,
            file.Code,
            plan.FileName,
            (progress, token) => WriteAsync(movieId, progress, token),
            VideoFileJobKind.Chapters);
        if (job.Kind != VideoFileJobKind.Chapters)
        {
            throw new InvalidOperationException($"Another job ({VideoFileJobs.Describe(job.Kind)}) is running on {job.FileName}. Wait for it to finish first.");
        }
        return job;
    }

    private async Task<VideoRepairResult> WriteAsync(int movieId, IProgress<FfmpegProgress> progress, CancellationToken ct)
    {
        // Planned again at run time: scenes may have changed while the job waited for the slot.
        var (plan, file, error) = await BuildPlanAsync(movieId, ct);
        if (plan is null || file is null)
        {
            return VideoRepairResult.Failed(error ?? "Chapters can't be written for this movie.");
        }

        var metadata = SceneChapterPlan.ToFfMetadata(plan.Chapters, blankTitleForGaps: SceneChapterPlan.IsMp4Family(file.Path));
        var totalDuration = TimeSpan.FromSeconds(plan.Chapters[^1].EndSeconds);
        var result = await VideoFileReplacer.ReplaceAsync(
            file.Path,
            ffmpegClient,
            stagingPath => ffmpegClient.RemuxWithChaptersAsync(file.Path, metadata, stagingPath, totalDuration, progress, ct),
            verify: probe => probe.ChapterCount == plan.Chapters.Count
                ? null
                : $"The rewritten file has {probe.ChapterCount} chapters instead of {plan.Chapters.Count}; the original was kept.",
            logger,
            ct);
        if (!result.Success)
        {
            return result;
        }

        await AfterWriteAsync(movieId);
        movieChangeNotifier?.NotifyChanged();
        return result;
    }

    /// <summary>Best effort: the file is already rewritten, so a failing refresh is only logged.</summary>
    private async Task AfterWriteAsync(int movieId)
    {
        try
        {
            var refresh = await localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, CancellationToken.None);
            if (refresh is { Success: false })
            {
                logger?.LogWarning("Media info refresh after writing chapters for movie #{MovieId} failed: {Error}", movieId, refresh.ErrorMessage);
            }

            string? itemId;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                itemId = await db.Movies.Where(m => m.Id == movieId).Select(m => m.JellyfinItemId).FirstOrDefaultAsync();
            }
            if (mediaServerClient is not null && !string.IsNullOrWhiteSpace(itemId))
            {
                var refreshed = await mediaServerClient.RefreshItemAsync(itemId, CancellationToken.None);
                if (!refreshed.Success)
                {
                    logger?.LogWarning("Media server refresh after writing chapters for movie #{MovieId} failed: {Error}", movieId, refreshed.ErrorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Refreshing movie #{MovieId} after writing chapters threw", movieId);
        }
    }

    private async Task<(ChapterWritePlan? Plan, MovieVideoFile? File, string? Error)> BuildPlanAsync(int movieId, CancellationToken ct)
    {
        IReadOnlyList<ResolvedScene> scenes;
        double? movieDuration;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            movieDuration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
            var rows = await db.Scenes.AsNoTracking().Where(s => s.MovieId == movieId).ToListAsync(ct);
            if (rows.Count == 0)
            {
                return (null, null, "This movie has no scenes to write.");
            }
            scenes = SceneRanges.ResolveEffectiveRanges(rows, movieDuration);
        }

        var file = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct);
        if (file is null)
        {
            return (null, null, "The movie's video file couldn't be found locally.");
        }

        var probe = await ffmpegClient.ProbeAsync(file.Path, ct);
        if (probe is null || probe.DurationSeconds <= 0)
        {
            return (null, file, "The movie's video file couldn't be read.");
        }

        // A last scene without an end runs to the end of the file itself.
        if (movieDuration is null)
        {
            scenes = SceneRanges.ResolveEffectiveRanges(scenes.Select(s => s.Scene), probe.DurationSeconds);
        }

        var chapters = SceneChapterPlan.Build(scenes, probe.DurationSeconds);
        if (chapters.Count == 0)
        {
            return (null, file, "None of the scenes fall inside the video file.");
        }
        return (new ChapterWritePlan(Path.GetFileName(file.Path), chapters, probe.Chapters), file, null);
    }

    /// <summary>Whether the file's chapters already are the planned ones (same boundaries within
    /// 50 ms and the same titles, a blank title counting as none).</summary>
    public static bool SameChapters(IReadOnlyList<FfprobeChapterInfo> current, IReadOnlyList<PlannedChapter> planned) =>
        current.Count == planned.Count
        && current.Zip(planned).All(pair =>
            Math.Abs(pair.First.StartSeconds - pair.Second.StartSeconds) <= SameBoundaryToleranceSeconds
            && Math.Abs(pair.First.EndSeconds - pair.Second.EndSeconds) <= SameBoundaryToleranceSeconds
            && string.Equals(pair.First.Title.TrimToNull(), pair.Second.Title.TrimToNull(), StringComparison.Ordinal));

}
