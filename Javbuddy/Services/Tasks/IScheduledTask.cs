namespace Javbuddy.Services.Tasks;

/// <summary>One "how far along" sample a task reports mid-run — Current out of Total items
/// processed so far. Total is null when a task can't cheaply know its total up front (e.g. a
/// streamed import); the UI shows just Current in that case, not a fraction/percentage. Stage is
/// an optional human-readable label for what the task is doing right now (e.g. "Probing
/// MediaInfo") — most useful for a multi-phase task like LibraryRescanTask, where a bare Current/Total
/// that resets to 0 at the start of each phase would otherwise look like progress went backwards.
/// Left null, the UI just shows the bare count as before.</summary>
public readonly record struct TaskProgress(int Current, int? Total, string? Stage = null);

/// <summary>A task that runs on its own periodic interval, tracked on the System &gt; Tasks page.
/// Implementations are resolved per-run from a fresh DI scope (see ScheduledTaskRunner), so they
/// can freely depend on Scoped services like IJellyfinClient or the DbContext factory.</summary>
public interface IScheduledTask
{
    /// <summary>Stable identifier shown in the UI and used as the ScheduledTaskRun.TaskName key —
    /// don't rename an existing task without accounting for its run history.</summary>
    string Name { get; }

    /// <summary>Short human-readable explanation of what this task does and why, shown on System &gt;
    /// Tasks (hover/tap the info icon next to the task's name).</summary>
    string Description { get; }

    /// <summary>False to keep this task off the System &gt; Tasks page entirely (both the
    /// "Scheduled" row and its "Queue" run history) while still letting it run normally on the
    /// shared scheduler loop. That page is meant for the relatively high-interval maintenance
    /// tasks — a task that runs every few seconds/minutes (e.g. QBittorrentSyncTask) would just
    /// spam its run history and doesn't belong there conceptually.</summary>
    bool ShowInTasksUi => true;

    /// <summary>False to skip persisting a ScheduledTaskRun row (queued/started/ended) and the
    /// per-tick "Running scheduled task" log line for this task entirely — as opposed to
    /// ShowInTasksUi=false, which still records that history, just hides it from System &gt;
    /// Tasks. Use for a task that runs so often (e.g. QBittorrentSyncTask, effectively every
    /// tick of the shared 1-minute loop) that per-run history would just be noise in both the
    /// database and the application logs. Failures still get an ILogger error log — see
    /// ScheduledTaskRunner — since those aren't routine noise the same way successful runs are.</summary>
    bool RecordRunHistory => true;

    TimeSpan GetInterval();

    /// <summary>Does the work and returns a short human-readable summary (e.g. "checked 3623,
    /// linked 3585") shown in the run history. Throw to record the run as failed. A task with a
    /// long-running item loop (e.g. ImageCacheTask) should call progress.Report(...)
    /// periodically — ScheduledTaskRunner persists it (throttled) so System &gt; Tasks can show
    /// live "X / Y" progress on a run that's still going. Optional: a task that finishes quickly
    /// enough not to need it can simply never call Report.</summary>
    Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress);
}
