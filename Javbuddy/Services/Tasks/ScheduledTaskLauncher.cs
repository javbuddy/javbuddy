using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Services.Tasks;

/// <summary>Queues an immediate, on-demand run of a registered <see cref="IScheduledTask"/> — System
/// &gt; Tasks' "Run now", Library Import's "Start" and Settings &gt; Metadata's r18.dev "Import now".
/// The run goes through <see cref="ScheduledTaskRunner"/> (so it gets the same history row as a
/// scheduled run) on <see cref="BackgroundJobRunner"/>, so it keeps going after the page that
/// queued it is gone.</summary>
public sealed class ScheduledTaskLauncher(BackgroundJobRunner jobs, ScheduledTaskRunTracker tracker)
{
    private readonly BackgroundJobRunner jobs = jobs;
    private readonly ScheduledTaskRunTracker tracker = tracker;

    /// <summary>Queues a run; does nothing when the task is already running.</summary>
    public Task Queue(string taskName) => TryQueue(taskName, onRecorded: null) ?? Task.CompletedTask;

    /// <summary>Queues a run and returns once it has ended; when the task is already running, waits for that run to
    /// end instead, so the caller sees its result either way.</summary>
    public async Task QueueAndWaitAsync(string taskName, CancellationToken ct = default)
    {
        var job = TryQueue(taskName, onRecorded: null);
        if (job is null)
        {
            await tracker.WaitUntilIdleAsync(taskName, ct);
            return;
        }
        await job.WaitAsync(ct);
    }

    /// <summary>Queues the run and returns once its "queued" row is saved, so a page that reloads the
    /// run list right after is sure to find it. Also returns when the job ends first (an unknown task
    /// name, or a failure before the row). Returns false, queueing nothing, when the task is already running.</summary>
    public async Task<bool> QueueAndWaitUntilRecordedAsync(string taskName)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = TryQueue(taskName, () => recorded.TrySetResult());
        if (job is null) return false;

        await Task.WhenAny(recorded.Task, job);
        return true;
    }

    /// <summary>Claims the task, then queues it; null when it is already running. Claiming here, not in the job,
    /// means two near-simultaneous requests can't both get queued.</summary>
    private Task? TryQueue(string taskName, Action? onRecorded)
    {
        if (!tracker.TryBegin(taskName)) return null;

        return jobs.Enqueue(taskName, async (services, ct) =>
        {
            var task = services.GetServices<IScheduledTask>().FirstOrDefault(t => t.Name == taskName);
            if (task is null)
            {
                tracker.Release(taskName);
                return;
            }

            await services.GetRequiredService<ScheduledTaskRunner>().RunAsync(task, ct, onRecorded, alreadyClaimed: true);
        });
    }
}
