using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.VideoRepair;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

public sealed record SceneSuggestionItem(int Id, double Seconds, SceneSuggestionSource Source, double Score);

public sealed record SceneAcceptResult(int Accepted, IReadOnlyList<string> Errors);

public interface ISceneDetectionService
{
    /// <summary>Starts (or returns the already-running) background scan of the movie's main video
    /// file. Throws InvalidOperationException when it can't start.</summary>
    Task<VideoRepairJob> StartAsync(int movieId, CancellationToken ct = default);

    VideoRepairJob? GetRunningJob(int movieId);

    /// <summary>The movie's pending (not dismissed) suggestions, in time order.</summary>
    Task<IReadOnlyList<SceneSuggestionItem>> GetSuggestionsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Creates an untitled scene at the suggestion and removes it; fails like adding a
    /// scene by hand would (e.g. inside an existing scene's explicit range). insertOpeningScene also
    /// adds an untitled scene at 0:00 running up to it.</summary>
    Task<SceneOperationResult> AcceptAsync(int suggestionId, bool insertOpeningScene = false, CancellationToken ct = default);

    /// <summary>Accepts every pending suggestion; insertOpeningScene also adds an untitled scene at
    /// 0:00.</summary>
    Task<SceneAcceptResult> AcceptAllAsync(int movieId, bool insertOpeningScene = false, CancellationToken ct = default);

    /// <summary>Hides the suggestion and remembers it, so later scans don't suggest that point again.</summary>
    Task DismissAsync(int suggestionId, CancellationToken ct = default);
}

/// <summary>Suggests scene boundaries from FFmpeg black-frame and scene-change detection
/// — signal detection only, no AI. The scan decodes the whole
/// file, so it runs as a background job on Video Repair's tracker (one heavy ffmpeg job at a time,
/// Activity entry, progress). Suggestions are stored, since a scan can outlive the page that
/// started it; a new scan replaces pending ones, while dismissed ones are kept and suppress the
/// same point next time. Suggestions are stored as the scan finds them, so they can
/// be worked through while it runs. Nothing becomes a scene until the user accepts it.</summary>
public class SceneDetectionService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    IVideoRepairJobTracker jobTracker,
    IMovieSceneService sceneService,
    ILogger<SceneDetectionService>? logger = null) : ISceneDetectionService
{
    public static SceneDetectionOptions Options { get; } = new();

    public async Task<VideoRepairJob> StartAsync(int movieId, CancellationToken ct = default)
    {
        var file = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct)
            ?? throw new InvalidOperationException("The movie's video file couldn't be found locally.");

        var job = jobTracker.GetOrStart(
            movieId,
            movieFileId: null,
            file.Code,
            Path.GetFileName(file.Path),
            (progress, token) => ScanAsync(movieId, file, progress, token),
            VideoFileJobKind.Detection);
        if (job.Kind != VideoFileJobKind.Detection)
        {
            throw new InvalidOperationException($"Another job ({VideoFileJobs.Describe(job.Kind)}) is running on {job.FileName}. Wait for it to finish first.");
        }
        return job;
    }

    public VideoRepairJob? GetRunningJob(int movieId) =>
        jobTracker.Get(movieId) is { Kind: VideoFileJobKind.Detection, Task.IsCompleted: false } job ? job : null;

    private async Task<VideoRepairResult> ScanAsync(int movieId, MovieVideoFile file, IProgress<FfmpegProgress> progress, CancellationToken ct)
    {
        var probe = await ffmpegClient.ProbeAsync(file.Path, ct);
        if (probe is null || probe.DurationSeconds <= 0)
        {
            return VideoRepairResult.Failed("The movie's video file couldn't be read.");
        }

        // A new scan replaces the pending suggestions up front, since its own arrive while it runs
        //; dismissed ones stay and keep suppressing their points.
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.SceneSuggestions.Where(s => s.MovieId == movieId && !s.Dismissed).ExecuteDeleteAsync(ct);
        }

        var stream = new SceneBoundaryStream(probe.DurationSeconds, Options);
        var sink = new SuggestionSink(this, movieId, stream);
        var signals = await ffmpegClient.DetectSceneSignalsAsync(file.Path, TimeSpan.FromSeconds(probe.DurationSeconds), progress, sink, ct);
        if (signals is null)
        {
            // What was already suggested stays: each was settled, and the next scan replaces it anyway.
            return VideoRepairResult.Failed("ffmpeg couldn't scan the video file.");
        }

        stream.Complete();
        await sink.SaveSettledAsync(ct);

        logger?.LogInformation(
            "Scene detection for {Code}: {Blacks} black stretches, {Cuts} cuts, {Suggestions} suggestions",
            file.Code, signals.BlackStretches.Count, signals.Cuts.Count, sink.Saved);
        return VideoRepairResult.Ok(file.Path);
    }

    /// <summary>Stores suggestions as the scan settles them, checked against the scene
    /// starts and dismissed suggestions as they are by then — the user can accept or dismiss earlier
    /// ones while the scan goes on.</summary>
    private async Task<int> SaveAsync(int movieId, SceneBoundaryStream stream, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var starts = await db.Scenes.Where(s => s.MovieId == movieId).Select(s => s.StartSeconds).ToListAsync(ct);
        var dismissed = await db.SceneSuggestions.Where(s => s.MovieId == movieId && s.Dismissed).Select(s => s.Seconds).ToListAsync(ct);
        var boundaries = stream.TakeSettled(starts.Concat(dismissed));
        db.SceneSuggestions.AddRange(boundaries.Select(b => new SceneSuggestion
        {
            MovieId = movieId,
            Seconds = b.Seconds,
            Source = b.Source,
            Score = b.Score,
        }));
        await db.SaveChangesAsync(ct);
        return boundaries.Count;
    }

    private sealed class SuggestionSink(SceneDetectionService service, int movieId, SceneBoundaryStream stream) : IFfmpegSceneSignalSink
    {
        public int Saved { get; private set; }

        public Task OnBlackStretchAsync(FfmpegBlackStretch black, CancellationToken ct)
        {
            stream.Add(black);
            return SaveSettledAsync(ct);
        }

        public Task OnCutAsync(FfmpegSceneCut cut, CancellationToken ct)
        {
            stream.Add(cut);
            return SaveSettledAsync(ct);
        }

        public Task OnDecodedAsync(double seconds, CancellationToken ct)
        {
            stream.Advance(seconds);
            return SaveSettledAsync(ct);
        }

        public async Task SaveSettledAsync(CancellationToken ct)
        {
            if (stream.HasSettled)
            {
                Saved += await service.SaveAsync(movieId, stream, ct);
            }
        }
    }

    public async Task<IReadOnlyList<SceneSuggestionItem>> GetSuggestionsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var suggestions = await db.SceneSuggestions.AsNoTracking()
            .Where(s => s.MovieId == movieId && !s.Dismissed)
            .ToListAsync(ct);
        return suggestions
            .OrderBy(s => s.Seconds)
            .Select(s => new SceneSuggestionItem(s.Id, s.Seconds, s.Source, s.Score))
            .ToList();
    }

    public async Task<SceneOperationResult> AcceptAsync(int suggestionId, bool insertOpeningScene = false, CancellationToken ct = default)
    {
        SceneSuggestion? suggestion;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            suggestion = await db.SceneSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == suggestionId && !s.Dismissed, ct);
        }
        if (suggestion is null)
        {
            return SceneOperationResult.Fail("Suggestion not found.");
        }

        var added = await sceneService.AddSceneAsync(suggestion.MovieId, suggestion.Seconds, null, null, ct);
        if (added.Success)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.SceneSuggestions.Where(s => s.Id == suggestionId).ExecuteDeleteAsync(ct);
        }
        if (added.Success && insertOpeningScene)
        {
            // No end: it runs until the next scene, i.e. up to the accepted suggestion.
            var opening = await sceneService.AddSceneAsync(suggestion.MovieId, 0, null, null, ct);
            if (!opening.Success)
            {
                return SceneOperationResult.Fail($"Added the scene, but not the one from 0:00: {opening.ErrorMessage}");
            }
        }
        return added;
    }

    public async Task<SceneAcceptResult> AcceptAllAsync(int movieId, bool insertOpeningScene = false, CancellationToken ct = default)
    {
        var accepted = 0;
        var errors = new List<string>();
        foreach (var suggestion in await GetSuggestionsAsync(movieId, ct))
        {
            var result = await AcceptAsync(suggestion.Id, ct: ct);
            if (result.Success)
            {
                accepted++;
            }
            else
            {
                errors.Add($"{SceneTimeFormat.Format(suggestion.Seconds)}: {result.ErrorMessage}");
            }
        }
        if (insertOpeningScene && accepted > 0)
        {
            var opening = await sceneService.AddSceneAsync(movieId, 0, null, null, ct);
            if (!opening.Success)
            {
                errors.Add($"{SceneTimeFormat.Format(0)}: {opening.ErrorMessage}");
            }
        }
        return new SceneAcceptResult(accepted, errors);
    }

    public async Task DismissAsync(int suggestionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SceneSuggestions.Where(s => s.Id == suggestionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Dismissed, true), ct);
    }
}
