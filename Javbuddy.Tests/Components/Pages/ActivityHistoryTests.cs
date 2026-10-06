using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers the Sort icon link's per-row visibility on Activity &gt; History:
/// it must show for a torrent that's Completed (regression guard) or still Seeding, but not for
/// one still actively Downloading.</summary>
public class ActivityHistoryTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(new TorrentChangeNotifier());
        Services.AddScoped<ITorrentHistoryService, TorrentHistoryService>();
        Services.AddScoped<IDeletedMovieService>(_ => new DeletedMovieService(factory, new MovieChangeNotifier()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        return factory;
    }

    private static TorrentDownload SeedTorrent(
        TestDbContextFactory factory,
        TorrentDownloadStatus status,
        DateTime? removedFromClientAt = null,
        DateTime? sortedAt = null)
    {
        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = $"AAA-{status}-{Guid.NewGuid():N}", Status = MovieStatus.Missing };
        db.Movies.Add(movie);
        db.SaveChanges();

        var download = new TorrentDownload
        {
            MovieId = movie.Id,
            MovieCode = movie.Code,
            ReleaseTitle = $"{status} Release",
            Status = status,
            RemovedFromClientAt = removedFromClientAt,
            SortedAt = sortedAt,
        };
        db.TorrentDownloads.Add(download);
        db.SaveChanges();
        return download;
    }

    [Fact]
    public void SeedingTorrent_ShowsSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Seeding, removedFromClientAt: DateTime.UtcNow);

        var cut = Render<ActivityHistory>();

        Assert.NotNull(cut.Find("a.activity-sort-link"));
    }

    [Fact]
    public void CompletedTorrent_ShowsSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Completed, removedFromClientAt: DateTime.UtcNow);

        var cut = Render<ActivityHistory>();

        Assert.NotNull(cut.Find("a.activity-sort-link"));
    }

    [Fact]
    public void DownloadingTorrent_WhenRemoved_DoesNotShowSortLink()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Downloading, removedFromClientAt: DateTime.UtcNow);

        var cut = Render<ActivityHistory>();

        Assert.Empty(cut.FindAll("a.activity-sort-link"));
    }

    [Fact]
    public void ActiveQueueTorrent_DoesNotAppearInHistory()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Downloading, removedFromClientAt: null, sortedAt: null);

        var cut = Render<ActivityHistory>();

        Assert.Empty(cut.FindAll(".activity-row-history:not(.activity-header)"));
        Assert.Contains("No history yet.", cut.Markup);
    }

    [Fact]
    public void SortedTorrent_AppearsInHistory_WithSortedBadge()
    {
        using var factory = SetUpServices();
        var sortedAt = DateTime.UtcNow.AddMinutes(-5);
        SeedTorrent(factory, TorrentDownloadStatus.Completed, sortedAt: sortedAt);

        var cut = Render<ActivityHistory>();

        var badge = cut.Find("a.activity-sorted-badge");
        Assert.NotNull(badge);
        Assert.Equal("Sorted", badge.TextContent.Trim());
    }

    [Fact]
    public void RemovedTorrent_AppearsInHistory()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Removed, removedFromClientAt: DateTime.UtcNow);

        var cut = Render<ActivityHistory>();

        var rows = cut.FindAll(".activity-row-history:not(.activity-header)");
        Assert.Single(rows);
    }

    private static void SeedHistory(TestDbContextFactory factory, int count)
    {
        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "AAA-1", Status = MovieStatus.Missing };
        db.Movies.Add(movie);
        db.SaveChanges();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= count; i++)
        {
            db.TorrentDownloads.Add(new TorrentDownload
            {
                MovieId = movie.Id,
                MovieCode = movie.Code,
                ReleaseTitle = $"Release {i:000}",
                Status = TorrentDownloadStatus.Completed,
                GrabbedAt = start.AddMinutes(i),
                RemovedFromClientAt = start.AddMinutes(i),
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public void ManyEntries_ShowsOnlyTheFirstPageNewestFirst_WithAnOlderLinkAndNoNewerLink()
    {
        using var factory = SetUpServices();
        SeedHistory(factory, 120);

        var cut = Render<ActivityHistory>();

        var rows = cut.FindAll(".activity-row-history:not(.activity-header)");
        Assert.Equal(50, rows.Count);
        Assert.Contains("Release 120", rows[0].TextContent);
        Assert.Contains("Page 1 of 3 · 120 entries", cut.Find(".activity-pager-status").TextContent);
        Assert.Equal("/activity/history?page=2", cut.Find("a[rel=next]").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a[rel=prev]"));
    }

    [Fact]
    public void LastPage_ShowsTheRemainingEntriesAndOnlyANewerLink()
    {
        using var factory = SetUpServices();
        SeedHistory(factory, 120);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/activity/history?page=3");
        var cut = Render<ActivityHistory>();

        Assert.Equal(20, cut.FindAll(".activity-row-history:not(.activity-header)").Count);
        Assert.Equal("/activity/history?page=2", cut.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a[rel=next]"));
    }

    [Fact]
    public void FewEntries_ShowNoPager()
    {
        using var factory = SetUpServices();
        SeedHistory(factory, 3);

        var cut = Render<ActivityHistory>();

        Assert.Empty(cut.FindAll(".activity-pager"));
    }

    private static void SeedDeletedMovies(TestDbContextFactory factory, int count)
    {
        using var db = factory.CreateDbContext();
        db.DeletedMovies.AddRange(Enumerable.Range(1, count).Select(i => new DeletedMovie
        {
            Code = $"ABC-{i:000}",
            NormalizedCode = $"ABC{i:000}",
            CanonicalKey = $"ABC-{i:000}",
            MetaTitle = $"Title {i}",
            PreviousStatus = MovieStatus.Got,
            DeletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
        }));
        db.SaveChanges();
    }

    private IRenderedComponent<ActivityHistory> RenderAt(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        return Render<ActivityHistory>();
    }

    [Fact]
    public void DeletedView_ListsHistoryNewestFirst_AndTabsSwitchViaTheUrl()
    {
        using var factory = SetUpServices();
        SeedTorrent(factory, TorrentDownloadStatus.Completed, removedFromClientAt: DateTime.UtcNow);
        SeedDeletedMovies(factory, 2);

        var cut = RenderAt("/activity/history");
        cut.WaitForAssertion(() => Assert.Contains("Completed Release", cut.Markup));
        Assert.DoesNotContain("ABC-001", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Deleted movies").Click();
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/activity/history?view=deleted", nav.Uri);
        cut.WaitForAssertion(() => Assert.Equal(["ABC-002", "ABC-001"], cut.FindAll(".activity-row-deleted:not(.activity-header) a.activity-movie-link").Select(a => a.TextContent)));
        Assert.Contains("Title 2", cut.Markup);
        Assert.DoesNotContain("Completed Release", cut.Markup);
        Assert.Contains("btn-secondary", cut.FindAll("button").Single(b => b.TextContent.Trim() == "Deleted movies").ClassList);
        Assert.Contains("btn-outline-secondary", cut.FindAll("button").Single(b => b.TextContent.Trim() == "Downloads").ClassList);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Downloads").Click();
        Assert.EndsWith("/activity/history", nav.Uri);
        cut.WaitForAssertion(() => Assert.Contains("Completed Release", cut.Markup));
    }

    [Fact]
    public void DeletedView_Empty_SaysSo()
    {
        using var factory = SetUpServices();
        var cut = RenderAt("/activity/history?view=deleted");
        cut.WaitForAssertion(() => Assert.Contains("No deleted movies recorded.", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Purge all history");
    }

    [Fact]
    public void DeletedView_PurgeAll_RequiresConfirmation_CancelKeepsHistory()
    {
        using var factory = SetUpServices();
        SeedDeletedMovies(factory, 2);
        var cut = RenderAt("/activity/history?view=deleted");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".activity-row-deleted:not(.activity-header)").Count));

        cut.FindAll("button").Single(b => b.TextContent == "Purge all history").Click();
        cut.Find(".delete-confirm-cancel-btn").Click();
        Assert.Equal(2, cut.FindAll(".activity-row-deleted:not(.activity-header)").Count);

        cut.FindAll("button").Single(b => b.TextContent == "Purge all history").Click();
        cut.Find(".delete-confirm-confirm-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("No deleted movies recorded.", cut.Markup));
        using var db = factory.CreateDbContext();
        Assert.Empty(db.DeletedMovies);
    }

    [Fact]
    public void DeletedView_PurgeOne_RemovesOnlyThatRecord()
    {
        using var factory = SetUpServices();
        SeedDeletedMovies(factory, 2);
        var cut = RenderAt("/activity/history?view=deleted");
        cut.WaitForAssertion(() => cut.Find("[aria-label='Purge history for ABC-001']"));

        cut.Find("[aria-label='Purge history for ABC-001']").Click();
        cut.Find(".delete-confirm-confirm-btn").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".activity-row-deleted:not(.activity-header)")));
        using var db = factory.CreateDbContext();
        Assert.Equal("ABC-002", db.DeletedMovies.Single().Code);
    }

    [Fact]
    public void DeletedView_PurgingLastRowOnLastPage_StepsBackAPage()
    {
        using var factory = SetUpServices();
        SeedDeletedMovies(factory, DeletedMovieService.PageSize + 1);
        var cut = RenderAt("/activity/history?view=deleted&page=2");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".activity-row-deleted:not(.activity-header)")));
        Assert.Contains("Page 2 of 2", cut.Markup);
        Assert.Equal("/activity/history?view=deleted", cut.Find("a[rel=prev]").GetAttribute("href"));

        cut.Find("[aria-label='Purge history for ABC-001']").Click();
        cut.Find(".delete-confirm-confirm-btn").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/activity/history?view=deleted", nav.Uri));
        cut.WaitForAssertion(() => Assert.Equal(DeletedMovieService.PageSize, cut.FindAll(".activity-row-deleted:not(.activity-header)").Count));
        Assert.DoesNotContain("Page 2 of", cut.Markup);
    }
}
