using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.VideoRepair;

public class VideoRepairJobTracker(
    TaskActivityTracker? activities = null,
    ILogger<VideoRepairJobTracker>? logger = null) : IVideoRepairJobTracker
{
    private readonly Dictionary<int, VideoRepairJob> jobs = [];
    private readonly Lock gate = new();

    // One remux at a time: each writes a full-size staging copy, so a batch of flagged movies
    // must not saturate disk I/O or exhaust free space that the per-file check only measures once.
    private readonly SemaphoreSlim repairSlot = new(1, 1);

    public VideoRepairJob? Get(int movieId)
    {
        lock (gate)
        {
            return jobs.GetValueOrDefault(movieId);
        }
    }

    public bool IsRunning(int movieId)
    {
        lock (gate)
        {
            return jobs.TryGetValue(movieId, out var job) && job.Task is { IsCompleted: false };
        }
    }

    public IReadOnlyList<VideoRepairJob> GetActiveJobs()
    {
        lock (gate)
        {
            return jobs.Values
                .Where(j => j.Task is { IsCompleted: false })
                .ToList();
        }
    }

    public VideoRepairJob GetOrStart(
        int movieId,
        int? movieFileId,
        string movieCode,
        string fileName,
        Func<IProgress<FfmpegProgress>, CancellationToken, Task<VideoRepairResult>> runRepair,
        VideoFileJobKind kind = VideoFileJobKind.Repair)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(movieId, out var existing) && existing.Task is { IsCompleted: false })
            {
                return existing;
            }

            var job = new VideoRepairJob
            {
                Kind = kind,
                MovieId = movieId,
                MovieFileId = movieFileId,
                MovieCode = movieCode,
                FileName = fileName,
            };

            var progress = new ImmediateProgress<FfmpegProgress>(p => job.LatestProgress = p);
            job.Task = RunAndRecordAsync(job, runRepair, progress, job.Cts.Token);
            jobs[movieId] = job;
            // Nothing reads a finished job back out of the tracker (the modal only reattaches to a
            // running one and awaits the Task it already holds), so retire it as soon as it ends
            // rather than keeping one entry per movie ever repaired. Scheduled only after the entry
            // is added, so a repair that completes synchronously is still removed.
            _ = job.Task.ContinueWith(_ => Retire(job), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return job;
        }
    }

    private void Retire(VideoRepairJob job)
    {
        lock (gate)
        {
            // A restart for the same movie may already have replaced this entry in the gap between
            // the Task completing and this continuation running; leave the newer job in place.
            if (jobs.TryGetValue(job.MovieId, out var current) && ReferenceEquals(current, job))
            {
                jobs.Remove(job.MovieId);
            }

            // Cancel() takes the same lock and can no longer find this job, so it never touches the
            // disposed source.
            job.Cts.Dispose();
        }
    }

    private async Task<VideoRepairResult> RunAndRecordAsync(
        VideoRepairJob job,
        Func<IProgress<FfmpegProgress>, CancellationToken, Task<VideoRepairResult>> runRepair,
        IProgress<FfmpegProgress> progress,
        CancellationToken ct)
    {
        long? activityId = null;
        var jobName = VideoFileJobs.Describe(job.Kind);
        try
        {
            logger?.LogInformation("Starting {Job} for movie {MovieCode} (File: {FileName})", jobName, job.MovieCode, job.FileName);
            await repairSlot.WaitAsync(ct);
            VideoRepairResult result;
            try
            {
                activityId = job.Kind switch
                {
                    VideoFileJobKind.Chapters => activities?.Start($"Writing chapters · {job.MovieCode}", $"Writing scene chapters into {job.FileName}…"),
                    VideoFileJobKind.Detection => activities?.Start($"Detecting scenes · {job.MovieCode}", $"Scanning {job.FileName} for scene boundaries…"),
                    _ => activities?.Start($"Repairing · {job.MovieCode}", $"Lossless video repair of {job.FileName}…"),
                };
                result = await runRepair(progress, ct);
                if (result.Success)
                {
                    CompleteActivity(activityId, job.Kind switch
                    {
                        VideoFileJobKind.Chapters => $"Wrote chapters into {job.FileName} ({job.MovieCode}).",
                        VideoFileJobKind.Detection => $"Scanned {job.FileName} for scene boundaries ({job.MovieCode}).",
                        _ => $"Repaired {job.FileName} ({job.MovieCode}).",
                    });
                    logger?.LogInformation("Completed {Job} for movie {MovieCode}", jobName, job.MovieCode);
                }
                else
                {
                    CompleteActivity(activityId, result.ErrorMessage ?? $"Failed {jobName} for {job.FileName}.", failed: true);
                    logger?.LogWarning("Failed {Job} for movie {MovieCode}: {Error}", jobName, job.MovieCode, result.ErrorMessage);
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                CompleteActivity(activityId, $"Cancelled {jobName} for {job.MovieCode}.");
                logger?.LogInformation("Cancelled {Job} for movie {MovieCode}", jobName, job.MovieCode);
                throw;
            }
            catch (Exception ex)
            {
                CompleteActivity(activityId, $"Unexpected error during {jobName} for {job.MovieCode}. Check server logs.", failed: true);
                logger?.LogError(ex, "Unexpected error during {Job} for movie {MovieCode}", jobName, job.MovieCode);
                throw;
            }
            finally
            {
                repairSlot.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger?.LogInformation("Cancelled {Job} before it started for movie {MovieCode}", jobName, job.MovieCode);
            throw;
        }
    }

    private void CompleteActivity(long? activityId, string message, bool failed = false)
    {
        if (activityId is { } id)
        {
            activities?.Complete(id, message, failed);
        }
    }

    public void Cancel(int movieId)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(movieId, out var job))
            {
                job.Cts.Cancel();
            }
        }
    }
}
