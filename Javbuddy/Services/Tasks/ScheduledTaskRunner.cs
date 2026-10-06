using System.Diagnostics;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Wraps a single IScheduledTask execution with a persisted ScheduledTaskRun row —
/// queued/started/ended, success/failure, and a summary — so both the automatic scheduler and
/// a manual "Run Now" click get identical history tracking.</summary>
public class ScheduledTaskRunner(IDbContextFactory<AppDbContext> dbFactory, JavbuddyMetrics metrics, ScheduledTaskChangeNotifier changeNotifier, ScheduledTaskCancellationRegistry cancellationRegistry, ILogger<ScheduledTaskRunner> logger)
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly JavbuddyMetrics metrics = metrics;
    private readonly ScheduledTaskChangeNotifier changeNotifier = changeNotifier;
    private readonly ScheduledTaskCancellationRegistry cancellationRegistry = cancellationRegistry;
    private readonly ILogger<ScheduledTaskRunner> logger = logger;

    public async Task RunAsync(IScheduledTask task, CancellationToken ct)
    {
        if (!task.RecordRunHistory)
        {
            await RunWithoutHistoryAsync(task, ct);
            return;
        }

        var run = new ScheduledTaskRun { TaskName = task.Name, QueuedAt = DateTime.UtcNow };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        changeNotifier.NotifyChanged();

        // Linked to the caller's token (app shutdown) plus a per-run source CancelRunAsync can
        // trip from System > Tasks.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellationRegistry.Register(run.Id, runCts);

        string? summary = null;
        string? error = null;
        var success = false;
        var progressReporter = new DbProgressReporter(dbFactory, run.Id, changeNotifier);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            run.StartedAt = DateTime.UtcNow;
            await using (var db = await dbFactory.CreateDbContextAsync(runCts.Token))
            {
                await db.ScheduledTaskRuns
                    .Where(r => r.Id == run.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartedAt, run.StartedAt), runCts.Token);
            }
            changeNotifier.NotifyChanged();

            summary = await task.RunAsync(runCts.Token, progressReporter);
            success = true;
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            error = "Task was cancelled.";
            logger.LogInformation("Scheduled task {TaskName} was cancelled.", task.Name);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "Scheduled task {TaskName} failed.", task.Name);
        }
        finally
        {
            cancellationRegistry.Unregister(run.Id);
            stopwatch.Stop();
            try
            {
                var outcomeTag = new KeyValuePair<string, object?>("outcome", success ? "success" : "failure");
                metrics.ScheduledTaskRuns.Add(1, new KeyValuePair<string, object?>("task", task.Name), outcomeTag);
                metrics.ScheduledTaskDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("task", task.Name));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record metrics for scheduled task {TaskName}.", task.Name);
            }

            try
            {
                await progressReporter.FlushFinalAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to flush final progress for scheduled task {TaskName}.", task.Name);
            }

            run.EndedAt = DateTime.UtcNow;
            run.Success = success;
            run.ResultSummary = summary;
            run.ErrorMessage = error;

            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                await db.ScheduledTaskRuns
                    .Where(r => r.Id == run.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.EndedAt, run.EndedAt)
                        .SetProperty(r => r.Success, run.Success)
                        .SetProperty(r => r.ResultSummary, run.ResultSummary)
                        .SetProperty(r => r.ErrorMessage, run.ErrorMessage),
                        CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist completion state for scheduled task {TaskName}.", task.Name);
            }

            changeNotifier.NotifyChanged();
        }
    }

    /// <summary>Runs a task whose IScheduledTask.RecordRunHistory is false: no ScheduledTaskRun
    /// row, no progress reporting (the task's progress reports are simply discarded), still
    /// records metrics and still logs a failure — only the noisy per-run bookkeeping is
    /// skipped.</summary>
    private async Task RunWithoutHistoryAsync(IScheduledTask task, CancellationToken ct)
    {
        var success = false;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await task.RunAsync(ct, new Progress<TaskProgress>());
            success = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled task {TaskName} failed.", task.Name);
        }
        finally
        {
            stopwatch.Stop();
            try
            {
                var outcomeTag = new KeyValuePair<string, object?>("outcome", success ? "success" : "failure");
                metrics.ScheduledTaskRuns.Add(1, new KeyValuePair<string, object?>("task", task.Name), outcomeTag);
                metrics.ScheduledTaskDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("task", task.Name));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record metrics for scheduled task {TaskName}.", task.Name);
            }
        }
    }

    /// <summary>Marks any ScheduledTaskRun rows that were left running (EndedAt == null) across
    /// an application crash or restart as terminated.</summary>
    public async Task<int> CleanUpOrphanedRunsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updatedCount = await db.ScheduledTaskRuns
            .Where(r => r.EndedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.EndedAt, DateTime.UtcNow)
                .SetProperty(r => r.Success, false)
                .SetProperty(r => r.ErrorMessage, "Interrupted by application restart/shutdown"), ct);

        if (updatedCount > 0)
        {
            logger.LogInformation("Cleaned up {Count} orphaned scheduled task run(s).", updatedCount);
            changeNotifier.NotifyChanged();
        }

        return updatedCount;
    }

    /// <summary>Stops a running task from the UI: signals the run's token so the task's own work
    /// actually halts (RunAsync then records it as "Task was cancelled."). A run this process
    /// isn't executing — an orphan from a crash/restart the startup cleanup missed — has no work
    /// to stop, so its row is just marked failed instead. False when the run is unknown or
    /// already ended.</summary>
    public async Task<bool> CancelRunAsync(int runId, CancellationToken ct = default)
    {
        if (cancellationRegistry.TryCancel(runId))
        {
            logger.LogInformation("Cancellation requested for scheduled task run {RunId}.", runId);
            changeNotifier.NotifyChanged();
            return true;
        }

        return await MarkRunAsFailedAsync(runId, ct: ct);
    }

    /// <summary>Manually terminates an active ScheduledTaskRun row as failed (e.g. from the UI
    /// when a task appears stuck).</summary>
    public async Task<bool> MarkRunAsFailedAsync(int runId, string reason = "Manually marked as failed", CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var taskName = await db.ScheduledTaskRuns
            .Where(r => r.Id == runId && r.EndedAt == null)
            .Select(r => r.TaskName)
            .FirstOrDefaultAsync(ct);

        if (taskName is null) return false;

        await db.ScheduledTaskRuns
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.EndedAt, DateTime.UtcNow)
                .SetProperty(r => r.Success, false)
                .SetProperty(r => r.ErrorMessage, reason), ct);

        logger.LogInformation("Scheduled task run {RunId} ({TaskName}) was manually marked as failed.", runId, taskName);
        changeNotifier.NotifyChanged();
        return true;
    }

    /// <summary>Persists TaskProgress reports to the run's ProgressCurrent/ProgressTotal columns,
    /// throttled to at most once a second — a task like ImageCacheTask can report thousands
    /// of times a minute, and writing every single one would just hammer SQLite for no visible UI
    /// benefit. A write already in flight is not queued behind; the next Report call simply
    /// supersedes it once it lands, so progress never falls far behind even under throttling.
    /// Failures are swallowed — a missed progress update must never fail the task itself.</summary>
    private sealed class DbProgressReporter(IDbContextFactory<AppDbContext> dbFactory, int runId, ScheduledTaskChangeNotifier changeNotifier) : IProgress<TaskProgress>
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

        private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
        private readonly int runId = runId;
        private readonly ScheduledTaskChangeNotifier changeNotifier = changeNotifier;
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private DateTime lastWriteUtc = DateTime.MinValue;
        private TaskProgress? latest;

        public void Report(TaskProgress value)
        {
            latest = value;
            var now = DateTime.UtcNow;
            if (now - lastWriteUtc < MinInterval) return;
            lastWriteUtc = now;
            _ = WriteAsync(value);
        }

        /// <summary>Writes whatever the most recently reported value was, unthrottled — called
        /// once by ScheduledTaskRunner right after the task finishes. No-op if Report was never
        /// called (the task doesn't report progress at all).</summary>
        public async Task FlushFinalAsync()
        {
            if (latest is not { } value) return;
            await writeGate.WaitAsync();
            try
            {
                await WriteCoreAsync(value);
            }
            finally
            {
                writeGate.Release();
            }
        }

        private async Task WriteAsync(TaskProgress value)
        {
            if (!await writeGate.WaitAsync(0)) return;
            try
            {
                await WriteCoreAsync(value);
            }
            finally
            {
                writeGate.Release();
            }
        }

        private async Task WriteCoreAsync(TaskProgress value)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var run = new ScheduledTaskRun { Id = runId };
                db.ScheduledTaskRuns.Attach(run);
                run.ProgressCurrent = value.Current;
                run.ProgressTotal = value.Total;
                run.ProgressStage = value.Stage;
                db.Entry(run).Property(r => r.ProgressCurrent).IsModified = true;
                db.Entry(run).Property(r => r.ProgressTotal).IsModified = true;
                db.Entry(run).Property(r => r.ProgressStage).IsModified = true;
                await db.SaveChangesAsync();
                changeNotifier.NotifyChanged();
            }
            catch
            {
                // Best-effort only — see class docs.
            }
        }
    }
}
