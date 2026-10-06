using Bunit;
using Javbuddy.Components.Layout;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Layout;

/// <summary>Regression: the search dropdown used to render a movie's raw
/// MetaCoverUrl directly as an &lt;img&gt; src. That field is only ever a remote fallback URL —
/// every other page (Movies.razor, ActorDetail.razor) instead requests posters through the
/// /image-cache/{code}/{role}/{variant} endpoint, which prefers a locally cached/cropped image
/// and falls back to the remote URL itself. A movie sourced from the local library (no remote
/// fallback poster) had a null MetaCoverUrl, so the dropdown fell back to a two-letter
/// placeholder even though the movie had a perfectly good cached cover.</summary>
public class SearchBoxTests : BunitContext
{
    private readonly IR18DevReleaseBrowseService catalog = Substitute.For<IR18DevReleaseBrowseService>();

    private ISearchService SetUpServices(SearchResults results)
    {
        var searchService = Substitute.For<ISearchService>();
        searchService.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(results));
        Services.AddSingleton(searchService);
        Services.AddSingleton(catalog);
        catalog.SuggestAsync(default!, default, default).ReturnsForAnyArgs(Array.Empty<R18DevReleaseRow>());
        return searchService;
    }

    [Fact]
    public void MovieWithMetadata_ShowsItsCachedPosterViaTheImageCacheEndpoint()
    {
        var movie = new Movie { Code = "AAA-001", MetaSourceName = "Local" };
        SetUpServices(new SearchResults([movie], []));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("AAA");

        cut.WaitForAssertion(() =>
        {
            var img = cut.Find("img.search-result-poster");
            Assert.Equal("/image-cache/AAA-001/poster/thumb", img.GetAttribute("src"));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MovieWithNoMetadataYet_ShowsThePlaceholderInstead()
    {
        var movie = new Movie { Code = "AAA-001", MetaSourceName = null };
        SetUpServices(new SearchResults([movie], []));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("AAA");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("img.search-result-poster"));
            Assert.Equal("AA", cut.Find(".search-result-poster-placeholder").TextContent);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ActorWithCachedImage_ShowsItsThumbnailViaTheActorImageEndpoint()
    {
        var actor = new ActorSearchResult(42, "Hatano", "Yui", HasImage: true);
        SetUpServices(new SearchResults([], [actor]));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("Hatano");

        cut.WaitForAssertion(() =>
        {
            var img = cut.Find("img.search-result-avatar-image");
            Assert.Equal("/actor-image/42/thumb", img.GetAttribute("src"));
            Assert.Equal("Yui Hatano", img.GetAttribute("alt"));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ActorWithNoCachedImage_ShowsInitialsInstead()
    {
        var actor = new ActorSearchResult(42, "Hatano", "Yui", HasImage: false);
        SetUpServices(new SearchResults([], [actor]));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("Hatano");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("img.search-result-avatar-image"));
            Assert.Equal("YH", cut.Find(".search-result-avatar").TextContent);
        }, TimeSpan.FromSeconds(2));
    }

    // Enter in the search box goes straight to the filtered Movies grid.
    [Fact]
    public void PressingEnter_NavigatesToTheMoviesGridFilteredByTheQuery()
    {
        SetUpServices(new SearchResults([], []));
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<SearchBox>();
        var input = cut.Find("input.search-input");
        input.Input("ABP 1");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("http://localhost/?q=ABP%201", nav.Uri);
        Assert.Empty(cut.FindAll(".search-dropdown"));
        Assert.Equal("", cut.Find("input.search-input").GetAttribute("value"));
    }

    [Fact]
    public void Navigating_ClosesTheOpenDropdown()
    {
        SetUpServices(new SearchResults([], []));
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("ABP");
        Assert.NotEmpty(cut.FindAll(".search-dropdown"));

        nav.NavigateTo("/movies/ABP-001");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".search-dropdown"));
            Assert.Equal("", cut.Find("input.search-input").GetAttribute("value"));
        });
    }

    // The box lives in the layout, so a query left behind would linger in the header.
    [Fact]
    public void LosingFocus_ClearsTheQuery()
    {
        SetUpServices(new SearchResults([], []));

        var cut = Render<SearchBox>();
        var input = cut.Find("input.search-input");
        input.Input("ABP");
        Assert.Equal("ABP", input.GetAttribute("value"));

        cut.Find("input.search-input").FocusOut();

        Assert.Equal("", cut.Find("input.search-input").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".search-dropdown"));
    }

    [Fact]
    public async Task LosingFocus_BeforeThePendingSearchRuns_DoesNotSearchAfterwards()
    {
        var searchService = SetUpServices(new SearchResults([new Movie { Code = "ABP-001" }], []));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("ABP");
        cut.Find("input.search-input").FocusOut();
        cut.Find("input.search-input").Focus();

        await Task.Delay(500);

        await searchService.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".search-result"));
    }

    private static R18DevReleaseRow CatalogRow(string code, string statusClass = "missing") =>
        new(new R18DevFilmographyEntry(code, "Title " + code, null, null, "https://pics.dmm.co.jp/x.jpg"),
            statusClass, statusClass == "missing" ? "Missing" : "Got", $"/movies/{code}", code);

    [Fact]
    public void CatalogMatches_ListOnlyReleasesTheLibraryDoesNotTrack()
    {
        SetUpServices(new SearchResults([], []));
        catalog.SuggestAsync("SSIS", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CatalogRow("SSIS-002"), CatalogRow("SSIS-001", "got") });

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("SSIS");

        cut.WaitForAssertion(() => Assert.Contains("r18.dev Catalog", cut.Markup), TimeSpan.FromSeconds(2));
        var link = cut.FindAll("a.search-result").Single(a => a.TextContent.Contains("SSIS-002"));
        Assert.Equal("/movies/SSIS-002", link.GetAttribute("href"));
        Assert.DoesNotContain(cut.FindAll("a.search-result"), a => a.TextContent.Contains("SSIS-001"));
    }

    [Fact]
    public void NoCatalogMatches_HidesTheCatalogSection()
    {
        SetUpServices(new SearchResults([], []));

        var cut = Render<SearchBox>();
        cut.Find("input.search-input").Input("SSIS");

        cut.WaitForAssertion(() => catalog.Received().SuggestAsync("SSIS", Arg.Any<int>(), Arg.Any<CancellationToken>()), TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("r18.dev Catalog", cut.Markup);
    }
}
