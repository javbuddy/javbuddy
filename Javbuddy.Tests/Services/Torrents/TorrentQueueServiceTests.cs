using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentQueueServiceTests
{
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
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), new TorrentChangeNotifier());

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
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), new TorrentChangeNotifier());

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
        var service = new TorrentQueueService(factory, client, notifier);

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
        var service = new TorrentQueueService(factory, Substitute.For<IQBittorrentClient>(), new TorrentChangeNotifier());

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
        var service = new TorrentQueueService(factory, client, notifier);

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
        var service = new TorrentQueueService(factory, client, notifier);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RemoveAsync([id]));

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.TorrentDownloads.SingleAsync();
        Assert.Equal(TorrentDownloadStatus.Downloading, row.Status);
        Assert.Null(row.RemovedFromClientAt);
        Assert.Equal(0, notified);
    }
}
