using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Metrics;

/// <summary>Refreshes JavbuddyMetrics' DB-/filesystem-backed gauges (image cache size, library
/// counts) on a timer rather than on every /metrics scrape — GetStatsAsync walks the whole cache
/// directory, too expensive to redo per-request. Separate from ScheduledTaskHostedService's task
/// loop: this is internal housekeeping with no user-visible run history, not a domain task.</summary>
public class MetricsGaugeRefreshService(IServiceScopeFactory scopeFactory, JavbuddyMetrics metrics, ILogger<MetricsGaugeRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly JavbuddyMetrics metrics = metrics;
    private readonly ILogger<MetricsGaugeRefreshService> logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh gauge metrics.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
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
