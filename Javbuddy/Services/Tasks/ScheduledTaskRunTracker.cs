using System.Collections.Concurrent;

namespace Javbuddy.Services.Tasks;

/// <summary>Process-wide, in-memory bookkeeping for scheduled tasks, shared by every
/// <see cref="ScheduledTaskRunner"/> scope: which tasks are executing right now (single-flight, so the
/// scheduler, System &gt; Tasks, Library Import and Activity &gt; Queue can never run the same task twice at
/// once) and when each last finished. The last-completion map exists for tasks that record no run
/// history (<see cref="IScheduledTask.RecordRunHistory"/> false), which have no database row to derive it
/// from; it is also kept for every other task, where the persisted history covers what happened before a restart.</summary>
public sealed class ScheduledTaskRunTracker
{
    /// <summary>When this process (this tracker) started: what a task that has never run measures its first due time from.</summary>
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    private readonly ConcurrentDictionary<string, byte> running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> lastCompletion = new(StringComparer.Ordinal);

    /// <summary>Claims the task for a run. False when it is already running, in which case the caller must not run it.</summary>
    public bool TryBegin(string taskName) => running.TryAdd(taskName, 0);

    /// <summary>Releases the claim and records <paramref name="completedUtc"/> as the task's last completion.</summary>
    public void End(string taskName, DateTime completedUtc)
    {
        lastCompletion.AddOrUpdate(taskName, completedUtc, (_, existing) => completedUtc > existing ? completedUtc : existing);
        running.TryRemove(taskName, out _);
    }

    /// <summary>Releases a claim without recording a completion (the task never actually ran).</summary>
    public void Release(string taskName) => running.TryRemove(taskName, out _);

    public bool IsRunning(string taskName) => running.ContainsKey(taskName);

    /// <summary>Returns once the task is no longer running (at once when it isn't).</summary>
    public async Task WaitUntilIdleAsync(string taskName, CancellationToken ct = default)
    {
        while (IsRunning(taskName))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    public DateTime? GetLastCompletion(string taskName) =>
        lastCompletion.TryGetValue(taskName, out var value) ? value : null;

    /// <summary>The later of two optional timestamps — a task's last completion from this process and from persisted history.</summary>
    public static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
