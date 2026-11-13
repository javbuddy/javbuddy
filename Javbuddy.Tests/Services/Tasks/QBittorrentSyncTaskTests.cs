using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class QBittorrentSyncTaskTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public TorrentChangeNotifier Notifier { get; } = new();
        public IQBittorrentClient Client { get; } = Substitute.For<IQBittorrentClient>();
        public int Notifications { get; private set; }

        public Fixture() => Notifier.Changed += () => Notifications++;

        public async Task SeedAsync(Action<TorrentDownload> configure)
        {
            await using var db = await Factory.CreateDbContextAsync();
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            var row = new TorrentDownload { Movie = movie, MovieCode = "ABC-123" };
            configure(row);
            db.TorrentDownloads.Add(row);
            await db.SaveChangesAsync();
        }

        public async Task<TorrentDownload> RunAsync(params QBittorrentTorrentDto[] live)
        {
            Client.GetTorrentsAsync(Arg.Any<CancellationToken>()).Returns([.. live]);
            var task = new QBittorrentSyncTask(Client, Factory, new ConfigurationBuilder().Build(), Notifier);
            await task.RunAsync(CancellationToken.None, new Progress<TaskProgress>());
            await using var db = await Factory.CreateDbContextAsync();
            return await db.TorrentDownloads.SingleAsync();
        }

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task RunAsync_StatusChanged_NotifiesAndSaves()
    {
        using var f = new Fixture();
        await f.SeedAsync(r => { r.Hash = "h1"; r.Status = TorrentDownloadStatus.Downloading; });

        var row = await f.RunAsync(new QBittorrentTorrentDto { Hash = "h1", State = "uploading", Progress = 1 });

        Assert.Equal(TorrentDownloadStatus.Seeding, row.Status);
        Assert.Equal(1, f.Notifications);
    }

    [Fact]
    public async Task RunAsync_HashCorrelated_Notifies()
    {
        using var f = new Fixture();
        await f.SeedAsync(r => r.Status = TorrentDownloadStatus.Queued);

        var row = await f.RunAsync(new QBittorrentTorrentDto { Hash = "h1", State = "downloading", Tags = "ABC-123" });

        Assert.Equal("h1", row.Hash);
        Assert.Equal(1, f.Notifications);
    }

    [Fact]
    public async Task RunAsync_RemovedFromClient_Notifies()
    {
        using var f = new Fixture();
        await f.SeedAsync(r => { r.Hash = "h1"; r.Status = TorrentDownloadStatus.Downloading; });

        var row = await f.RunAsync();

        Assert.NotNull(row.RemovedFromClientAt);
        Assert.Equal(1, f.Notifications);
    }

    [Fact]
    public async Task RunAsync_OnlyProgressChanged_DoesNotNotify()
    {
        using var f = new Fixture();
        await f.SeedAsync(r => { r.Hash = "h1"; r.Status = TorrentDownloadStatus.Downloading; r.Progress = 0.1; });

        var row = await f.RunAsync(new QBittorrentTorrentDto { Hash = "h1", State = "downloading", Progress = 0.5, DlSpeed = 1000, Eta = 60 });

        Assert.Equal(0.5, row.Progress);
        Assert.Equal(0, f.Notifications);
    }

    [Fact]
    public async Task RunAsync_NothingMatches_DoesNotNotify()
    {
        using var f = new Fixture();
        await f.SeedAsync(r => r.Status = TorrentDownloadStatus.Queued);

        await f.RunAsync();

        Assert.Equal(0, f.Notifications);
    }
}
