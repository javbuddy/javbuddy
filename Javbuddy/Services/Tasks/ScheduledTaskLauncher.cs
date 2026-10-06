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

    public Task Queue(string taskName) => jobs.Enqueue(taskName, async (services, ct) =>
    {
        var task = services.GetServices<IScheduledTask>().FirstOrDefault(t => t.Name == taskName);
        if (task is null) return;

        await services.GetRequiredService<ScheduledTaskRunner>().RunAsync(task, ct);
    });
}
