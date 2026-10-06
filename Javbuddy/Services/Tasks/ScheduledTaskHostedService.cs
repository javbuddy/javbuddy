using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Wakes up once a minute, and for each registered IScheduledTask, runs it if its
/// interval has elapsed since its last recorded run (or it has never run). One shared loop
/// rather than one timer per task, since checking "is it due" is cheap and this scales fine to
/// however many tasks get registered later.</summary>
public class ScheduledTaskHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly ILogger<ScheduledTaskHostedService> logger = logger;

    // A task that has never run (lastRun is null: a new task, or a fresh/reset database) used to count
    // as immediately due, so it fired on the first tick after every app start, which is surprising for
    // a long task like ImageCacheTask on a large library. Instead it waits a full interval measured from
    // this process's start before it's first due. A task that fell overdue while the app was off (it has
    // run before, but longer ago than its interval) still catches up right after restart.
    private readonly DateTime processStartUtc = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
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
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetServices<IScheduledTask>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var runner = scope.ServiceProvider.GetRequiredService<ScheduledTaskRunner>();

        foreach (var task in tasks)
        {
            DateTime? lastRun;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                lastRun = await db.ScheduledTaskRuns
                    .Where(r => r.TaskName == task.Name && r.EndedAt != null)
                    .OrderByDescending(r => r.EndedAt)
                    .Select(r => r.EndedAt)
                    .FirstOrDefaultAsync(ct);
            }

            var due = lastRun is not null
                ? DateTime.UtcNow - lastRun.Value >= task.GetInterval()
                : DateTime.UtcNow - processStartUtc >= task.GetInterval();
            if (!due) continue;

            if (task.RecordRunHistory)
            {
                logger.LogInformation("Running scheduled task {TaskName}.", task.Name);
            }
            await runner.RunAsync(task, ct);
        }
    }
}
