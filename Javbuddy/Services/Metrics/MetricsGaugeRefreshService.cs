using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Metrics;

/// <summary>Refreshes JavbuddyMetrics' DB-/filesystem-backed gauges (image cache size, library
/// counts) on a timer rather than on every /metrics scrape — GetStatsAsync walks the whole cache
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
                    await RefreshAsync(stoppingToken);
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

    private async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var cacheMaintenance = scope.ServiceProvider.GetRequiredService<IImageCacheMaintenanceService>();

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var got = await db.Movies.CountAsync(m => m.Status == MovieStatus.Got, ct);
            var missing = await db.Movies.CountAsync(m => m.Status == MovieStatus.Missing, ct);
            var actors = await db.Actors.CountAsync(ct);
            metrics.UpdateLibraryStats(got, missing, actors);
        }

        var cacheStats = await cacheMaintenance.GetStatsAsync(ct);
        metrics.UpdateImageCacheStats(cacheStats.FileCount, cacheStats.TotalBytes);
    }
}
