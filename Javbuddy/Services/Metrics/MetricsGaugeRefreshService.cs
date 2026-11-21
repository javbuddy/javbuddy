using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.Tasks;
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

    // The version read gives up after this many failed attempts rather than starting ffmpeg every pass forever.
    private const int MaxFfmpegVersionAttempts = 5;
    private bool taskSeedDone;
    private bool ffmpegInfoDone;
    private int ffmpegVersionAttempts;

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

            // Last and in its own try, so a failure above can't skip it and its (up to 10 s) read doesn't delay the gauges above.
            try
            {
                await RefreshFfmpegInfoAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to determine the ffmpeg binary info.");
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

    /// <summary>Publishes whether both ffmpeg and ffprobe were found and the ffmpeg version (read independently of ffprobe).
    /// The resolver's paths never change, so this is done once, except that a failed version read is retried on later
    /// passes (up to a few times). Public so the retry can be tested without waiting for the refresh timer.</summary>
    public async Task RefreshFfmpegInfoAsync(CancellationToken ct)
    {
        if (ffmpegInfoDone) return;

        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFfmpegBinaryResolver>();
        var version = resolver.FfmpegPath is { } ffmpegPath ? await FfmpegVersionReader.ReadAsync(ffmpegPath, ct) : null;
        metrics.UpdateFfmpegBinary(resolver.IsAvailable, version);
        if (version is not null || resolver.FfmpegPath is null || ++ffmpegVersionAttempts >= MaxFfmpegVersionAttempts)
            ffmpegInfoDone = true;
    }

    private static Dictionary<string, int> Merge(IEnumerable<(string Label, int Count)> counts)
    {
        var merged = new Dictionary<string, int>();
        foreach (var (label, count) in counts) merged[label] = merged.GetValueOrDefault(label) + count;
        return merged;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var cacheMaintenance = scope.ServiceProvider.GetRequiredService<IImageCacheMaintenanceService>();

        // Every block is isolated: a failure in one is logged and must not stop the pass (or the others).
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var got = await db.Movies.CountAsync(m => m.Status == MovieStatus.Got, ct);
            var missing = await db.Movies.CountAsync(m => m.Status == MovieStatus.Missing, ct);
            var actors = await db.Actors.CountAsync(a => a.PlaceholderMovieId == null, ct);
            metrics.UpdateLibraryStats(got, missing, actors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the movie and actor count gauges.");
        }

        try
        {
            var cacheStats = await cacheMaintenance.GetStatsAsync(ct);
            metrics.UpdateImageCacheStats(cacheStats.FileCount, cacheStats.TotalBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the image cache gauges.");
        }

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

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Grouped in SQL on the raw stored values; normalized into bounded label values afterwards and merged.
            var codecs = await db.MovieFiles.GroupBy(f => f.VideoCodec).Select(g => new { Value = g.Key, Count = g.Count() }).ToListAsync(ct);
            var containers = await db.MovieFiles.GroupBy(f => f.ContainerFormat).Select(g => new { Value = g.Key, Count = g.Count() }).ToListAsync(ct);
            var widths = await db.MovieFiles.GroupBy(f => f.Width).Select(g => new { Value = g.Key, Count = g.Count() }).ToListAsync(ct);
            metrics.UpdateLibraryComposition(
                Merge(codecs.Select(c => (LibraryMediaLabels.Codec(c.Value), c.Count))),
                Merge(widths.Select(w => (LibraryMediaLabels.Resolution(w.Value), w.Count))),
                Merge(containers.Select(c => (LibraryMediaLabels.Container(c.Value), c.Count))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the library composition gauges.");
        }

        // Seeds the scheduled-task last-success gauge from persisted history so it survives a restart (retried on
        // later passes until it has worked once); ScheduledTaskRunner keeps it current afterwards.
        if (!taskSeedDone)
        {
            try
            {
                // Only tasks that are registered now (by the name the runner records), so a retired or renamed task
                // doesn't keep a frozen series.
                var registered = scope.ServiceProvider.GetServices<IScheduledTask>().Select(t => t.Name).ToHashSet();
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var lastSuccesses = await db.ScheduledTaskRuns
                    .Where(r => r.Success && r.EndedAt != null)
                    .GroupBy(r => r.TaskName)
                    .Select(g => new { Task = g.Key, EndedAt = g.Max(r => r.EndedAt) })
                    .ToListAsync(ct);
                foreach (var last in lastSuccesses)
                {
                    if (registered.Contains(last.Task) && last.EndedAt is { } endedAt) metrics.RecordScheduledTaskSuccess(last.Task, endedAt);
                }
                taskSeedDone = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to seed the scheduled-task last-success gauge.");
            }
        }
    }
}
