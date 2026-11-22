using System.Collections.Concurrent;

namespace Javbuddy.Services.Tasks;

/// <summary>Process-wide, in-memory record of which scheduled tasks are executing right now, shared by
/// every <see cref="ScheduledTaskRunner"/> scope. It makes a task single-flight: the scheduler, System &gt;
/// Tasks, Library Import and Activity &gt; Queue can never run the same task twice at once.</summary>
public sealed class ScheduledTaskRunTracker
{
    private readonly ConcurrentDictionary<string, byte> running = new(StringComparer.Ordinal);

    /// <summary>Claims the task for a run. False when it is already running, in which case the caller must not run it.</summary>
    public bool TryBegin(string taskName) => running.TryAdd(taskName, 0);

    /// <summary>Releases the claim made by <see cref="TryBegin"/>.</summary>
    public void End(string taskName) => running.TryRemove(taskName, out _);

    public bool IsRunning(string taskName) => running.ContainsKey(taskName);
}
