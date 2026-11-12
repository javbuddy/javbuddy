using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers the Sort icon link's per-row visibility on Activity &gt; Queue:
/// it must show for a torrent that's Completed (regression guard) or still Seeding, but not for
/// one still actively Downloading.</summary>
public class ActivityQueueTests : BunitContext
{
    private readonly FakeTimeProvider clock = new();
    private IQBittorrentClient qbittorrent = null!;

    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(new TorrentChangeNotifier());

        Services.AddSingleton<TimeProvider>(clock);
        qbittorrent = Substitute.For<IQBittorrentClient>();
        Services.AddSingleton(qbittorrent);
        Services.AddScoped<ITorrentQueueService, TorrentQueueService>();

        return factory;
    }

    private static TorrentDownload SeedTorrent(TestDbContextFactory factory, TorrentDownloadStatus status, string? hash = null)
    {
        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = $"AAA-{status}", Status = MovieStatus.Missing };
        db.Movies.Add(movie);
        db.SaveChanges();

        var download = new TorrentDownload
        {
            MovieId = movie.Id,
            MovieCode = movie.Code,
            ReleaseTitle = $"{status} Release",
            Status = status,
            Hash = hash,
            // Deliberately no Hash: the live-poll loop (OnAfterRender) skips calling
            // IQBittorrentClient.GetTorrentsAsync entirely when no row has a Hash, so these tests
            // don't need to coordinate with that background timer.
        };
        db.TorrentDownloads.Add(download);
        db.SaveChanges();
        return download;
    }

    [Fact]
    public void SeedingTorrent_ShowsSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Seeding);

        var cut = Render<ActivityQueue>();

        Assert.NotNull(cut.Find("a.activity-sort-link"));
    }

    [Fact]
    public void CompletedTorrent_ShowsSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Completed);

        var cut = Render<ActivityQueue>();

        Assert.NotNull(cut.Find("a.activity-sort-link"));
    }

    [Fact]
    public void DownloadingTorrent_DoesNotShowSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Downloading);

        var cut = Render<ActivityQueue>();

        Assert.Empty(cut.FindAll("a.activity-sort-link"));
    }

    [Theory]
    [InlineData(TorrentDownloadStatus.Downloading, true)]
    [InlineData(TorrentDownloadStatus.Queued, true)]
    [InlineData(TorrentDownloadStatus.Seeding, false)]
    [InlineData(TorrentDownloadStatus.Completed, false)]
    [InlineData(TorrentDownloadStatus.Error, false)]
    public void NeedsLivePoll_OnlyForRowsStillDownloading(TorrentDownloadStatus status, bool expected)
    {
        var row = new TorrentDownload { Status = status, Hash = "abc" };

        Assert.Equal(expected, ActivityQueue.NeedsLivePoll([row]));
    }

    [Fact]
    public void NeedsLivePoll_FalseWithoutHashOrOnceFullyDownloaded()
    {
        Assert.False(ActivityQueue.NeedsLivePoll([new TorrentDownload { Status = TorrentDownloadStatus.Downloading }]));
        Assert.False(ActivityQueue.NeedsLivePoll([new TorrentDownload { Status = TorrentDownloadStatus.Downloading, Hash = "abc", Progress = 1 }]));
        Assert.True(ActivityQueue.NeedsLivePoll([new TorrentDownload { Status = TorrentDownloadStatus.Downloading, Hash = "abc", Progress = 0.4 }]));
    }

    [Fact]
    public async Task SeedingOnlyQueue_DoesNotPollQBittorrent()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Seeding, hash: "abc");

        var cut = Render<ActivityQueue>();
        clock.Advance(TimeSpan.FromSeconds(4));
        await cut.InvokeAsync(() => { });
        clock.Advance(TimeSpan.FromSeconds(4));
        await cut.InvokeAsync(() => { });

        await qbittorrent.DidNotReceiveWithAnyArgs().GetTorrentsAsync(default);
    }

    [Fact]
    public async Task DownloadingRow_IsPolledEveryTick()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Downloading, hash: "abc");
        qbittorrent.GetTorrentsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var cut = Render<ActivityQueue>();
        clock.Advance(TimeSpan.FromSeconds(4));

        await cut.WaitForAssertionAsync(() => qbittorrent.Received(1).GetTorrentsAsync(Arg.Any<CancellationToken>()));
    }
}
