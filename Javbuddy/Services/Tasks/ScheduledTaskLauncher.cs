using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Services.Tasks;

/// <summary>Queues an immediate, on-demand run of a registered <see cref="IScheduledTask"/> — System
/// &gt; Tasks' "Run now", Library Import's "Start" and Settings &gt; Metadata's r18.dev "Import now".
/// The run goes through <see cref="ScheduledTaskRunner"/> (so it gets the same history row as a
/// scheduled run) on <see cref="BackgroundJobRunner"/>, so it keeps going after the page that
/// queued it is gone.</summary>
public sealed class ScheduledTaskLauncher(BackgroundJobRunner jobs)
{
    private readonly BackgroundJobRunner jobs = jobs;

    public Task Queue(string taskName) => Queue(taskName, onRecorded: null);

    /// <summary>Queues the run and returns once its "queued" row is saved, so a page that reloads the
    /// run list right after is sure to find it. Also returns when the job ends first (an unknown task
    /// name, or a failure before the row).</summary>
    public async Task QueueAndWaitUntilRecordedAsync(string taskName)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = Queue(taskName, () => recorded.TrySetResult());
        await Task.WhenAny(recorded.Task, job);
    }

    private Task Queue(string taskName, Action? onRecorded) => jobs.Enqueue(taskName, async (services, ct) =>
    {
        var task = services.GetServices<IScheduledTask>().FirstOrDefault(t => t.Name == taskName);
        if (task is null) return;

        await services.GetRequiredService<ScheduledTaskRunner>().RunAsync(task, ct, onRecorded);
    });
}
