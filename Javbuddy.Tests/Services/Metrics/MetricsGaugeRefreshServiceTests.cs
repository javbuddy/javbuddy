using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Ffmpeg;
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

    [Fact]
    public async Task FirstPass_PublishesLibraryComposition_AndTheFfmpegBinaryInfo()
    {
        if (OperatingSystem.IsWindows()) return;
        var scratch = Directory.CreateTempSubdirectory("javbuddy-refresh-");
        var ffmpeg = Path.Combine(scratch.FullName, "ffmpeg");
        File.WriteAllText(ffmpeg, "#!/bin/sh\necho 'ffmpeg version 7.1.1-static Copyright'\n");
        File.SetUnixFileMode(ffmpeg, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-001", Status = MovieStatus.Got };
            movie.MovieFiles.Add(new MovieFile { FileName = "a.mp4", VideoCodec = "AVC", ContainerFormat = "MPEG-4", Width = 1920 });
            movie.MovieFiles.Add(new MovieFile { FileName = "b.mp4", VideoCodec = "AVC", ContainerFormat = "MPEG-4", Width = 1916 });
            movie.MovieFiles.Add(new MovieFile { FileName = "c.mkv", VideoCodec = "HEVC", ContainerFormat = "Matroska", Width = 3840 });
            movie.MovieFiles.Add(new MovieFile { FileName = "d.mkv" });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var resolver = Substitute.For<IFfmpegBinaryResolver>();
        resolver.FfmpegPath.Returns(ffmpeg);
        resolver.FfprobePath.Returns("/usr/bin/ffprobe");
        resolver.IsAvailable.Returns(true);
        var backups = Substitute.For<IBackupService>();
        backups.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        var cache = Substitute.For<IImageCacheMaintenanceService>();
        cache.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(new ImageCacheStats(0, 0, 0, 0, 0, 0));
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<Javbuddy.Data.AppDbContext>>(factory)
            .AddSingleton(backups)
            .AddSingleton(cache)
            .AddSingleton(resolver)
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
                if (recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_ffmpeg_binary_info")
                    && recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_library_video_container_total")) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            scratch.Delete(recursive: true);
        }

        Dictionary<string, int> Latest(string instrument, string label) => recorder.Measurements
            .Where(m => m.InstrumentName == instrument)
            .GroupBy(m => (string)m.Tags[label]!)
            .ToDictionary(g => g.Key, g => (int)g.Last().Value!);

        Assert.Equal(new Dictionary<string, int> { ["h264"] = 2, ["hevc"] = 1, ["unknown"] = 1 }, Latest("javbuddy_library_video_codec_total", "codec"));
        Assert.Equal(new Dictionary<string, int> { ["mp4"] = 2, ["mkv"] = 1, ["unknown"] = 1 }, Latest("javbuddy_library_video_container_total", "container"));
        Assert.Equal(new Dictionary<string, int> { ["1080p"] = 2, ["4k"] = 1, ["unknown"] = 1 }, Latest("javbuddy_library_video_resolution_total", "resolution_bucket"));
        Assert.Equal(1, (int)recorder.Measurements.Last(m => m.InstrumentName == "javbuddy_ffmpeg_binary_available").Value!);
        var info = recorder.Measurements.Last(m => m.InstrumentName == "javbuddy_ffmpeg_binary_info");
        Assert.Equal(1, (int)info.Value!);
        Assert.Equal("7.1.1", info.Tags["version"]);
    }

    [Fact]
    public async Task FirstPass_WithNoFfmpegBinary_ReportsUnavailable_AndNoInfoSeries()
    {
        using var factory = new TestDbContextFactory();
        var resolver = Substitute.For<IFfmpegBinaryResolver>();
        resolver.IsAvailable.Returns(false);
        var backups = Substitute.For<IBackupService>();
        backups.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        var cache = Substitute.For<IImageCacheMaintenanceService>();
        cache.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(new ImageCacheStats(0, 0, 0, 0, 0, 0));
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<Javbuddy.Data.AppDbContext>>(factory)
            .AddSingleton(backups)
            .AddSingleton(cache)
            .AddSingleton(resolver)
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
                if (recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_ffmpeg_binary_available")) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, (int)recorder.Measurements.Last(m => m.InstrumentName == "javbuddy_ffmpeg_binary_available").Value!);
        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_binary_info");
    }

    private static MetricsGaugeRefreshService ServiceWith(IFfmpegBinaryResolver resolver, JavbuddyMetrics metrics)
    {
        var services = new ServiceCollection().AddSingleton(resolver).BuildServiceProvider();
        return new MetricsGaugeRefreshService(services.GetRequiredService<IServiceScopeFactory>(), metrics, NullLogger<MetricsGaugeRefreshService>.Instance);
    }

    private static string FakeFfmpeg(DirectoryInfo dir, string body)
    {
        var path = Path.Combine(dir.FullName, "ffmpeg");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task FfmpegInfo_IsEmittedWhenOnlyFfprobeIsMissing_ButTheBinaryIsNotAvailable()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-refresh-");
        try
        {
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfmpegPath.Returns(FakeFfmpeg(dir, "echo 'ffmpeg version 7.1.1 Copyright'"));
            resolver.FfprobePath.Returns((string?)null);
            resolver.IsAvailable.Returns(false);
            using var metrics = new JavbuddyMetrics();
            using var recorder = new MetricsRecorder(metrics);
            using var service = ServiceWith(resolver, metrics);

            await service.RefreshFfmpegInfoAsync(CancellationToken.None);
            recorder.SampleGauges();

            Assert.Equal(0, (int)recorder.Measurements.Single(m => m.InstrumentName == "javbuddy_ffmpeg_binary_available").Value!);
            Assert.Equal("7.1.1", recorder.Measurements.Single(m => m.InstrumentName == "javbuddy_ffmpeg_binary_info").Tags["version"]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FfmpegInfo_RetriesAFailedVersionRead_UntilItWorks_ThenStops()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-refresh-");
        try
        {
            var marker = Path.Combine(dir.FullName, "attempted");
            // Fails the first time it runs, works afterwards.
            var ffmpeg = FakeFfmpeg(dir, $"if [ ! -e '{marker}' ]; then touch '{marker}'; exit 1; fi; echo 'ffmpeg version 6.0 Copyright'");
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfmpegPath.Returns(ffmpeg);
            resolver.FfprobePath.Returns("/usr/bin/ffprobe");
            resolver.IsAvailable.Returns(true);
            using var metrics = new JavbuddyMetrics();
            using var recorder = new MetricsRecorder(metrics);
            using var service = ServiceWith(resolver, metrics);

            await service.RefreshFfmpegInfoAsync(CancellationToken.None);
            recorder.SampleGauges();
            Assert.Equal(1, (int)recorder.Measurements.Single(m => m.InstrumentName == "javbuddy_ffmpeg_binary_available").Value!);
            Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_binary_info");

            await service.RefreshFfmpegInfoAsync(CancellationToken.None);
            recorder.SampleGauges();
            Assert.Equal("6.0", recorder.Measurements.Single(m => m.InstrumentName == "javbuddy_ffmpeg_binary_info").Tags["version"]);

            File.Delete(ffmpeg); // done: a third call must not touch the binary again
            await service.RefreshFfmpegInfoAsync(CancellationToken.None);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FfmpegInfo_StillPublished_WhenTheFirstPassFailsEarly()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-refresh-");
        try
        {
            var factory = Substitute.For<Microsoft.EntityFrameworkCore.IDbContextFactory<Javbuddy.Data.AppDbContext>>();
            factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns<Task<Javbuddy.Data.AppDbContext>>(_ => throw new InvalidOperationException("db gone"));
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfmpegPath.Returns(FakeFfmpeg(dir, "echo 'ffmpeg version 7.1.1 Copyright'"));
            resolver.FfprobePath.Returns("/usr/bin/ffprobe");
            resolver.IsAvailable.Returns(true);
            var services = new ServiceCollection()
                .AddSingleton(factory)
                .AddSingleton(Substitute.For<IBackupService>())
                .AddSingleton(Substitute.For<IImageCacheMaintenanceService>())
                .AddSingleton(resolver)
                .BuildServiceProvider();
            using var metrics = new JavbuddyMetrics();
            using var recorder = new MetricsRecorder(metrics);
            using var service = new MetricsGaugeRefreshService(services.GetRequiredService<IServiceScopeFactory>(), metrics, NullLogger<MetricsGaugeRefreshService>.Instance);

            await service.StartAsync(CancellationToken.None);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline && !recorder.Measurements.Any(m => m.InstrumentName == "javbuddy_ffmpeg_binary_info"))
                {
                    recorder.SampleGauges();
                    await Task.Delay(50);
                }
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            Assert.Contains(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_binary_info" && (string)m.Tags["version"]! == "7.1.1");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
