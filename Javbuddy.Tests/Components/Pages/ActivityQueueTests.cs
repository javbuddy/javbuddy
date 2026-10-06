using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers the Sort icon link's per-row visibility on Activity &gt; Queue:
/// it must show for a torrent that's Completed (regression guard) or still Seeding, but not for
/// one still actively Downloading.</summary>
public class ActivityQueueTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(new TorrentChangeNotifier());

        var qbittorrent = Substitute.For<IQBittorrentClient>();
        Services.AddSingleton(qbittorrent);
        Services.AddScoped<ITorrentQueueService, TorrentQueueService>();

        return factory;
    }

    private static TorrentDownload SeedTorrent(TestDbContextFactory factory, TorrentDownloadStatus status)
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
}
