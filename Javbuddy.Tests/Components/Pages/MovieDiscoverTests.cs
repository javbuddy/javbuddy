using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Pages.MovieDiscoverSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MovieDiscoverTests : BunitContext
{
    public MovieDiscoverTests()
    {
        // Each studio section's VirtualizedGrid imports its JS module on first render; nothing here
        // exercises the module itself.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static DiscoveredMovieCandidate Candidate(int id, string code, string title = "Title", bool isUpcoming = false, DateTime? releaseDate = null) =>
        new()
        {
            Id = id,
            Code = code,
            Title = title,
            Studio = "S1 NO.1 STYLE",
            SourceName = "S1",
            IsUpcoming = isUpcoming,
            ReleaseDate = releaseDate,
            Status = DiscoveredMovieStatus.New,
        };

    private static DiscoveredMovieCandidateView View(DiscoveredMovieCandidate candidate, IReadOnlyList<DiscoveredMovieActress>? actresses = null) =>
        new(candidate, actresses ?? []);

    private static DiscoveredStudioSection Section(string sourceName, string studioDisplayName, string? logoUrl, params DiscoveredMovieCandidateView[] candidates) =>
        new(sourceName, studioDisplayName, logoUrl is null ? null : new StudioLogo(logoUrl, 205, 161), candidates);

    private static DiscoveredStudioSection S1Section(params DiscoveredMovieCandidateView[] candidates) =>
        Section("S1", "S1 NO.1 STYLE", null, candidates);

    [Fact]
    public void MovieDiscover_RendersCandidatesOnInitialLoad()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var candidate = Candidate(1, "SIVR-501", title: "Sample Title", isUpcoming: true);
        candidate.CoverImageUrl = "https://example.test/SIVR-501.jpg";
        candidate.GalleryImageUrls = ["https://example.test/gallery-1.jpg", "https://example.test/gallery-2.jpg"];
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(candidate))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        Assert.Contains("Scan Now", cut.Markup);
        var card = cut.Find("article.discover-candidate-card");
        Assert.Contains("SIVR-501", card.TextContent);
        Assert.Contains("Sample Title", card.TextContent);
        Assert.Contains("Upcoming", card.TextContent);
        Assert.NotNull(card.QuerySelector(".discover-candidate-cover img"));
    }

    [Fact]
    public void MovieDiscover_DoesNotRenderWeeklyRunFooterNote()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501")))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        Assert.DoesNotContain("runs automatically once a week", cut.Markup);
        Assert.DoesNotContain("Studio sites checked", cut.Markup);
        Assert.Empty(cut.FindAll(".discover-footer-note"));
    }

    [Fact]
    public void MovieDiscover_ActressMatchedToTrackedActor_RendersAsLink()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var candidate = Candidate(1, "SIVR-501");
        var actresses = new List<DiscoveredMovieActress>
        {
            new("白石透羽", 42, "Shiraishi Tou"),
            new("Someone Untracked", null, null),
        };
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(candidate, actresses))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var actressLine = cut.Find(".discover-candidate-actresses");
        Assert.Contains("Shiraishi Tou", actressLine.TextContent);
        Assert.Contains("Someone Untracked", actressLine.TextContent);

        var link = cut.Find("a.discover-actress-tracked");
        Assert.Equal("Shiraishi Tou", link.TextContent);
        Assert.Equal("/actors/Shiraishi%20Tou", link.GetAttribute("href"));
        Assert.DoesNotContain(actressLine.QuerySelectorAll("a.discover-actress-tracked"), a => a.TextContent == "Someone Untracked");
    }

    [Fact]
    public void MovieDiscover_MultipleStudioSections_RendersHeaderPerSectionInGivenOrder()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Section("S1", "S1 NO.1 STYLE", "/studio-logos/s1.png", View(Candidate(1, "SIVR-501")), View(Candidate(2, "SIVR-502"))),
            Section("Moodyz", "MOODYZ", null, View(Candidate(3, "MIDV-001"))),
        ]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var headers = cut.FindAll(".discover-studio-header");
        Assert.Equal(2, headers.Count);

        Assert.Contains("S1 NO.1 STYLE", headers[0].TextContent);
        Assert.Contains("2 new", headers[0].TextContent);
        Assert.Equal("/studio-logos/s1.png", headers[0].QuerySelector("img.discover-studio-logo")?.GetAttribute("src"));

        Assert.Contains("MOODYZ", headers[1].TextContent);
        Assert.Contains("1 new", headers[1].TextContent);
        Assert.Null(headers[1].QuerySelector("img.discover-studio-logo"));

        var sections = cut.FindAll(".discover-studio-section");
        Assert.Equal(2, sections[0].QuerySelectorAll("article.discover-candidate-card").Count());
        Assert.Single(sections[1].QuerySelectorAll("article.discover-candidate-card"));
    }

    [Fact]
    public void MovieDiscover_TopBar_ShowsOneLogoPerStudioWithALogoAndTheScanButtonOnTheRight()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Section("S1", "S1 NO.1 STYLE", "/studio-logos/s1.png", View(Candidate(1, "SIVR-501"))),
            Section("Moodyz", "MOODYZ", null, View(Candidate(2, "MIDV-001"))), // no logo configured
        ]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var topBar = cut.Find(".discover-top-bar");
        var logos = topBar.QuerySelectorAll("img.discover-top-bar-logo");
        Assert.Single(logos);
        Assert.Equal("/studio-logos/s1.png", logos[0].GetAttribute("src"));

        var scanButton = topBar.QuerySelector(".discover-top-bar-actions button.btn-primary");
        Assert.NotNull(scanButton);
        Assert.Equal("Scan Now", scanButton!.TextContent.Trim());
    }

    [Fact]
    public void MovieDiscover_TopBarLogo_LinksToItsOwnSectionsAnchorId()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Section("S1", "S1 NO.1 STYLE", "/studio-logos/s1.png", View(Candidate(1, "SIVR-501"))),
            Section("Moodyz", "MOODYZ", "/studio-logos/moodyz.png", View(Candidate(2, "MIDV-001"))),
        ]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var logoLink = cut.Find("a.discover-top-bar-logo-link");
        var href = logoLink.GetAttribute("href");
        Assert.NotNull(href);
        // Must include the page's own path, not just "#fragment" — App.razor's <base href="/">
        // makes the browser resolve a hash-only href against the base URL rather than the current
        // page, so a bare "#fragment" would navigate away to "/" instead of scrolling.
        Assert.StartsWith("/movies/discover#", href);

        var targetSection = cut.Find($"#{href!.Split('#')[1]}");
        Assert.Contains("discover-studio-section", targetSection.ClassList);
        Assert.Contains("S1 NO.1 STYLE", targetSection.TextContent);
    }

    [Fact]
    public async Task MovieDiscover_ClickingCover_OpensCandidateGallery()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var candidate = Candidate(1, "SIVR-501");
        candidate.CoverImageUrl = "https://example.test/SIVR-501.jpg";
        candidate.GalleryImageUrls = ["https://example.test/gallery-1.jpg", "https://example.test/gallery-2.jpg"];
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(candidate))]);
        Services.AddSingleton(discoveryService);
        JSInterop.SetupModule("./Components/Shared/ImageZoomControls.razor.js").Mode = JSRuntimeMode.Loose;

        var cut = Render<MovieDiscover>();

        await cut.InvokeAsync(() => cut.Find(".discover-candidate-cover").Click());

        Assert.Equal("1 / 2", cut.Find(".gallery-index").TextContent);
        Assert.Equal("https://example.test/gallery-1.jpg", cut.Find(".gallery-image").GetAttribute("src"));
    }

    [Fact]
    public void MovieDiscover_NoCandidates_ShowsEmptyMessage()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        Assert.Contains("Nothing new right now", cut.Markup);
    }

    [Fact]
    public async Task MovieDiscover_ClickingScanNow_RunsScanAndReloadsCandidates()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([], [S1Section(View(Candidate(1, "SIVR-501")))]);
        discoveryService.RunScanAsync(Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new MovieDiscoveryScanResult(TotalFound: 5, NewCount: 1, AlreadyTrackedCount: 4));
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        Assert.Contains("Nothing new right now", cut.Markup);

        await cut.InvokeAsync(() => cut.Find("button.btn-primary").Click());

        await discoveryService.Received(1).RunScanAsync(Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());
        Assert.Contains("Found 5 releases", cut.Markup);
        Assert.Contains("1 new candidate", cut.Markup);
        Assert.Contains("SIVR-501", cut.Find("article.discover-candidate-card").TextContent);
    }

    [Fact]
    public async Task MovieDiscover_ClickingAdd_CallsAddCandidateAndMovesRowToRecentlyAdded()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501", title: "Sample Title")))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var addButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Add");

        await cut.InvokeAsync(() => addButton.Click());

        await discoveryService.Received(1).AddCandidateAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("article.discover-candidate-card"));
        Assert.Contains("Added this session", cut.Markup);
        Assert.Contains("SIVR-501", cut.Find("ul.discover-result-list").TextContent);
    }

    [Fact]
    public async Task MovieDiscover_ClickingDismiss_CallsDismissCandidateAndRemovesRow()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501")))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var dismissButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Dismiss");

        await cut.InvokeAsync(() => dismissButton.Click());

        await discoveryService.Received(1).DismissCandidateAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("article.discover-candidate-card"));
        Assert.Empty(cut.FindAll(".discover-studio-section")); // last candidate in the section removed → section itself disappears
    }

    [Fact]
    public async Task MovieDiscover_ClickingAdd_ShowsLoadingSpinnerAndAddingLabelWhileInFlight()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var tcs = new TaskCompletionSource<MovieAddResult>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501", title: "Sample Title")))]);
        discoveryService.AddCandidateAsync(1, Arg.Any<CancellationToken>()).Returns(tcs.Task);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var addButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Add");

        _ = cut.InvokeAsync(() => addButton.Click());

        cut.WaitForAssertion(() =>
        {
            var actions = cut.Find(".discover-candidate-actions");
            Assert.Contains("is-adding", actions.ClassList);

            var singleButton = actions.QuerySelector("button");
            Assert.NotNull(singleButton);
            Assert.True(singleButton.HasAttribute("disabled"));
            Assert.Contains("Adding…", singleButton.TextContent);
            Assert.NotNull(singleButton.QuerySelector(".spinner-border"));
            Assert.Null(actions.QuerySelectorAll("button").FirstOrDefault(b => b.TextContent.Trim() == "Dismiss"));
        });

        await cut.InvokeAsync(() => tcs.SetResult(new MovieAddResult(new Movie { Code = "SIVR-501", Title = "Sample Title" }, false, true, null)));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("article.discover-candidate-card"));
            Assert.Contains("Added this session", cut.Markup);
        });
    }

    [Fact]
    public async Task MovieDiscover_AddCandidateThrows_ResetsButtonStateAndShowsError()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501", title: "Sample Title")))]);
        discoveryService.AddCandidateAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MovieAddResult>(new InvalidOperationException("Metadata service unreachable")));
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var addButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Add");

        await cut.InvokeAsync(() => addButton.Click());

        Assert.Contains("Failed to add SIVR-501: Metadata service unreachable", cut.Markup);

        var actions = cut.Find(".discover-candidate-actions");
        Assert.DoesNotContain("is-adding", actions.ClassList);

        var buttons = actions.QuerySelectorAll("button").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal("Add", buttons[0].TextContent.Trim());
        Assert.False(buttons[0].HasAttribute("disabled"));
        Assert.Equal("Dismiss", buttons[1].TextContent.Trim());
        Assert.False(buttons[1].HasAttribute("disabled"));
    }

    [Fact]
    public async Task MovieDiscover_ClickingDismiss_ShowsDismissingLabelWhileInFlight()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var tcs = new TaskCompletionSource();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(View(Candidate(1, "SIVR-501")))]);
        discoveryService.DismissCandidateAsync(1, Arg.Any<CancellationToken>()).Returns(tcs.Task);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var dismissButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Dismiss");

        _ = cut.InvokeAsync(() => dismissButton.Click());

        cut.WaitForAssertion(() =>
        {
            var actions = cut.Find(".discover-candidate-actions");
            Assert.Contains("is-dismissing", actions.ClassList);

            var singleButton = actions.QuerySelector("button");
            Assert.NotNull(singleButton);
            Assert.True(singleButton.HasAttribute("disabled"));
            Assert.Contains("Dismissing…", singleButton.TextContent);
            Assert.NotNull(singleButton.QuerySelector(".spinner-border"));
        });

        await cut.InvokeAsync(() => tcs.SetResult());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("article.discover-candidate-card")));
    }

    [Fact]
    public async Task MovieDiscover_CandidateAddingInFlight_DisablesOtherCandidates()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var tcs = new TaskCompletionSource<MovieAddResult>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>())
            .Returns([S1Section(View(Candidate(1, "SIVR-501")), View(Candidate(2, "SIVR-502")))]);
        discoveryService.AddCandidateAsync(1, Arg.Any<CancellationToken>()).Returns(tcs.Task);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var firstAddButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Add");

        _ = cut.InvokeAsync(() => firstAddButton.Click());

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll("article.discover-candidate-card");
            Assert.Equal(2, cards.Count);

            var firstActions = cards[0].QuerySelector(".discover-candidate-actions");
            Assert.Contains("is-adding", firstActions!.ClassList);

            var secondButtons = cards[1].QuerySelectorAll(".discover-candidate-actions button");
            Assert.Equal(2, secondButtons.Count);
            Assert.All(secondButtons, b => Assert.True(b.HasAttribute("disabled")));
        });

        await cut.InvokeAsync(() => tcs.SetResult(new MovieAddResult(new Movie { Code = "SIVR-501", Title = "Sample Title" }, false, true, null)));
    }

    private static DiscoveredMovieCandidateView[] Views(int count, int firstId = 1) =>
        Enumerable.Range(firstId, count)
            .Select(id => View(Candidate(id, $"SIVR-{id:000}")))
            .ToArray();

    [Fact]
    public void MovieDiscover_LongSection_RendersOnlyItsInitialWindow()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(Views(40))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var cards = cut.FindAll("article.discover-candidate-card");
        Assert.Equal(DiscoverSectionWindow.InitialSize, cards.Count);
        Assert.Contains("SIVR-001", cards[0].TextContent);
        // The header still counts the whole section, and the grid reserves the height of all of it.
        Assert.Contains("40 new", cut.Find(".discover-studio-count").TextContent);
        Assert.Equal("40", cut.Find(".virtualized-grid-spacer").GetAttribute("data-total-count"));
    }

    [Fact]
    public async Task MovieDiscover_SectionGridAsksForARange_RendersThatSectionsSliceOnly()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            S1Section(Views(40)),
            Section("Moodyz", "MOODYZ", null, Views(30, firstId: 101)),
        ]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        var grids = cut.FindComponents<VirtualizedGrid>();

        await cut.InvokeAsync(() => grids[1].Instance.SetVisibleRange(18, 8, 6));

        var sections = cut.FindAll(".discover-studio-section");
        var moodyzCards = sections[1].QuerySelectorAll("article.discover-candidate-card").ToList();
        Assert.Equal(8, moodyzCards.Count);
        Assert.Contains("SIVR-119", moodyzCards[0].TextContent); // index 18 of a section starting at id 101
        Assert.Equal("18", sections[1].QuerySelector(".virtualized-grid-spacer")!.GetAttribute("data-window-start"));
        Assert.Equal(DiscoverSectionWindow.InitialSize, sections[0].QuerySelectorAll("article.discover-candidate-card").Count());
    }

    [Fact]
    public void MovieDiscover_OnlyTheFirstSectionsFirstCover_IsEagerAndHighPriority()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        var views = Views(3);
        foreach (var view in views) view.Candidate.CoverImageUrl = $"https://example.test/{view.Candidate.Code}.jpg";
        var moodyz = Views(1, firstId: 101);
        moodyz[0].Candidate.CoverImageUrl = "https://example.test/moodyz.jpg";
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
            [S1Section(views), Section("Moodyz", "MOODYZ", null, moodyz)]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        var covers = cut.FindAll(".discover-candidate-cover img");
        Assert.Equal(4, covers.Count);
        Assert.Equal("eager", covers[0].GetAttribute("loading"));
        Assert.Equal("high", covers[0].GetAttribute("fetchpriority"));
        Assert.All(covers.Skip(1), img =>
        {
            Assert.Equal("lazy", img.GetAttribute("loading"));
            Assert.False(img.HasAttribute("fetchpriority"));
        });
    }

    [Fact]
    public void MovieDiscover_Logos_CarryTheirIntrinsicSize()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns(
            [Section("S1", "S1 NO.1 STYLE", "/studio-logos/s1.png", View(Candidate(1, "SIVR-501")))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();

        foreach (var logo in cut.FindAll("img.discover-top-bar-logo, img.discover-studio-logo"))
        {
            Assert.Equal("205", logo.GetAttribute("width"));
            Assert.Equal("161", logo.GetAttribute("height"));
        }
        Assert.Equal(2, cut.FindAll("img.discover-top-bar-logo, img.discover-studio-logo").Count);
    }

    [Fact]
    public async Task MovieDiscover_DismissingFromTheEndOfAWindow_KeepsTheWindowInsideTheSection()
    {
        var discoveryService = Substitute.For<IMovieDiscoveryService>();
        discoveryService.GetCandidateSectionsAsync(Arg.Any<CancellationToken>()).Returns([S1Section(Views(13))]);
        Services.AddSingleton(discoveryService);

        var cut = Render<MovieDiscover>();
        // The last row on its own, as the grid asks for it once the section is scrolled far past.
        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(12, 6, 6));
        Assert.Contains("SIVR-013", cut.Find("article.discover-candidate-card").TextContent);

        var dismiss = cut.FindAll("button").First(b => b.TextContent.Trim() == "Dismiss");
        await cut.InvokeAsync(() => dismiss.Click());

        // 12 left, so the window falls back to the (new) last row rather than pointing past the end.
        var cards = cut.FindAll("article.discover-candidate-card");
        Assert.Equal(6, cards.Count);
        Assert.Contains("SIVR-007", cards[0].TextContent);
        Assert.Equal("6", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
    }
}
