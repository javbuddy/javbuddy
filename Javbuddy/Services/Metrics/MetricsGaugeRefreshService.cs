using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Metrics;

/// <summary>Refreshes JavbuddyMetrics' DB-/filesystem-backed gauges (image cache size, library
/// counts, last backup, scheduled-task last-success seed) on a timer rather than on every /metrics scrape — GetStatsAsync walks the whole cache
/// directory, too expensive to redo per-request. Separate from ScheduledTaskHostedService's task
/// loop: this is internal housekeeping with no user-visible run history, not a domain task.
/// The first pass runs at startup; after that the refresh is skipped while nothing has scraped the gauges
/// recently, so an app nobody monitors never pays for the directory walk. The first scrape after an idle
/// stretch still sees the last values, but it starts a refresh straight away, so the next scrape is current.</summary>
public class MetricsGaugeRefreshService(IServiceScopeFactory scopeFactory, JavbuddyMetrics metrics, ILogger<MetricsGaugeRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly JavbuddyMetrics metrics = metrics;
    private readonly ILogger<MetricsGaugeRefreshService> logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (first || metrics.WasObservedWithin(JavbuddyMetrics.ObservationIdleAfter))
                    await RefreshAsync(first, stoppingToken);
                first = false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh gauge metrics.");
            }

            try
            {
                await metrics.WaitForScrapeAfterIdleAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(bool first, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var cacheMaintenance = scope.ServiceProvider.GetRequiredService<IImageCacheMaintenanceService>();

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var got = await db.Movies.CountAsync(m => m.Status == MovieStatus.Got, ct);
            var missing = await db.Movies.CountAsync(m => m.Status == MovieStatus.Missing, ct);
            var actors = await db.Actors.CountAsync(a => a.PlaceholderMovieId == null, ct);
            metrics.UpdateLibraryStats(got, missing, actors);
        }

        var cacheStats = await cacheMaintenance.GetStatsAsync(ct);
        metrics.UpdateImageCacheStats(cacheStats.FileCount, cacheStats.TotalBytes);

        // The gauges below are secondary: a failure in one is logged and must not stop the pass (or the others).
        // Backups are also pushed by BackupService on every change; this pass picks up changes made outside it.
        try
        {
            var backups = await scope.ServiceProvider.GetRequiredService<IBackupService>().ListAsync(ct);
            metrics.UpdateLastBackup(backups.Count > 0 ? backups.Max(b => b.Manifest.CreatedUtc) : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the last-backup gauge.");
        }

        // Seeds the scheduled-task last-success gauge from persisted history once at startup so it survives a
        // restart; ScheduledTaskRunner keeps it current afterwards.
        if (first)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var lastSuccesses = await db.ScheduledTaskRuns
                    .Where(r => r.Success && r.EndedAt != null)
                    .GroupBy(r => r.TaskName)
                    .Select(g => new { Task = g.Key, EndedAt = g.Max(r => r.EndedAt) })
                    .ToListAsync(ct);
                foreach (var last in lastSuccesses)
                {
                    if (last.EndedAt is { } endedAt) metrics.RecordScheduledTaskSuccess(last.Task, endedAt);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to seed the scheduled-task last-success gauge.");
            }
        }
    }
}
