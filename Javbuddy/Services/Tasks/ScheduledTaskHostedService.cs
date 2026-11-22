using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;

namespace Javbuddy.Services.Tasks;

/// <summary>Wakes up once a minute, and for each registered IScheduledTask, runs it if its
/// interval has elapsed since its last recorded run (or it has never run). One shared loop
/// rather than one timer per task, since checking "is it due" is cheap and this scales fine to
/// however many tasks get registered later. A due task is queued on <see cref="BackgroundJobRunner"/>
/// rather than awaited, so a long one (Library Rescan, Image Cache) never holds up the short periodic
/// ones (qBittorrent Sync, backups); <see cref="ScheduledTaskRunner"/> keeps each task to one run at a time.</summary>
public class ScheduledTaskHostedService(IServiceScopeFactory scopeFactory, ScheduledTaskLauncher launcher, ScheduledTaskRunTracker tracker, ILogger<ScheduledTaskHostedService> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly ScheduledTaskLauncher launcher = launcher;
    private readonly ScheduledTaskRunTracker tracker = tracker;
    private readonly ILogger<ScheduledTaskHostedService> logger = logger;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    // A task that has never run (lastRun is null: a new task, or a fresh/reset database) used to count
    // as immediately due, so it fired on the first tick after every app start, which is surprising for
    // a long task like ImageCacheTask on a large library. Instead it waits a full interval measured from
    // this process's start before it's first due. A task that fell overdue while the app was off (it has
    // run before, but longer ago than its interval) still catches up right after restart.
    private DateTime processStartUtc => tracker.StartedUtc;

    // Old run history is pruned on the first tick and then daily, so a long-running instance doesn't keep it forever.
    private DateTime? lastPruneUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, time, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled task tick failed.");
            }

            try
            {
                await Task.Delay(TickInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Whether a task is due: its interval has passed since its last run, or, for a task that has never
    /// run, since this process started (or at once when <paramref name="runImmediatelyWhenNeverRun"/>).</summary>
    public static bool IsDue(DateTime? lastRunUtc, DateTime nowUtc, DateTime processStartUtc, TimeSpan interval, bool runImmediatelyWhenNeverRun) =>
        lastRunUtc is not null
            ? nowUtc - lastRunUtc.Value >= interval
            : runImmediatelyWhenNeverRun || nowUtc - processStartUtc >= interval;

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetServices<IScheduledTask>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var runner = scope.ServiceProvider.GetRequiredService<ScheduledTaskRunner>();

        if (lastPruneUtc is null || time.GetUtcNow().UtcDateTime - lastPruneUtc.Value >= PruneInterval)
        {
            lastPruneUtc = time.GetUtcNow().UtcDateTime;
            try
            {
                await runner.PruneOldRunsAsync(ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pruning old scheduled task runs failed.");
            }
        }

        foreach (var task in tasks)
        {
            if (tracker.IsRunning(task.Name)) continue;

            bool due;
            // The once-a-minute "is it due" check would otherwise be a one-query trace per task every
            // minute; only an actual run is worth tracing.
            using (SuppressInstrumentationScope.Begin())
            {
                // A task without run history has no row to read, so its in-memory last completion is all there is.
                DateTime? lastRun = null;
                if (task.RecordRunHistory)
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    lastRun = await db.ScheduledTaskRuns
                        .Where(r => r.TaskName == task.Name && r.EndedAt != null)
                        .OrderByDescending(r => r.EndedAt)
                        .Select(r => r.EndedAt)
                        .FirstOrDefaultAsync(ct);
                }
                lastRun = ScheduledTaskRunTracker.Latest(lastRun, tracker.GetLastCompletion(task.Name));

                due = IsDue(lastRun, time.GetUtcNow().UtcDateTime, processStartUtc, task.GetInterval(), task.RunImmediatelyWhenNeverRun);
            }

            if (!due) continue;

            if (task.RecordRunHistory)
            {
                logger.LogInformation("Running scheduled task {TaskName}.", task.Name);
            }
            _ = launcher.Queue(task.Name);
        }
    }
}
