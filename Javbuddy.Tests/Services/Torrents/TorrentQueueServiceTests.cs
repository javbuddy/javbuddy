using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentQueueServiceTests
{
    private sealed class FakeSyncTask(Func<Task<string?>> run) : IScheduledTask
    {
        public string Name => QBittorrentSyncTask.TaskName;
        public string Description => "";
        public TimeSpan GetInterval() => TimeSpan.FromSeconds(30);
        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress) => run();
    }

    private static ScheduledTaskLauncher CreateLauncher(TestDbContextFactory factory, params IScheduledTask[] tasks)
    {
        var tracker = new ScheduledTaskRunTracker();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        services.AddSingleton(new JavbuddyMetrics());
        services.AddSingleton(new ScheduledTaskChangeNotifier());
        services.AddSingleton(Substitute.For<ILogger<ScheduledTaskRunner>>());
        services.AddSingleton<ScheduledTaskCancellationRegistry>();
        services.AddSingleton(tracker);
        services.AddScoped<ScheduledTaskRunner>();
        foreach (var task in tasks) services.AddSingleton(task);
        var provider = services.BuildServiceProvider();
        var jobs = new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        return new ScheduledTaskLauncher(jobs, tracker);
    }

    private static ScheduledTaskLauncher NoTasksLauncher() => CreateLauncher(new TestDbContextFactory());

    private static async Task<int> SeedAsync(TestDbContextFactory factory, string title, string? hash = null,
        DateTime? grabbedAt = null, DateTime? sortedAt = null, DateTime? removedAt = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = $"AAA-{title}", Status = MovieStatus.Missing };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var row = new TorrentDownload
        {
            MovieId = movie.Id,
            MovieCode = movie.Code,
            ReleaseTitle = title,
            Hash = hash,
            Status = TorrentDownloadStatus.Downloading,
            GrabbedAt = grabbedAt ?? DateTime.UtcNow,
            SortedAt = sortedAt,
            RemovedFromClientAt = removedAt
        };
        db.TorrentDownloads.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsUnsortedUnremovedNewestFirst()
    {
        using var factory = new TestDbContextFactory();
        var now = DateTime.UtcNow;
        await SeedAsync(factory, "old", grabbedAt: now.AddHours(-2));
        await SeedAsync(factory, "new", grabbedAt: now);
        await SeedAsync(factory, "sorted", sortedAt: now);
        await SeedAsync(factory, "removed", removedAt: now);
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), NoTasksLauncher(), new TorrentChangeNotifier());

        var rows = await service.GetActiveAsync();

        Assert.Equal(["new", "old"], rows.Select(r => r.ReleaseTitle));
    }

    [Fact]
    public async Task GetActiveCountAsync_CountsUnsortedUnremovedDownloads()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory, "one");
        await SeedAsync(factory, "two");
        await SeedAsync(factory, "sorted", sortedAt: DateTime.UtcNow);
        await SeedAsync(factory, "removed", removedAt: DateTime.UtcNow);
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), NoTasksLauncher(), new TorrentChangeNotifier());

        Assert.Equal(2, await service.GetActiveCountAsync());
    }

    [Fact]
    public async Task RemoveAsync_DeletesHashedTorrentsFromClientAndMarksSelectedRowsRemoved()
    {
        using var factory = new TestDbContextFactory();
        var hashed = await SeedAsync(factory, "hashed", hash: "abc");
        var unhashed = await SeedAsync(factory, "unhashed");
        var untouched = await SeedAsync(factory, "untouched", hash: "zzz");
        var client = Substitute.For<IQBittorrentClient>();
        var notifier = new TorrentChangeNotifier();
        var notified = 0;
        notifier.Changed += () => notified++;
        var service = new TorrentQueueService(factory, client, NoTasksLauncher(), notifier);

        await service.RemoveAsync([hashed, unhashed]);

        await client.Received(1).DeleteTorrentAsync("abc", false, Arg.Any<CancellationToken>());
        await client.DidNotReceive().DeleteTorrentAsync("zzz", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await using var db = await factory.CreateDbContextAsync();
        var byId = await db.TorrentDownloads.ToDictionaryAsync(d => d.Id);
        Assert.Equal(TorrentDownloadStatus.Removed, byId[hashed].Status);
        Assert.NotNull(byId[hashed].RemovedFromClientAt);
        Assert.Equal(TorrentDownloadStatus.Removed, byId[unhashed].Status);
        Assert.Equal(TorrentDownloadStatus.Downloading, byId[untouched].Status);
        Assert.Null(byId[untouched].RemovedFromClientAt);
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task RemoveAsync_IgnoresMissingIds()
    {
        using var factory = new TestDbContextFactory();
        var id = await SeedAsync(factory, "one", hash: "abc");
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), NoTasksLauncher(), new TorrentChangeNotifier());

        await service.RemoveAsync([id, 9999]);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(TorrentDownloadStatus.Removed, (await db.TorrentDownloads.SingleAsync()).Status);
    }

    [Fact]
    public async Task RemoveAsync_EmptySelection_DoesNothingAndDoesNotNotify()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<IQBittorrentClient>();
        var notifier = new TorrentChangeNotifier();
        var notified = 0;
        notifier.Changed += () => notified++;
        var service = new TorrentQueueService(factory, client, NoTasksLauncher(), notifier);

        await service.RemoveAsync([]);

        await client.DidNotReceiveWithAnyArgs().DeleteTorrentAsync(default!, default, default);
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task RemoveAsync_ClientFailure_PropagatesAndLeavesRowsAndListenersUntouched()
    {
        using var factory = new TestDbContextFactory();
        var id = await SeedAsync(factory, "one", hash: "abc");
        var client = Substitute.For<IQBittorrentClient>();
        client.DeleteTorrentAsync("abc", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new HttpRequestException("qBittorrent down"));
        var notifier = new TorrentChangeNotifier();
        var notified = 0;
        notifier.Changed += () => notified++;
        var service = new TorrentQueueService(factory, client, NoTasksLauncher(), notifier);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RemoveAsync([id]));

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.TorrentDownloads.SingleAsync();
        Assert.Equal(TorrentDownloadStatus.Downloading, row.Status);
        Assert.Null(row.RemovedFromClientAt);
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task RefreshAsync_RunsTheSyncTaskThroughTheLauncherAndWaitsForIt()
    {
        using var factory = new TestDbContextFactory();
        var ran = false;
        var launcher = CreateLauncher(factory, new FakeSyncTask(async () =>
        {
            await Task.Delay(50);
            ran = true;
            return "ok";
        }));
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), launcher, new TorrentChangeNotifier());

        await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(ran);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(QBittorrentSyncTask.TaskName, (await db.ScheduledTaskRuns.SingleAsync()).TaskName);
    }

    [Fact]
    public async Task ApplyLiveProgressAsync_CopiesLiveStateOntoMatchingRows()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<IQBittorrentClient>();
        client.GetTorrentsAsync(Arg.Any<CancellationToken>()).Returns(new QBittorrentTorrentsResult(true,
            [new QBittorrentTorrentDto { Hash = "ABC", Progress = 0.5, DlSpeed = 100, Eta = 60 }]));
        var service = new TorrentQueueService(factory, client, NoTasksLauncher(), new TorrentChangeNotifier());
        var row = new TorrentDownload { Hash = "abc" };

        Assert.True(await service.ApplyLiveProgressAsync([row]));

        Assert.Equal(0.5, row.Progress);
        Assert.Equal(100, row.DownloadSpeedBytesPerSec);
    }

    [Fact]
    public async Task ApplyLiveProgressAsync_UnreachableClient_LeavesRowsAlone()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<IQBittorrentClient>();
        client.GetTorrentsAsync(Arg.Any<CancellationToken>()).Returns(new QBittorrentTorrentsResult(false, []));
        var service = new TorrentQueueService(factory, client, NoTasksLauncher(), new TorrentChangeNotifier());
        var row = new TorrentDownload { Hash = "abc", Progress = 0.1 };

        Assert.False(await service.ApplyLiveProgressAsync([row]));

        Assert.Equal(0.1, row.Progress);
    }
}
