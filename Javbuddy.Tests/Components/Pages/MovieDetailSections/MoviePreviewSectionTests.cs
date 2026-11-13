using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MoviePreviewSectionTests : BunitContext
{
    private readonly IMovieAddService addService = Substitute.For<IMovieAddService>();
    private readonly MovieFilterNavigationState filterState = new();

    public MoviePreviewSectionTests()
    {
        Services.AddSingleton(addService);
        Services.AddSingleton(filterState);
        // The related releases shelf's own Prowlarr search dialog.
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Torrents.ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Prowlarr.IProwlarrClient>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static R18DevMovieDetail Preview() => new("MIDE-001", "A Preview", null, null, null, null, null, null, null, null, null, null, [], [], null, []);

    private IRenderedComponent<MoviePreviewSection> RenderSection(Action<ComponentParameterCollectionBuilder<MoviePreviewSection>>? configure = null) =>
        Render<MoviePreviewSection>(p =>
        {
            p.Add(x => x.Preview, Preview()).Add(x => x.BackHref, "/").Add(x => x.BackLabel, "All movies");
            configure?.Invoke(p);
        });

    [Fact]
    public void ShowsTheToolbarAndThePreview()
    {
        var cut = RenderSection();

        var buttons = cut.FindAll(".page-toolbar .page-toolbar-btn").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("All movies", buttons);
        Assert.Contains("Add to Library", buttons);
        Assert.Contains("Search Prowlarr", buttons);
        Assert.NotEmpty(cut.FindAll(".movie-hero"));
    }

    [Fact]
    public async Task AddToLibrary_AddsTheRelease_AndTellsThePage()
    {
        var added = 0;
        var cut = RenderSection(p => p.Add(x => x.OnAdded, () => added++));

        await cut.InvokeAsync(() => cut.FindAll(".page-toolbar-btn").First(b => b.TextContent.Contains("Add to Library")).Click());

        await addService.Received(1).AddAsync("MIDE-001", Arg.Any<CancellationToken>());
        Assert.Equal(1, added);
    }

    [Fact]
    public async Task AFailedAdd_ShowsTheReason_AndDoesNotTellThePage()
    {
        addService.AddAsync("MIDE-001", Arg.Any<CancellationToken>()).Returns<Task<MovieAddResult>>(_ => throw new InvalidOperationException("no source"));
        var added = 0;
        var cut = RenderSection(p => p.Add(x => x.OnAdded, () => added++));

        await cut.InvokeAsync(() => cut.FindAll(".page-toolbar-btn").First(b => b.TextContent.Contains("Add to Library")).Click());

        Assert.Contains("Couldn't add MIDE-001 to the library: no source", cut.Find(".alert-danger").TextContent);
        Assert.Equal(0, added);
        Assert.Contains("Add to Library", cut.FindAll(".page-toolbar-btn").Select(b => b.TextContent).First(t => t.Contains("Add")));
    }

    [Fact]
    public void SearchProwlarr_AsksThePageToOpenItsDialog()
    {
        var requested = 0;
        var cut = RenderSection(p => p.Add(x => x.OnSearchRequested, () => requested++));

        cut.FindAll(".page-toolbar-btn").First(b => b.TextContent.Contains("Search Prowlarr")).Click();

        Assert.Equal(1, requested);
    }

    [Fact]
    public void BrowsingTheSeries_GoesToTheMoviesPageWithTheCatalogFilter()
    {
        var series = new R18DevCatalogRef(20, "Series A");
        var row = new R18DevReleaseRow(new R18DevFilmographyEntry("MIDE-002", "Another", null, new DateOnly(2020, 1, 1), null), "wanted", "Wanted", "/movies/MIDE-002", "MIDE002");
        var related = new R18DevRelatedReleases(series, null, [row], 5);
        var cut = RenderSection(p => p.Add(x => x.Related, related));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/MIDE-001");

        cut.Find("button.related-releases-see-all").Click();

        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(series, filterState.PeekPendingFilter()!.CatalogSeries);
    }
}
