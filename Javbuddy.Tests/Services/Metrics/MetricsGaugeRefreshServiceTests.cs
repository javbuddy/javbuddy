using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Images;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Metrics;

public class MetricsGaugeRefreshServiceTests
{
    [Fact]
    public async Task FirstPass_SeedsTaskLastSuccessFromHistory_AndPublishesTheNewestBackup()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.AddRange(
                Run("sync", success: true, ended: new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)),
                Run("sync", success: true, ended: new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc)),
                Run("sync", success: false, ended: new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc)),
                Run("never-ok", success: false, ended: new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        var backups = Substitute.For<IBackupService>();
        var newest = new DateTime(2026, 11, 19, 4, 0, 0, DateTimeKind.Utc);
        backups.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new BackupEntry("a.zip", BackupKind.Auto, 1, new BackupManifest("1", newest.AddDays(-3), "m", 0, 0, 0, 0)),
            new BackupEntry("b.zip", BackupKind.Manual, 1, new BackupManifest("1", newest, "m", 0, 0, 0, 0)),
        ]);
        var cache = Substitute.For<IImageCacheMaintenanceService>();
        cache.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(new ImageCacheStats(0, 0, 0, 0, 0, 0));
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<Javbuddy.Data.AppDbContext>>(factory)
            .AddSingleton(backups)
            .AddSingleton(cache)
            .BuildServiceProvider();

        using var metrics = new JavbuddyMetrics(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(newest.AddHours(5))));
        using var recorder = new MetricsRecorder(metrics);
        using var service = new MetricsGaugeRefreshService(services.GetRequiredService<IServiceScopeFactory>(), metrics, NullLogger<MetricsGaugeRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                recorder.SampleGauges();
                if (recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_last_backup_age_seconds")
                    && recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_scheduled_task_last_success_timestamp_seconds")) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(5 * 3600d, recorder.Measurements.First(m => m.InstrumentName == "javbuddy_last_backup_age_seconds").Value);
        var stamps = recorder.Measurements
            .Where(m => m.InstrumentName == "javbuddy_scheduled_task_last_success_timestamp_seconds")
            .GroupBy(m => (string)m.Tags["task"]!)
            .ToDictionary(g => g.Key, g => (double)g.Last().Value!);
        Assert.Equal(["sync"], stamps.Keys);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), stamps["sync"]);
    }

    private static ScheduledTaskRun Run(string task, bool success, DateTime ended) =>
        new() { TaskName = task, QueuedAt = ended.AddMinutes(-1), StartedAt = ended.AddSeconds(-30), EndedAt = ended, Success = success };

    [Fact]
    public async Task BackupListingFailure_DoesNotStopTheCacheAndLibraryGauges()
    {
        using var factory = new TestDbContextFactory();
        var backups = Substitute.For<IBackupService>();
        backups.ListAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<BackupEntry>>>(_ => throw new IOException("disk gone"));
        var cache = Substitute.For<IImageCacheMaintenanceService>();
        cache.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(new ImageCacheStats(0, 0, 3, 1000, 0, 0));
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<Javbuddy.Data.AppDbContext>>(factory)
            .AddSingleton(backups)
            .AddSingleton(cache)
            .BuildServiceProvider();

        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var service = new MetricsGaugeRefreshService(services.GetRequiredService<IServiceScopeFactory>(), metrics, NullLogger<MetricsGaugeRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                recorder.SampleGauges();
                if (recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_image_cache_files" && (int)m.Value! == 3)) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(recorder.Measurements, m => m.InstrumentName == "javbuddy_image_cache_files" && (int)m.Value! == 3);
        Assert.Contains(recorder.Measurements, m => m.InstrumentName == "javbuddy_image_cache_bytes" && (long)m.Value! == 1000);
        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_last_backup_age_seconds");
    }
}
