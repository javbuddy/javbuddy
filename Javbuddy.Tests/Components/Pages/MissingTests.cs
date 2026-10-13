using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MissingTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IMovieGridQueryService>(new MovieGridQueryService(factory));
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ProwlarrSearchResult(true, [], null));
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return factory;
    }

    [Fact]
    public void SortDescendingByDefault_ShowsNewestReleaseFirst()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2020, 1, 1) });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2023, 1, 1) });
            db.SaveChanges();
        }

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(new[] { "AAA-002", "AAA-001" }, codes);
    }

    [Fact]
    public void ViewStateCookie_RestoresAscendingSortOnFreshLoad()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2020, 1, 1) });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2023, 1, 1) });
            db.SaveChanges();
        }

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-missing-view=" + Uri.EscapeDataString("""{"SortDescending":false}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-002" }, codes);
    }

    private static List<string> Codes(IRenderedComponent<Missing> cut) =>
        cut.FindAll(".missing-row-item > .missing-code").Select(e => e.TextContent).ToList();

    private static void AddDatedMissingMovies(TestDbContextFactory factory, int count)
    {
        using var db = factory.CreateDbContext();
        for (var i = 1; i <= count; i++)
        {
            db.Movies.Add(new Movie { Code = $"MIS-{i:000}", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2000, 1, 1).AddDays(i) });
        }
        db.SaveChanges();
    }

    [Fact]
    public void LongList_RendersOnlyTheFirstWindow_WithTheTotalForTheSpacers()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(48, codes.Count);
        Assert.Equal("MIS-120", codes[0]);
        Assert.Equal("120", cut.Find(".virtualized-grid-spacer").GetAttribute("data-total-count"));
        Assert.Equal("0", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
    }

    [Fact]
    public async Task ScrollingToTheEnd_LoadsTheLastWindow_SoEveryMovieStaysReachable()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);
        var cut = Render<Missing>();

        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(100, 40, 1));

        cut.WaitForAssertion(() =>
        {
            var codes = Codes(cut);
            Assert.Equal(20, codes.Count);
            Assert.Equal("MIS-020", codes[0]);
            Assert.Equal("MIS-001", codes[^1]);
            Assert.Equal("100", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
        });
    }

    [Fact]
    public async Task TogglingSort_ReloadsFromTheTopInTheOtherOrder()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);
        var cut = Render<Missing>();
        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(100, 40, 1));

        cut.Find(".missing-sortable").Click();

        cut.WaitForAssertion(() =>
        {
            var codes = Codes(cut);
            Assert.Equal("MIS-001", codes[0]);
            Assert.Equal("0", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
        });
    }

    [Fact]
    public void NoMissingMovies_ShowsTheEmptyMessage()
    {
        using var factory = SetUpServices();

        var cut = Render<Missing>();

        Assert.Contains("Nothing missing", cut.Markup);
        Assert.Empty(Codes(cut));
    }

    [Fact]
    public void SearchButton_OpensTheSearchModalForThatMovie()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 3);
        var cut = Render<Missing>();

        cut.FindAll(".missing-row-item .missing-icon-btn")[0].Click();

        var modal = cut.FindComponent<ProwlarrSearchModal>();
        Assert.True(modal.Instance.Show);
        Assert.Equal("MIS-003", modal.Instance.Movie?.Code);
    }

    [Fact]
    public void TogglingSort_WritesViewStateCookie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<Missing>();
        cut.Find(".missing-sortable").Click();

        cut.WaitForAssertion(
            () => Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "lsJsonCookie.set" && inv.Arguments.Any(a => a?.ToString() == "javbuddy-missing-view")),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TogglingSort_WhenCircuitDisconnected_DoesNotThrow()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.SaveChanges();
        }
        JSInterop.SetupVoid("lsJsonCookie.set", _ => true).SetException(new JSDisconnectedException("Circuit disconnected."));

        var cut = Render<Missing>();
        cut.Find(".missing-sortable").Click();

        cut.WaitForAssertion(
            () => Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "lsJsonCookie.set"),
            TimeSpan.FromSeconds(2));
    }
}
