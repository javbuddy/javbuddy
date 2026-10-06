using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.VrMerge;

/// <summary>State of one in-flight (or finished) merge job. Mutable and reference-shared rather than
/// a record, so progress updates and the eventual Task result are visible to any caller holding the
/// same instance without needing to re-fetch it from the tracker.</summary>
public class VrMergeJob
{
    public Task<VrMergeResult>? Task { get; internal set; }
    public CancellationTokenSource Cts { get; } = new();
    public FfmpegProgress? LatestProgress { get; internal set; }
}

/// <summary>Tracks the running VR merge job (if any) per TorrentDownload, keyed by TorrentDownloadId (a
/// finished job is dropped, releasing its cancellation source), so navigating away from the sort wizard and back reattaches to a merge in flight instead of
/// orphaning it — a ~7 GB stream-copy over a network share is minutes, not seconds. Registered as a
/// singleton (see Program.cs) since it must outlive any one Blazor circuit.</summary>
public interface IVrMergeJobTracker
{
    VrMergeJob? Get(int torrentDownloadId);

    /// <summary>Returns the existing running job for this download, or starts a new one by invoking
    /// runMerge with a progress reporter and cancellation token this tracker owns.</summary>
    VrMergeJob GetOrStart(int torrentDownloadId, Func<IProgress<FfmpegProgress>, CancellationToken, Task<VrMergeResult>> runMerge);

    VrMergeJob GetOrStart(int torrentDownloadId, string movieCode, Func<IProgress<FfmpegProgress>, CancellationToken, Task<VrMergeResult>> runMerge);

    void Cancel(int torrentDownloadId);
}

public class VrMergeJobTracker(JavbuddyMetrics metrics, TaskActivityTracker? activities = null) : IVrMergeJobTracker
{
    private readonly JavbuddyMetrics metrics = metrics;
    private readonly Dictionary<int, VrMergeJob> jobs = [];
    private readonly Lock gate = new();

    public VrMergeJob? Get(int torrentDownloadId)
    {
        lock (gate)
        {
            return jobs.GetValueOrDefault(torrentDownloadId);
        }
    }

    public VrMergeJob GetOrStart(int torrentDownloadId, Func<IProgress<FfmpegProgress>, CancellationToken, Task<VrMergeResult>> runMerge)
        => GetOrStart(torrentDownloadId, "movie sort", runMerge);

    public VrMergeJob GetOrStart(int torrentDownloadId, string movieCode, Func<IProgress<FfmpegProgress>, CancellationToken, Task<VrMergeResult>> runMerge)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(torrentDownloadId, out var existing) && existing.Task is { IsCompleted: false })
            {
                return existing;
            }

            var job = new VrMergeJob();
            var progress = new ImmediateProgress<FfmpegProgress>(p => job.LatestProgress = p);
            var activityId = activities?.Start($"Sorting · {movieCode}", "Merging VR video parts…");
            metrics.VrMergeJobStarted();
            job.Task = RunAndRecordAsync(runMerge, progress, job.Cts.Token, activityId);
            jobs[torrentDownloadId] = job;
            // The wizard only reattaches to a running merge and awaits the Task it already holds, so a
            // finished job is retired as soon as it ends instead of being kept for every download ever
            // merged. Scheduled after the entry is added, so a merge that completes synchronously is
            // still removed.
            _ = job.Task.ContinueWith(_ => Retire(torrentDownloadId, job), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return job;
        }
    }

    private void Retire(int torrentDownloadId, VrMergeJob job)
    {
        lock (gate)
        {
            // A restart for the same download may already have replaced this entry; leave the newer job.
            if (jobs.TryGetValue(torrentDownloadId, out var current) && ReferenceEquals(current, job))
            {
                jobs.Remove(torrentDownloadId);
            }

            // Cancel() takes the same lock and can no longer find this job, so it never touches the
            // disposed source.
            job.Cts.Dispose();
        }
    }

    private async Task<VrMergeResult> RunAndRecordAsync(
        Func<IProgress<FfmpegProgress>, CancellationToken, Task<VrMergeResult>> runMerge,
        IProgress<FfmpegProgress> progress,
        CancellationToken ct,
        long? activityId)
    {
        try
        {
            var result = await runMerge(progress, ct);
            metrics.VrMergeJobs.Add(1, new KeyValuePair<string, object?>("outcome", result.Success ? "success" : "failure"));
            CompleteActivity(activityId, result.Success ? "VR video parts merged." : result.ErrorMessage ?? "VR merge failed.", !result.Success);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            metrics.VrMergeJobs.Add(1, new KeyValuePair<string, object?>("outcome", "cancelled"));
            CompleteActivity(activityId, "VR merge cancelled.");
            throw;
        }
        catch
        {
            metrics.VrMergeJobs.Add(1, new KeyValuePair<string, object?>("outcome", "error"));
            CompleteActivity(activityId, "VR merge failed. Check the server logs for details.", failed: true);
            throw;
        }
        finally
        {
            metrics.VrMergeJobEnded();
        }
    }

    private void CompleteActivity(long? activityId, string message, bool failed = false)
    {
        if (activityId is { } id)
        {
            activities?.Complete(id, message, failed);
        }
    }

    public void Cancel(int torrentDownloadId)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(torrentDownloadId, out var job))
            {
                job.Cts.Cancel();
            }
        }
    }
}
