using Bunit;
using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Torrents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MovieReleasesBrowserTests : BunitContext
{
    private const R18DevCatalogAvailability Ready = R18DevCatalogAvailability.Ready;

    private readonly IR18DevReleaseBrowseService browseService = Substitute.For<IR18DevReleaseBrowseService>();
    private readonly IMovieAddService movieAddService = Substitute.For<IMovieAddService>();

    public MovieReleasesBrowserTests()
    {
        Services.AddSingleton(browseService);
        Services.AddSingleton(movieAddService);
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        browseService.CountAsync(default!, default).ReturnsForAnyArgs(new R18DevCatalogCounts(10, 7, 2, 1));
    }

    private static R18DevReleaseRow Row(string dvdId, string statusClass = "missing") =>
        new(new R18DevFilmographyEntry(dvdId, "Title " + dvdId, null, new DateOnly(2020, 1, 2), null),
            statusClass, statusClass == "missing" ? "2020-01-02" : "Got", $"/movies/{dvdId}", dvdId);

    private void Returns(R18DevReleaseBrowseResult result) =>
        browseService.BrowseAsync(default!, default, default, default).ReturnsForAnyArgs(result);

    private IRenderedComponent<MovieReleasesBrowser> RenderBrowser(R18DevCatalogFilter? filter = null, Action<R18DevCatalogFilter>? onChanged = null) =>
        Render<MovieReleasesBrowser>(p => p
            .Add(x => x.Filter, filter ?? new R18DevCatalogFilter { CodePrefix = "MIDE" })
            .Add(x => x.FilterChanged, f => onChanged?.Invoke(f)));

    [Fact]
    public void WhileQueryRuns_ShowsPosterSkeleton_ThenResults()
    {
        var pending = new TaskCompletionSource<R18DevReleaseBrowseResult>();
        browseService.BrowseAsync(default!, default, default, default).ReturnsForAnyArgs(pending.Task);

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".skeleton-wrapper")));
        Assert.Empty(cut.FindAll(".poster-grid"));

        pending.SetResult(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".skeleton-wrapper")));
        Assert.Contains("MIDE-001", cut.Markup);
    }

    [Fact]
    public void SourceDisabled_ShowsHintLinkingToMetadataSettings()
    {
        Returns(new R18DevReleaseBrowseResult(false, R18DevCatalogAvailability.NoDump, []));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.Contains("disabled", cut.Markup));
        Assert.Equal("/settings/metadata", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void DumpNotImported_ShowsImportHint()
    {
        Returns(new R18DevReleaseBrowseResult(true, R18DevCatalogAvailability.NoDump, []));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.Contains("hasn't been imported yet", cut.Markup));
    }

    [Fact]
    public void DumpFromAnOlderVersion_AsksForAReimport()
    {
        Returns(new R18DevReleaseBrowseResult(true, R18DevCatalogAvailability.NeedsReimport, []));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.Contains("to re-import it", cut.Markup));
        Assert.Empty(cut.FindAll(".btn-group"));
    }

    [Fact]
    public void NoReleases_ShowsEmptyMessage()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, []));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.Contains("No releases on r18.dev match these filters", cut.Markup));
    }

    [Fact]
    public void EmptyFilter_BrowsesTheWholeCatalog()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));

        var cut = RenderBrowser(new R18DevCatalogFilter());

        cut.WaitForAssertion(() => cut.Find("a.poster-card"));
        browseService.Received(1).BrowseAsync(Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == null), 0, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Releases_RenderAsCardsWithTheMatchingTotal()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-003"), Row("MIDE-002", "wanted"), Row("MIDE-001", "got")]));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => Assert.Contains("Showing 3 of 10 releases", cut.Find(".releases-summary").TextContent));
        Assert.Equal(3, cut.FindAll("a.poster-card").Count);
    }

    [Fact]
    public void StatusButtons_ShowPerStatusCountsAndRaiseTheChosenStatus()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        R18DevCatalogFilter? changed = null;

        var cut = RenderBrowser(onChanged: f => changed = f);
        cut.WaitForAssertion(() => Assert.Contains("Untracked (7)", cut.Markup));
        Assert.Contains("All (10)", cut.Markup);
        Assert.Contains("Wanted (2)", cut.Markup);
        Assert.Contains("Got (1)", cut.Markup);

        cut.FindAll(".btn-group button").Single(b => b.TextContent.Trim().StartsWith("Got")).Click();

        Assert.Equal(R18DevCatalogStatus.Got, changed?.Status);
        Assert.Equal("MIDE", changed?.CodePrefix);
    }

    [Fact]
    public void SwitchingOnlyTheStatus_ReloadsTheGridButKeepsTheCounts()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        var cut = RenderBrowser();
        cut.WaitForAssertion(() => Assert.Contains("All (10)", cut.Markup));

        cut.Render(p => p.Add(x => x.Filter, new R18DevCatalogFilter { CodePrefix = "MIDE", Status = R18DevCatalogStatus.Untracked }));

        cut.WaitForAssertion(() => browseService.Received(1).BrowseAsync(Arg.Is<R18DevCatalogFilter>(f => f.Status == R18DevCatalogStatus.Untracked), 0, null, Arg.Any<CancellationToken>()));
        browseService.Received(1).CountAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SeriesAndLabelChips_RemoveTheirFilter()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        R18DevCatalogFilter? changed = null;
        var filter = new R18DevCatalogFilter { Series = new R18DevCatalogRef(20, "Series A"), Label = new R18DevCatalogRef(10, "Label B") };

        var cut = RenderBrowser(filter, f => changed = f);
        cut.WaitForAssertion(() => Assert.Contains("Series: Series A", cut.Markup));
        Assert.Contains("Label: Label B", cut.Markup);

        cut.Find("button[aria-label='Remove series filter']").Click();

        Assert.Null(changed?.Series);
        Assert.Equal(new R18DevCatalogRef(10, "Label B"), changed?.Label);
    }

    [Fact]
    public void ShowMore_FetchesTheNextPageSkippingReleasesAlreadyShown()
    {
        browseService.BrowseAsync(Arg.Any<R18DevCatalogFilter>(), 0, Arg.Any<IReadOnlySet<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-003"), Row("MIDE-002")], NextOffset: 200, HasMore: true));
        browseService.BrowseAsync(Arg.Any<R18DevCatalogFilter>(), 200, Arg.Any<IReadOnlySet<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")], NextOffset: 300, HasMore: false));
        var cut = RenderBrowser();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("a.poster-card").Count));

        cut.Find("button.releases-show-more").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("a.poster-card").Count));
        Assert.Empty(cut.FindAll("button.releases-show-more"));
        browseService.Received(1).BrowseAsync(Arg.Any<R18DevCatalogFilter>(), 200,
            Arg.Is<IReadOnlySet<string>?>(s => s != null && s.SetEquals(new[] { "MIDE-003", "MIDE-002" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LastPage_HasNoShowMore()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")], NextOffset: 1, HasMore: false));

        var cut = RenderBrowser();

        cut.WaitForAssertion(() => cut.Find("a.poster-card"));
        Assert.Empty(cut.FindAll("button.releases-show-more"));
    }

    [Fact]
    public void AddButton_AddsTheMovieAndTheCardBecomesInLibrary()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        movieAddService.AddAsync("MIDE-001", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Code = "MIDE-001", Status = MovieStatus.Missing }, false, true, null));
        var cut = RenderBrowser();
        cut.WaitForAssertion(() => cut.Find("button.missing-poster-add"));

        cut.Find("button.missing-poster-add").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button.missing-poster-add")));
        Assert.NotNull(cut.Find(".poster-image.status-wanted"));
        movieAddService.Received(1).AddAsync("MIDE-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddFailure_ShowsErrorAndKeepsTheCardMissing()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        movieAddService.AddAsync("MIDE-001", Arg.Any<CancellationToken>()).Returns<Task<MovieAddResult>>(_ => throw new InvalidOperationException("boom"));
        var cut = RenderBrowser();
        cut.WaitForAssertion(() => cut.Find("button.missing-poster-add"));

        cut.Find("button.missing-poster-add").Click();

        cut.WaitForAssertion(() => Assert.Contains("Couldn't add MIDE-001", cut.Find(".alert-danger").TextContent));
        Assert.NotNull(cut.Find(".poster-image.status-missing"));
    }

    [Fact]
    public void ChangingTheFilter_ReloadsWithTheNewFilter()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, []));
        var cut = RenderBrowser();
        cut.WaitForAssertion(() => Assert.Contains("No releases on r18.dev", cut.Markup));

        cut.Render(p => p.Add(x => x.Filter, new R18DevCatalogFilter { CodePrefix = "SSIS", Studios = ["S1 NO.1 STYLE"], Genres = ["Creampie"] }));

        cut.WaitForAssertion(() => browseService.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == "SSIS" && f.Studios.SequenceEqual(new[] { "S1 NO.1 STYLE" }) && f.Genres.SequenceEqual(new[] { "Creampie" })),
            0, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ReRenderingWithAnEqualFilter_DoesNotRequery()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001")]));
        var cut = RenderBrowser(new R18DevCatalogFilter { CodePrefix = "MIDE", Genres = ["A"] });
        cut.WaitForAssertion(() => cut.Find("a.poster-card"));

        cut.Render(p => p.Add(x => x.Filter, new R18DevCatalogFilter { CodePrefix = "MIDE", Genres = ["A"] }));

        browseService.Received(1).BrowseAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Any<int>(), Arg.Any<IReadOnlySet<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PreviouslyDeletedMatches_AreCountedInTheSummary_AsHiddenOrShown()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-002")]));
        browseService.CountAsync(default!, default).ReturnsForAnyArgs(new R18DevCatalogCounts(9, 9, 0, 0, 2));

        var cut = RenderBrowser();
        cut.WaitForAssertion(() => Assert.Contains("2 previously deleted, hidden", cut.Find(".releases-summary").TextContent));

        cut.Render(p => p.Add(x => x.Filter, new R18DevCatalogFilter { CodePrefix = "MIDE", HidePreviouslyDeleted = false }));

        cut.WaitForAssertion(() => Assert.Contains("2 previously deleted", cut.Find(".releases-summary").TextContent));
        Assert.DoesNotContain("hidden", cut.Find(".releases-summary").TextContent);
    }

    [Fact]
    public void PreviouslyDeletedRelease_CanBeExplicitlyReAdded()
    {
        Returns(new R18DevReleaseBrowseResult(true, Ready, [Row("MIDE-001", "deleted")]));
        movieAddService.AddAsync("MIDE-001", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Code = "MIDE-001" }, false, false, null));
        var cut = RenderBrowser(new R18DevCatalogFilter { CodePrefix = "MIDE", HidePreviouslyDeleted = false });
        cut.WaitForAssertion(() => cut.Find(".status-deleted"));

        cut.Find(".missing-poster-add").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".status-wanted")));
    }
}
