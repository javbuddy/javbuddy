using System.Collections.Concurrent;

namespace Javbuddy.Services.Tasks;

/// <summary>Process-wide map of in-flight ScheduledTaskRun ids to the CancellationTokenSource
/// their ScheduledTaskRunner.RunAsync is running under, so System &gt; Tasks can actually stop a
/// running task rather than only marking its row failed. A singleton because ScheduledTaskRunner
/// is scoped — every run (scheduler tick, "Run now", Library Import) resolves its own runner, but
/// the page cancelling it lives in yet another scope. A run id missing from here is either
/// finished or an orphan left behind by a previous process (no work to stop).</summary>
public sealed class ScheduledTaskCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> active = new();

    public void Register(int runId, CancellationTokenSource cts) => active[runId] = cts;

    public void Unregister(int runId) => active.TryRemove(runId, out _);

    /// <summary>True when this process is still executing the run, so cancelling it will stop
    /// real work.</summary>
    public bool IsActive(int runId) => active.ContainsKey(runId);

    public bool IsCancellationRequested(int runId) =>
        active.TryGetValue(runId, out var cts) && cts.IsCancellationRequested;

    /// <summary>Signals the run's token. Returns false when the run isn't executing in this
    /// process (already finished, or orphaned by a restart).</summary>
    public bool TryCancel(int runId)
    {
        if (!active.TryGetValue(runId, out var cts)) return false;

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run finished and disposed its source between the lookup and Cancel.
            return false;
        }
    }
}
