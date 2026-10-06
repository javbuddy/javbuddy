using Javbuddy.Data;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.VideoRepair;

public class VideoRepairService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    IVideoRepairJobTracker jobTracker,
    MovieChangeNotifier? movieChangeNotifier = null,
    ILogger<VideoRepairService>? logger = null) : IVideoRepairService
{
    public async Task<IReadOnlyList<VideoRepairCandidate>> GetRepairCandidatesAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsNoTracking()
            .Include(m => m.MovieFiles)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return [];
        }

        if (movie.MovieFiles.Count > 0)
        {
            var candidates = movie.MovieFiles
                .OrderByDescending(f => f.IsPrimary)
                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new VideoRepairCandidate
                {
                    MovieId = movie.Id,
                    MovieCode = movie.Code!,
                    MovieFileId = f.Id,
                    FileName = f.FileName,
                    VersionTag = f.VersionTag,
                    FileSizeBytes = f.FileSizeBytes,
                    DurationSeconds = f.DurationSeconds ?? movie.MediaDurationSeconds ?? 0,
                    ResolutionDisplay = f.ResolutionDisplay ?? (movie.MediaWidth is > 0 && movie.MediaHeight is > 0 ? $"{movie.MediaWidth}x{movie.MediaHeight}" : null),
                    IsPrimary = f.IsPrimary,
                    HasBrokenBFrames = movie.HasBrokenBFrames,
                })
                .ToList();
            if (candidates.Count > 1)
            {
                // HasBrokenBFrames is a movie-level flag, so it can only be cleared once every
                // version has been repaired: offer repairing them all in one job.
                candidates.Add(new VideoRepairCandidate
                {
                    MovieId = movie.Id,
                    MovieCode = movie.Code!,
                    MovieFileId = null,
                    FileName = $"All versions ({candidates.Count} files)",
                    VersionTag = "All versions",
                    FileSizeBytes = candidates.Sum(c => c.FileSizeBytes),
                    DurationSeconds = candidates.Max(c => c.DurationSeconds),
                    HasBrokenBFrames = movie.HasBrokenBFrames,
                });
            }

            return candidates;
        }

        if (!string.IsNullOrWhiteSpace(movie.MediaVideoFileName))
        {
            return
            [
                new VideoRepairCandidate
                {
                    MovieId = movie.Id,
                    MovieCode = movie.Code!,
                    MovieFileId = null,
                    FileName = movie.MediaVideoFileName,
                    VersionTag = "Original",
                    FileSizeBytes = movie.LocalFileSizeBytes ?? 0,
                    DurationSeconds = movie.MediaDurationSeconds ?? 0,
                    ResolutionDisplay = movie.MediaWidth is > 0 && movie.MediaHeight is > 0 ? $"{movie.MediaWidth}x{movie.MediaHeight}" : null,
                    IsPrimary = true,
                    HasBrokenBFrames = movie.HasBrokenBFrames,
                }
            ];
        }

        return [];
    }

    public async Task<VideoRepairResult> RepairAsync(
        int movieId,
        int? movieFileId = null,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken ct = default)
    {
        string movieCode;
        List<RepairTarget> targets;
        bool clearsBrokenFlag;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies
                .AsNoTracking()
                .Include(m => m.MovieFiles)
                .FirstOrDefaultAsync(m => m.Id == movieId, ct);

            if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
            {
                return VideoRepairResult.Failed("Movie not found in database.");
            }

            movieCode = movie.Code;

            if (movieFileId is { } fid)
            {
                var targetFile = movie.MovieFiles.FirstOrDefault(f => f.Id == fid);
                if (targetFile is null)
                {
                    return VideoRepairResult.Failed("The selected video file no longer exists for this movie. Reopen the repair dialog and try again.");
                }

                targets = [new RepairTarget(targetFile.FileName, targetFile.DurationSeconds ?? movie.MediaDurationSeconds ?? 0)];
            }
            else if (movie.MovieFiles.Count > 0)
            {
                targets = movie.MovieFiles
                    .OrderByDescending(f => f.IsPrimary)
                    .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new RepairTarget(f.FileName, f.DurationSeconds ?? movie.MediaDurationSeconds ?? 0))
                    .ToList();
            }
            else
            {
                targets = [new RepairTarget(movie.MediaVideoFileName, movie.MediaDurationSeconds ?? 0)];
            }

            // HasBrokenBFrames is per movie, not per file: repairing one version of a multi-version
            // movie must not hide the versions that were left untouched.
            clearsBrokenFlag = movieFileId is null || movie.MovieFiles.Count <= 1;
        }

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(movieCode, ct);
        if (folder is null || !Directory.Exists(folder))
        {
            return VideoRepairResult.Failed($"Local media folder for {movieCode} was not found.");
        }

        var repairedCount = 0;
        string? lastRepairedPath = null;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var fileName = target.FileName;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    var detectedFiles = MovieVersionParser.FindVideoFiles(folder);
                    if (detectedFiles.Count == 0)
                    {
                        return VideoRepairResult.Failed($"No video file found in {folder}.");
                    }
                    fileName = Path.GetFileName(detectedFiles[0]);
                }

                var fileProgress = progress is not null && targets.Count > 1
                    ? new SegmentProgress(progress, i, targets.Count)
                    : progress;
                var result = await RepairFileAsync(folder, fileName, target.DurationSeconds, fileProgress, ct);
                if (!result.Success)
                {
                    return result;
                }

                repairedCount++;
                lastRepairedPath = result.OutputPath;
            }
        }
        finally
        {
            // Files already swapped in place stay repaired even if a later one fails or is
            // cancelled, so the stored size/media info must catch up either way.
            if (repairedCount > 0)
            {
                await RefreshMediaInfoAfterRepairAsync(movieId);
            }
        }

        if (clearsBrokenFlag)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var movie = await db.Movies.FindAsync([movieId], ct);
            if (movie is not null && movie.HasBrokenBFrames)
            {
                movie.HasBrokenBFrames = false;
                await db.SaveChangesAsync(ct);
            }
        }

        movieChangeNotifier?.NotifyChanged();
        return VideoRepairResult.Ok(lastRepairedPath!);
    }

    private async Task<VideoRepairResult> RepairFileAsync(
        string folder,
        string targetFileName,
        double durationSeconds,
        IProgress<FfmpegProgress>? progress,
        CancellationToken ct)
    {
        var targetPath = Path.Combine(folder, targetFileName);
        if (durationSeconds <= 0 && File.Exists(targetPath))
        {
            var probe = await ffmpegClient.ProbeAsync(targetPath, ct);
            if (probe is not null && probe.DurationSeconds > 0)
            {
                durationSeconds = probe.DurationSeconds;
            }
        }

        var totalDuration = TimeSpan.FromSeconds(durationSeconds);
        return await VideoFileReplacer.ReplaceAsync(
            targetPath,
            ffmpegClient,
            stagingPath => ffmpegClient.RemuxDts2PtsAsync(targetPath, stagingPath, totalDuration, progress, ct),
            verify: _ => null,
            logger,
            ct);
    }

    /// <summary>Refreshes stored size/media info once files were swapped on disk. A failure here
    /// must not turn an already-completed repair into a reported failure (the original is gone, so
    /// a retry would just remux the repaired file again) — it is logged and the next rescan
    /// picks up the new size.</summary>
    private async Task RefreshMediaInfoAfterRepairAsync(int movieId)
    {
        try
        {
            var refresh = await localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, CancellationToken.None);
            if (refresh is { Success: false })
            {
                logger?.LogWarning("Media info refresh after repair of movie #{MovieId} failed: {Error}", movieId, refresh.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Media info refresh after repair of movie #{MovieId} threw", movieId);
        }
    }

    public async Task<VideoRepairJob> StartRepairJobAsync(int movieId, int? movieFileId = null, CancellationToken ct = default)
    {
        string movieCode;
        string fileName;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies
                .AsNoTracking()
                .Include(m => m.MovieFiles)
                .FirstOrDefaultAsync(m => m.Id == movieId, ct);

            if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
            {
                throw new InvalidOperationException($"Movie #{movieId} not found.");
            }

            movieCode = movie.Code;

            if (movieFileId is { } fid)
            {
                var file = movie.MovieFiles.FirstOrDefault(f => f.Id == fid)
                    ?? throw new InvalidOperationException("The selected video file no longer exists for this movie.");
                fileName = file.FileName;
            }
            else if (movie.MovieFiles.Count > 1)
            {
                fileName = $"{movie.MovieFiles.Count} files";
            }
            else if (movie.MovieFiles.Count == 1)
            {
                fileName = movie.MovieFiles.First().FileName;
            }
            else
            {
                fileName = movie.MediaVideoFileName ?? $"{movie.Code}.mp4";
            }
        }

        var job = jobTracker.GetOrStart(
            movieId,
            movieFileId,
            movieCode,
            fileName,
            (prog, token) => RepairAsync(movieId, movieFileId, prog, token));

        if (job.Kind != VideoFileJobKind.Repair)
        {
            throw new InvalidOperationException($"Another job ({VideoFileJobs.Describe(job.Kind)}) is running on {job.FileName}. Wait for it to finish first.");
        }

        // Repairs are tracked per movie; a job already running for a different file must not be
        // mistaken for the one that was just requested.
        if (job.MovieFileId != movieFileId)
        {
            throw new InvalidOperationException($"A repair of {job.FileName} is already running for {movieCode}. Wait for it to finish first.");
        }

        return job;
    }

    public async Task<int> StartBatchRepairAsync(IReadOnlyList<int> movieIds, CancellationToken ct = default)
    {
        var startedCount = 0;
        foreach (var id in movieIds.Distinct())
        {
            if (jobTracker.IsRunning(id)) continue;
            try
            {
                await StartRepairJobAsync(id, null, ct);
                startedCount++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Failed to start repair job for movie #{MovieId}", id);
            }
        }
        return startedCount;
    }

    private sealed record RepairTarget(string? FileName, double DurationSeconds);

    /// <summary>Maps one file's 0–100% progress onto its slice of a multi-file repair job.</summary>
    private sealed class SegmentProgress(IProgress<FfmpegProgress> inner, int index, int count) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) =>
            inner.Report(value with { PercentComplete = (index * 100.0 + value.PercentComplete) / count });
    }
}
