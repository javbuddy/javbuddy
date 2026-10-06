using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Movies;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MovieScenesTests : BunitContext
{
    private readonly ISceneWallQueryService wall = Substitute.For<ISceneWallQueryService>();
    private readonly ISceneMediaService media = Substitute.For<ISceneMediaService>();
    private readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    private readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();
    private readonly BunitJSModuleInterop module;
    private readonly BunitJSModuleInterop clipCardModule;

    private static SceneWallCard Card(int id, string code, long? thumb = 111, long? preview = 222) =>
        new(id, id * 10, code, $"{code} title", $"Scene {id}", 60, 600, ["Aika"], [new SceneTagItem(1, "Rough", "Play")], id == 1, thumb, preview)
        {
            HasLocalVideo = true,
            JellyfinItemId = $"jf-{id}",
        };

    private static readonly SceneWallOptions Options = new(
        [new SceneWallOption(5, "Play › Rough")], [new SceneWallOption(7, "Aika")], ["S1"], ActorAttributeOptions.None)
    {
        ApexTags = [new SceneWallOption(9, "Squirt")],
    };

    private static readonly SceneWallOptions ApexOptions = new([], [new SceneWallOption(7, "Aika")], ["S1"], ActorAttributeOptions.None)
    {
        ApexTags = [new SceneWallOption(9, "Squirt")],
    };

    public MovieScenesTests()
    {
        Services.AddSingleton(wall);
        Services.AddSingleton(media);
        Services.AddSingleton(highlightMedia);
        Services.AddSingleton(apexMedia);
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        Services.AddSingleton(Substitute.For<IMovieHighlightService>());
        Services.AddSingleton(Substitute.For<IMovieApexService>());
        Services.AddSingleton(StreamServiceStubs.AllPlayable());
        Services.AddSingleton(Substitute.For<ITrickplayService>());
        Services.AddSingleton(new TrickplayGenerationTracker());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Trickplay.IHighlightTrickplayService>());
        Services.AddSingleton(new SceneMediaQueue(Substitute.For<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance));
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Options);
        wall.GetHighlightOptionsAsync(Arg.Any<CancellationToken>()).Returns(Options);
        wall.GetApexOptionsAsync(Arg.Any<CancellationToken>()).Returns(ApexOptions);
        apexMedia.ServesPreview.Returns(true);
        media.ServesVariant(Arg.Any<string>()).Returns(true);
        highlightMedia.ServesVariant(Arg.Any<string>()).Returns(true);
        module = JSInterop.SetupModule("./Components/Pages/MovieScenes.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        clipCardModule = JSInterop.SetupModule("./Components/Shared/ClipCard.razor.js");
        clipCardModule.Mode = JSRuntimeMode.Loose;
        // The clip player's own modules (clip controls, seek keys) once a card with a Jellyfin item plays.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void RememberedState(string json) => module.Setup<string?>("getCookie", "javbuddy-scenes-view").SetResult(json);

    private static void ClickButton(IRenderedComponent<MovieScenes> cut, string textStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(textStart, StringComparison.Ordinal)).Click();

    [Fact]
    public void Cards_LinkToTheScene_AndShowTheirDetails()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123"), Card(2, "XYZ-9")], 2));

        var cut = Render<MovieScenes>();

        var cards = cut.FindAll(".scene-wall-card");
        Assert.Equal(2, cards.Count);
        Assert.Equal("/movies/ABC-123?scene=1", cards[0].GetAttribute("href"));
        Assert.Equal("9:00", cards[0].QuerySelector(".scene-wall-duration")!.TextContent);
        Assert.Contains("Play › Rough", cards[0].TextContent);
        Assert.Contains("Aika", cards[0].TextContent);
        Assert.Single(cut.FindAll(".scene-wall-fav"));
        Assert.Equal("2 scenes", cut.Find(".scene-wall-count").TextContent);
        Assert.Empty(cut.FindAll(".scene-wall-more"));
    }

    [Fact]
    public void UnknownEnd_OmitsTheDuration()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123") with { EffectiveEndSeconds = null }], 1));

        var cut = Render<MovieScenes>();

        Assert.Empty(cut.FindAll(".scene-wall-duration"));
    }

    [Fact]
    public void Hover_PlaysThePreviewOverTheScreenshot()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123")], 1));
        var cut = Render<MovieScenes>();

        Assert.Empty(cut.FindAll(".scene-wall-media video"));
        cut.Find(".scene-wall-card").MouseEnter();
        Assert.Equal("/scene-image/1/thumb?v=111", cut.Find(".scene-wall-media img").GetAttribute("src"));
        Assert.Equal("/scene-image/1/preview?v=222", cut.Find(".scene-wall-media video").GetAttribute("src"));
    }

    [Fact]
    public void MissingMedia_IsQueued_AndShowsAPlaceholder()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(3, "NOM-1", thumb: null, preview: null)], 1));

        var cut = Render<MovieScenes>();

        // A loading shimmer while it's generated, not an <img> pointing at a 404.
        Assert.Empty(cut.FindAll(".scene-wall-media img"));
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
        Assert.Empty(cut.FindAll(".scene-preview-none"));
        media.Received(1).EnsureQueuedAsync(30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MissingMedia_ThatCantBeGenerated_SaysNoPreview()
    {
        // No local video to take a screenshot from, or a cache mode that doesn't keep screenshots.
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(3, "NOM-1", thumb: null, preview: null) with { HasLocalVideo = false }, Card(4, "NOM-2", thumb: null, preview: null)], 2));
        media.ServesVariant(SceneMediaService.VariantThumb).Returns(true);

        var cut = Render<MovieScenes>();

        Assert.Equal("No preview", cut.Find(".scene-preview-none").TextContent);
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));

        media.ServesVariant(SceneMediaService.VariantThumb).Returns(false);
        cut = Render<MovieScenes>();

        Assert.All(cut.FindAll(".scene-preview-none"), p => Assert.Equal("No preview", p.TextContent));
        Assert.Empty(cut.FindAll(".scene-preview-shimmer"));
        Assert.Equal(2, cut.FindAll(".scene-preview-none").Count);
    }

    [Fact]
    public void ChangingAFilter_ReloadsWithIt()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();
        Assert.Contains("No scenes yet", cut.Find(".scene-wall-empty").TextContent);

        ClickButton(cut, "Filter");
        ClickButton(cut, "Tag");
        ClickButton(cut, "Play › Rough");
        ClickButton(cut, "Actor");
        ClickButton(cut, "Name");
        ClickButton(cut, "Aika");
        ClickButton(cut, "Favorites only");

        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.TagIds!.Single() == 5 && f.ActorIds!.Single() == 7 && f.FavoritesOnly),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Equal("No scenes match these filters.", cut.Find(".scene-wall-empty").TextContent.Trim());
        Assert.Contains("Filter (3)", cut.Markup);
        var saved = module.Invocations.Last(i => i.Identifier == "setJsonCookie");
        Assert.Equal("javbuddy-scenes-view", saved.Arguments[0]);
        Assert.Contains("\"TagIds\":[5]", (string)saved.Arguments[1]!);

        // The reset × clears every filter.
        cut.Find(".dropdown-clear-btn").Click();
        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.TagIds == null && f.ActorIds == null && !f.FavoritesOnly),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RememberedFilters_AreApplied_DroppingValuesNoLongerOffered()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"TagIds":[5,999],"Studios":["S1"],"FavoritesOnly":true,"Text":"bath","Sort":1}""");

        var cut = Render<MovieScenes>();

        cut.WaitForAssertion(() => wall.Received(1).GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.TagIds!.Single() == 5 && f.Studios!.Single() == "S1" && f.FavoritesOnly && f.Text == "bath"),
            SceneWallSort.DateAdded, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>()));
        Assert.Contains("Filter (3)", cut.Markup);
        Assert.Contains("Sort: Date added", cut.Markup);
        Assert.Equal("bath", cut.Find(".scene-wall-search").GetAttribute("value"));
    }

    [Fact]
    public void ARememberedRatingSortAndFilter_FallBackToTheDefaults()
    {
        // Sort 2 and MinRating are what the wall remembered before ratings were removed.
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"MinRating":4,"Sort":2}""");

        var cut = Render<MovieScenes>();

        cut.WaitForAssertion(() => wall.Received(1).GetPageAsync(
            Arg.Any<SceneWallFilter>(), SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>()));
        Assert.Contains("Sort: Release date", cut.Markup);
        Assert.DoesNotContain("Filter (", cut.Markup);
    }

    [Fact]
    public void Search_ReloadsWithTheText_AfterTyping()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();

        cut.Find(".scene-wall-search").Input("intro");

        cut.WaitForAssertion(() => wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.Text == "intro"),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Sort_UsesTheDropdown()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Sort: Release date");
        ClickButton(cut, "Date added");

        wall.Received().GetPageAsync(Arg.Any<SceneWallFilter>(), SceneWallSort.DateAdded, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Sort: Date added", cut.Markup);
    }

    private static List<SceneWallCard> Cards(int from, int count) =>
        [.. Enumerable.Range(from, count).Select(i => Card(i + 1, $"ABC-{i + 1}"))];

    private void Window(int skip, int take, int total) =>
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(skip), Arg.Is(take), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage(Cards(skip, Math.Min(take, total - skip)), total));

    private static string? SpacerAttribute(IRenderedComponent<MovieScenes> cut, string name) =>
        cut.Find(".virtualized-grid-spacer").GetAttribute(name);

    // One scrollable wall instead of Load more pages.
    [Fact]
    public async Task ScrollingFurther_LoadsTheRowAlignedWindow_WithoutALoadMoreButton()
    {
        Window(0, 48, 200);
        Window(48, 30, 200);
        var cut = Render<MovieScenes>();

        Assert.Equal("200", SpacerAttribute(cut, "data-total-count"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Load more"));

        // 50 isn't on a row boundary at 4 columns: the window starts at the row's first card.
        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(50, 30, 4));

        var cards = cut.FindAll(".scene-wall-card");
        Assert.Equal(30, cards.Count);
        Assert.Equal("/movies/ABC-49?scene=49", cards[0].GetAttribute("href"));
        Assert.Equal("48", SpacerAttribute(cut, "data-window-start"));
        Assert.Equal("30", SpacerAttribute(cut, "data-window-count"));
    }

    [Fact]
    public async Task ARangePastTheEnd_IsClampedToTheLastRow()
    {
        Window(0, 48, 60);
        Window(56, 4, 60);
        var cut = Render<MovieScenes>();

        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(100, 48, 4));

        Assert.Equal("56", SpacerAttribute(cut, "data-window-start"));
        Assert.Equal(4, cut.FindAll(".scene-wall-card").Count);
    }

    [Fact]
    public async Task AWindowLandingAfterAFilterChange_IsDropped()
    {
        Window(0, 48, 200);
        var slow = new TaskCompletionSource<SceneWallPage>();
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(96), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(slow.Task);
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), SceneWallSort.DateAdded, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(7, "NEW-7")], 1));
        var cut = Render<MovieScenes>();

        var scrolling = cut.InvokeAsync(() => cut.Instance.SetVisibleRange(96, 48, 4));
        ClickButton(cut, "Sort:");
        ClickButton(cut, "Date added");
        slow.SetResult(new SceneWallPage(Cards(96, 48), 200));
        await scrolling;

        var card = Assert.Single(cut.FindAll(".scene-wall-card"));
        Assert.Equal("/movies/NEW-7?scene=7", card.GetAttribute("href"));
        Assert.Equal("0", SpacerAttribute(cut, "data-window-start"));
    }

    [Fact]
    public async Task GeneratedMedia_RefreshesOnlyTheLoadedWindow()
    {
        Window(0, 48, 200);
        Window(48, 30, 200);
        var cut = Render<MovieScenes>();
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(48, 30, 4));
        wall.ClearReceivedCalls();

        // ABC-49's movie (id 490) got its media; raised the way SceneMediaQueue's worker does.
        var queue = Services.GetRequiredService<SceneMediaQueue>();
        var generated = (Action<int>?)typeof(SceneMediaQueue).GetField(nameof(SceneMediaQueue.Generated),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue);
        generated!(490);

        cut.WaitForAssertion(() => wall.Received(1).GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(48), Arg.Is(30), Arg.Any<CancellationToken>()));
        await wall.DidNotReceive().GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CardsWithoutActorsOrTags_StillReserveTheirLines()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(2, "XYZ-9") with { Actors = [], Tags = [] }], 1));

        var cut = Render<MovieScenes>();

        Assert.Single(cut.FindAll(".scene-wall-actors"));
        Assert.Single(cut.FindAll(".scene-wall-tags"));
    }

    [Fact]
    public void ShowHiddenScenes_IncludesThemAndTheirFilterOptions_AndIsRemembered()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        wall.GetOptionsAsync(true, Arg.Any<CancellationToken>()).Returns(new SceneWallOptions(
            [new SceneWallOption(5, "Play › Rough"), new SceneWallOption(6, "Intro tag")], [new SceneWallOption(7, "Aika")], ["S1"], ActorAttributeOptions.None));
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");
        ClickButton(cut, "Show hidden scenes");

        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.IncludeHidden), SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (1)", cut.Markup);
        Assert.Contains("\"ShowHidden\":true", (string)module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!);

        // A tag offered only by hidden scenes stops applying when they're hidden again, and applies
        // again when they're shown.
        ClickButton(cut, "Tag");
        ClickButton(cut, "Intro tag");
        ClickButton(cut, "Show hidden scenes");
        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => !f.IncludeHidden && f.TagIds == null), SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.DoesNotContain("Intro tag", cut.Markup);
        Assert.DoesNotContain("Filter (", cut.Markup);
        Assert.Contains("\"TagIds\":[6]", (string)module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!);

        ClickButton(cut, "Show hidden scenes");
        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.IncludeHidden && f.TagIds!.Single() == 6), SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (2)", cut.Markup);
    }

    [Fact]
    public void RememberedShowHidden_LoadsTheOptionsWithHiddenScenes()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        wall.GetOptionsAsync(true, Arg.Any<CancellationToken>()).Returns(new SceneWallOptions([new SceneWallOption(6, "Intro tag")], [], [], ActorAttributeOptions.None));
        RememberedState("""{"TagIds":[6],"ShowHidden":true}""");

        var cut = Render<MovieScenes>();

        cut.WaitForAssertion(() => wall.Received(1).GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.IncludeHidden && f.TagIds!.Single() == 6),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>()));
        Assert.Contains("Filter (2)", cut.Markup);
    }

    // --- Highlights view ---

    private static HighlightWallCard HCard(int id, string code, long? thumb = 333) =>
        new(id, id * 10, code, $"{code} title", $"Highlight {id}", 60, 100, ["Aika"], [], id == 1, thumb, 444)
        {
            HasLocalVideo = true,
            JellyfinItemId = $"jf-{id}",
        };

    private void OpenHighlightsView() =>
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/scenes?view=highlights");

    [Fact]
    public void HighlightsView_ShowsHighlightCards_LinkingToTheClip()
    {
        wall.GetHighlightPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new HighlightWallPage([HCard(1, "ABC-123")], 1));
        OpenHighlightsView();

        var cut = Render<MovieScenes>();

        var card = cut.Find(".scene-wall-card");
        Assert.Contains("scene-wall-card-highlight", card.ClassList);
        Assert.Equal("/movies/ABC-123?highlight=1", card.GetAttribute("href"));
        Assert.Equal("/highlight-image/1/thumb?v=333", card.QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("0:40", card.QuerySelector(".scene-wall-duration")!.TextContent);
        Assert.Contains("Aika", card.TextContent);
        Assert.Single(cut.FindAll(".scene-wall-fav"));
        Assert.Equal("1 highlight", cut.Find(".scene-wall-count").TextContent);
        Assert.Equal("true", cut.Find(".scene-wall-view-btn[data-view=highlights]").GetAttribute("aria-pressed"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Show hidden scenes"));
        wall.DidNotReceive().GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HighlightsView_MissingMedia_IsQueued_AndShowsAPlaceholder()
    {
        wall.GetHighlightPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new HighlightWallPage([HCard(2, "NOM-1", thumb: null)], 1));
        OpenHighlightsView();

        var cut = Render<MovieScenes>();

        Assert.Empty(cut.FindAll(".scene-wall-media img"));
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
        highlightMedia.Received(1).EnsureQueuedAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HighlightsView_Hover_PlaysThePreviewOverTheScreenshot()
    {
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([HCard(1, "ABC-123")], 1));
        OpenHighlightsView();
        var cut = Render<MovieScenes>();

        cut.Find(".scene-wall-card").MouseEnter();

        Assert.Equal("/highlight-image/1/preview?v=444", cut.Find(".scene-wall-card video").GetAttribute("src"));
    }

    [Fact]
    public void SwitchingToHighlights_UpdatesUrlAndReloadsWithoutHiddenFlag()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([Card(1, "ABC-123")], 1));
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([], 0));
        RememberedState("""{"ShowHidden":true,"Text":"abc"}""");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies/scenes");

        var cut = Render<MovieScenes>();
        cut.Find(".scene-wall-view-btn[data-view=highlights]").Click();

        Assert.EndsWith("/movies/scenes?view=highlights", nav.Uri);
        wall.Received().GetHighlightPageAsync(Arg.Is<SceneWallFilter>(f => f.Text == "abc" && !f.IncludeHidden), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Equal("No highlights match these filters.", cut.Find(".scene-wall-empty").TextContent.Trim());
        Assert.Empty(cut.FindAll(".scene-wall-card"));

        cut.Find(".scene-wall-view-btn[data-view=scenes]").Click();
        Assert.EndsWith("/movies/scenes", nav.Uri);
        Assert.Single(cut.FindAll(".scene-wall-card"));
    }

    [Fact]
    public void SwitchingViews_KeepsASelectionTheOtherViewDoesNotOffer_Unapplied()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([], 0));
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([], 0));
        wall.GetHighlightOptionsAsync(Arg.Any<CancellationToken>()).Returns(new SceneWallOptions([], [new SceneWallOption(7, "Aika")], ["S1"], ActorAttributeOptions.None));
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies/scenes");
        var cut = Render<MovieScenes>();
        ClickButton(cut, "Filter");
        ClickButton(cut, "Tag");
        ClickButton(cut, "Play › Rough");
        ClickButton(cut, "Actor");
        ClickButton(cut, "Name");
        ClickButton(cut, "Aika");
        Assert.Contains("Filter (2)", cut.Markup);

        cut.Find(".scene-wall-view-btn[data-view=highlights]").Click();

        wall.Received().GetHighlightPageAsync(Arg.Is<SceneWallFilter>(f => f.TagIds == null && f.ActorIds!.Single() == 7),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (1)", cut.Markup);

        // A filter change in this view still remembers the tag.
        ClickButton(cut, "Favorites only");
        Assert.Contains("\"TagIds\":[5]", (string)module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!);

        wall.ClearReceivedCalls();
        cut.Find(".scene-wall-view-btn[data-view=scenes]").Click();

        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.TagIds!.Single() == 5 && f.ActorIds!.Single() == 7 && f.FavoritesOnly),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (3)", cut.Markup);
    }

    [Fact]
    public void HighlightsView_Reset_KeepsWhatOnlyTheSceneViewOffers()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([], 0));
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([], 0));
        wall.GetHighlightOptionsAsync(Arg.Any<CancellationToken>()).Returns(new SceneWallOptions([], [new SceneWallOption(7, "Aika")], ["S1"], ActorAttributeOptions.None));
        RememberedState("""{"TagIds":[5],"ActorIds":[7],"ShowHidden":true}""");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies/scenes?view=highlights");
        var cut = Render<MovieScenes>();
        cut.WaitForAssertion(() => Assert.Contains("Filter (1)", cut.Markup));

        cut.Find(".dropdown-clear-btn").Click();

        wall.Received().GetHighlightPageAsync(Arg.Is<SceneWallFilter>(f => f.TagIds == null && f.ActorIds == null),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        var saved = (string)module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!;
        Assert.Contains("\"TagIds\":[5]", saved);
        Assert.Contains("\"ActorIds\":[]", saved);
        Assert.Contains("\"ShowHidden\":true", saved);

        wall.ClearReceivedCalls();
        cut.Find(".scene-wall-view-btn[data-view=scenes]").Click();

        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.IncludeHidden && f.TagIds!.Single() == 5 && f.ActorIds == null),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        ClickButton(cut, "Filter");
        Assert.Equal("true", cut.FindAll("button").First(b => b.TextContent.Contains("Show hidden scenes")).GetAttribute("aria-pressed"));
    }

    [Fact]
    public void HighlightsView_Empty_ExplainsWhereToAddThem()
    {
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([], 0));
        OpenHighlightsView();

        var cut = Render<MovieScenes>();

        Assert.StartsWith("No highlights yet.", cut.Find(".scene-wall-empty").TextContent.Trim());
    }

    [Fact]
    public void HighlightCards_MissingMedia_QueueOncePerMovie()
    {
        wall.GetHighlightPageAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new HighlightWallPage([HCard(1, "A-1", thumb: null), HCard(1, "A-1", thumb: null) with { HighlightId = 2 }], 2));
        OpenHighlightsView();

        Render<MovieScenes>();

        highlightMedia.Received(1).EnsureQueuedAsync(10, Arg.Any<CancellationToken>());
        media.DidNotReceive().EnsureQueuedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingASceneCard_PlaysItInTheClipPlayer_AndClosingKeepsTheWall()
    {
        var card = Card(1, "ABC-123");
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([card], 1));
        var cut = Render<MovieScenes>();
        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);

        cut.Find(".scene-wall-card").Click();

        Assert.Equal(PlayerClip.FromScene(card), cut.FindComponent<ClipPlayerModal>().Instance.Clip);
        cut.Find(".video-player-modal-close-btn").Click();
        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);
        Assert.Single(cut.FindAll(".scene-wall-card"));
    }

    [Fact]
    public void ClickingAHighlightCard_PlaysItInTheClipPlayer()
    {
        var card = HCard(1, "ABC-123");
        wall.GetHighlightPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new HighlightWallPage([card], 1));
        OpenHighlightsView();
        var cut = Render<MovieScenes>();

        cut.Find(".scene-wall-card").Click();

        Assert.Equal(PlayerClip.FromHighlight(card), cut.FindComponent<ClipPlayerModal>().Instance.Clip);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ModifierClicks_LeaveTheLinkToTheBrowser(bool ctrl, bool meta, bool shift)
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123")], 1));
        var cut = Render<MovieScenes>();

        cut.Find(".scene-wall-card").Click(new MouseEventArgs { CtrlKey = ctrl, MetaKey = meta, ShiftKey = shift });

        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);
    }

    [Fact]
    public void PlainCardClicks_AreKeptFromNavigating()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123")], 1));

        Render<MovieScenes>();

        Assert.Single(clipCardModule.Invocations, i => i.Identifier == "preventPlainCardClicks");
    }

    [Fact]
    public void CardTagRows_AreMeasuredForOverflow()
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Card(1, "ABC-123")], 1));

        Render<MovieScenes>();

        Assert.Single(clipCardModule.Invocations, i => i.Identifier == "measureTagOverflow");
    }

    private void ScenePages(IReadOnlyList<SceneWallCard> firstPage, IReadOnlyList<SceneWallCard> all)
    {
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage(firstPage, all.Count));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(all.Count), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage(all, all.Count));
    }

    private static ClipPlayerModal Player(IRenderedComponent<MovieScenes> cut) => cut.FindComponent<ClipPlayerModal>().Instance;

    [Fact]
    public async Task PlayAll_PlaysEveryMatchingClipInTheWallsOrder_ThenCloses()
    {
        SceneWallCard[] all = [Card(1, "ABC-1"), Card(2, "ABC-2"), Card(3, "ABC-3")];
        ScenePages(all[..2], all);
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Play all");

        // All three, not just the two loaded cards, with the wall's filters and sort.
        await wall.Received(1).GetPageAsync(Arg.Any<SceneWallFilter>(), SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(3), Arg.Any<CancellationToken>());
        Assert.Equal((PlayerClip.FromScene(all[0]), 1, 3), (Player(cut).Clip, Player(cut).Position, Player(cut).Count));

        await cut.InvokeAsync(() => Player(cut).OnEnded.InvokeAsync());
        Assert.Equal((PlayerClip.FromScene(all[1]), 2), (Player(cut).Clip, Player(cut).Position));
        await cut.InvokeAsync(() => Player(cut).OnNext.InvokeAsync());
        Assert.Equal(PlayerClip.FromScene(all[2]), Player(cut).Clip);
        await cut.InvokeAsync(() => Player(cut).OnPrevious.InvokeAsync());
        Assert.Equal(PlayerClip.FromScene(all[1]), Player(cut).Clip);
        await cut.InvokeAsync(() => Player(cut).OnNext.InvokeAsync());

        // After the last clip, its end closes the player.
        await cut.InvokeAsync(() => Player(cut).OnEnded.InvokeAsync());
        Assert.Null(Player(cut).Clip);
        Assert.Null(Player(cut).Position);
    }

    [Fact]
    public void PlayAll_LeavesOutClipsThatCantPlay()
    {
        // A movie whose local file is gone: its scene would stop the playlist on an error.
        SceneWallCard[] all = [Card(1, "ABC-1"), Card(2, "ABC-2") with { HasLocalVideo = false }, Card(3, "ABC-3")];
        ScenePages(all, all);
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Play all");

        Assert.Equal((PlayerClip.FromScene(all[0]), 1, 2), (Player(cut).Clip, Player(cut).Position, Player(cut).Count));
    }

    [Fact]
    public async Task Next_OnTheLastClip_ClosesThePlayer()
    {
        SceneWallCard[] all = [Card(1, "ABC-1")];
        ScenePages(all, all);
        var cut = Render<MovieScenes>();
        ClickButton(cut, "Play all");

        await cut.InvokeAsync(() => Player(cut).OnNext.InvokeAsync());

        Assert.Null(Player(cut).Clip);
    }

    [Fact]
    public void Shuffle_PlaysTheSameClips_InSomeOrder()
    {
        var all = Enumerable.Range(1, 5).Select(i => Card(i, $"ABC-{i}")).ToArray();
        ScenePages(all, all);
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Shuffle");

        Assert.Equal((1, 5), (Player(cut).Position, Player(cut).Count));
        Assert.Contains(Player(cut).Clip, all.Select(PlayerClip.FromScene));
    }

    [Fact]
    public void PlayAll_InTheHighlightsView_PlaysTheHighlights()
    {
        HighlightWallCard[] all = [HCard(1, "ABC-1"), HCard(2, "ABC-2")];
        wall.GetHighlightPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HighlightWallPage(all, 2));
        OpenHighlightsView();
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Play all");

        Assert.Equal((PlayerClip.FromHighlight(all[0]), 1, 2), (Player(cut).Clip, Player(cut).Position, Player(cut).Count));
    }

    [Fact]
    public void PlayAllAndShuffle_AreDisabled_WhenNothingMatches()
    {
        ScenePages([], []);

        var cut = Render<MovieScenes>();

        Assert.All(cut.FindAll(".scene-wall-play-btn"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.Equal(2, cut.FindAll(".scene-wall-play-btn").Count);
    }

    [Fact]
    public void ACardClick_AfterAPlaylist_PlaysJustThatClip()
    {
        SceneWallCard[] all = [Card(1, "ABC-1"), Card(2, "ABC-2")];
        ScenePages(all, all);
        var cut = Render<MovieScenes>();
        ClickButton(cut, "Play all");
        cut.Find(".video-player-modal-close-btn").Click();

        cut.FindAll(".scene-wall-card")[1].Click();

        Assert.Equal(PlayerClip.FromScene(all[1]), Player(cut).Clip);
        Assert.Null(Player(cut).Position);
        Assert.False(Player(cut).OnNext.HasDelegate);
    }

    private static SceneWallOptions OptionsWithActorAttributes(ActorAttributeOptions attributes) =>
        new([], [new SceneWallOption(7, "Aika")], ["S1"], attributes);

    private static string LastCookie(MovieScenesTests t) =>
        (string)t.module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!;

    [Fact]
    public void ActorAttributes_CupSizeChip_FiltersTheWall_PersistsAndResets()
    {
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(
            new ActorAttributeOptions { CupSizes = ["D", "E"], Height = new RangeBounds(150, 170) }));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");
        ClickButton(cut, "Actor");
        ClickButton(cut, "Cup Size");
        ClickButton(cut, "E");

        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.ActorAttributes!.CupSizes.Single() == "E" && !f.ActorAttributes.Height.IsSet),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (1)", cut.Markup);
        Assert.Contains("Height", cut.Markup);
        Assert.Contains("\"CupSizes\":[\"E\"]", LastCookie(this));

        cut.Find(".dropdown-clear-btn").Click();
        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.ActorAttributes == null),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ActorAttributes_NothingOffered_HidesEveryGroup()
    {
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(ActorAttributeOptions.None));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");

        Assert.DoesNotContain("Cup Size", cut.Markup);
        Assert.DoesNotContain("Height", cut.Markup);
        Assert.DoesNotContain("Bust", cut.Markup);
        Assert.DoesNotContain("Age", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_RememberedSelectionTheViewNoLongerOffers_IsNotApplied()
    {
        // Cup K (nobody has it) and an age floor below the offered bound (the end stop: no limit).
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(
            new ActorAttributeOptions { CupSizes = ["E"], Age = new RangeBounds(20, 40) }));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"ActorAttributes":{"CupSizes":["K"],"Age":{"Min":18}}}""");

        var cut = Render<MovieScenes>();

        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.ActorAttributes == null),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.DoesNotContain("Filter (", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_RememberedSelection_AppliesWhenTheViewOffersIt()
    {
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(
            new ActorAttributeOptions { CupSizes = ["E"], Age = new RangeBounds(19, 40), Bust = new RangeBounds(80, 100) }));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"ActorAttributes":{"CupSizes":["E"],"Age":{"Min":25,"Max":30},"Bust":{"Min":90}}}""");

        var cut = Render<MovieScenes>();

        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.ActorAttributes!.CupSizes.Single() == "E" && f.ActorAttributes.Age == new IntRange(25, 30) && f.ActorAttributes.Bust == new IntRange(90, null)),
            SceneWallSort.ReleaseDate, Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (3)", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_EditingInOneView_KeepsWhatOnlyTheOtherViewOffers()
    {
        // The scenes view offers cups and Age; the remembered selection also has a Bust range only Highlights offers.
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(
            new ActorAttributeOptions { CupSizes = ["D", "E"], Age = new RangeBounds(19, 40) }));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"ActorAttributes":{"CupSizes":["K"],"Bust":{"Min":90}}}""");
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");
        ClickButton(cut, "Actor");
        ClickButton(cut, "Cup Size");
        ClickButton(cut, "E");

        var saved = LastCookie(this);
        Assert.Contains("\"K\"", saved);      // not offered here: kept
        Assert.Contains("\"E\"", saved);      // edited
        Assert.Contains("\"Bust\":{\"Min\":90", saved); // not offered here: kept
    }

    [Fact]
    public void ActorSection_NestsTheNamePickerAndTheAttributeFilters_AndCountsBoth()
    {
        wall.GetOptionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OptionsWithActorAttributes(
            new ActorAttributeOptions { CupSizes = ["E"], Bust = new RangeBounds(80, 100) }));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        RememberedState("""{"ActorIds":[7],"ActorAttributes":{"CupSizes":["E"]}}""");
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");

        Assert.Contains("Actor (2)", cut.Markup);            // one actor by name + one cup size
        Assert.DoesNotContain("Cup Size", cut.Markup);       // nested, and collapsed
        ClickButton(cut, "Actor");
        var body = cut.Find(".filter-section-body").TextContent;
        Assert.Contains("Name (1)", body);
        Assert.Contains("Cup Size (1)", body);
        Assert.Contains("Bust", body);
    }

    // --- Apexes ---

    private static ApexWallCard ACard(int id, string code, long? preview = 555) =>
        new(id, id * 10, code, $"{code} title", $"Apex {id}", 125, ["Aika"], [new SceneTagItem(9, "Squirt", null)], id == 1, preview)
        {
            HasLocalVideo = true,
            JellyfinItemId = $"jf-{id}",
            DurationSeconds = 3600,
        };

    private void OpenApexesView() =>
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/scenes?view=apexes");

    [Fact]
    public void SceneAndHighlightCards_ShowHowManyApexesTheyContain()
    {
        wall.GetPageAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new SceneWallPage([Card(1, "ABC-123") with { ApexCount = 2 }, Card(2, "XYZ-9")], 2));
        wall.GetHighlightPageAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new HighlightWallPage([HCard(1, "ABC-123") with { ApexCount = 1 }], 1));
        var cut = Render<MovieScenes>();

        var badge = cut.Find(".scene-wall-apex-count");
        Assert.Equal("2", badge.TextContent.Trim());
        Assert.Equal("Contains 2 apexes", badge.GetAttribute("title"));
        Assert.Null(cut.FindAll(".scene-wall-card")[1].QuerySelector(".scene-wall-apex-count"));

        cut.Find(".scene-wall-view-btn[data-view=highlights]").Click();

        Assert.Equal("Contains 1 apex", cut.Find(".scene-wall-card-highlight .scene-wall-apex-count").GetAttribute("title"));
    }

    [Fact]
    public void ApexFilters_KeepClipsContainingAnApex_OfTheSelectedTags()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([], 0));
        var cut = Render<MovieScenes>();

        ClickButton(cut, "Filter");
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Apex") && !b.TextContent.Contains("Apex tag")).Click();
        ClickButton(cut, "Contains an apex");
        ClickButton(cut, "Apex tag");
        ClickButton(cut, "Squirt");

        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => f.ApexOnly && f.ApexTagIds!.Single() == 9),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Contains("Filter (2)", cut.Markup);
        var saved = (string)module.Invocations.Last(i => i.Identifier == "setJsonCookie").Arguments[1]!;
        Assert.Contains("\"ApexOnly\":true", saved);
        Assert.Contains("\"ApexTagIds\":[9]", saved);

        cut.Find(".dropdown-clear-btn").Click();
        wall.Received().GetPageAsync(
            Arg.Is<SceneWallFilter>(f => !f.ApexOnly && f.ApexTagIds == null),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ApexesView_ShowsApexCards_WithTheirPreviewStillUntilHovered()
    {
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([ACard(1, "ABC-123")], 1));
        OpenApexesView();

        var cut = Render<MovieScenes>();

        var card = cut.Find(".scene-wall-card");
        Assert.Contains("scene-wall-card-apex", card.ClassList);
        Assert.Equal("/movies/ABC-123?t=120", card.GetAttribute("href"));
        // No title or duration: "Apex N" and the clip's fixed length say nothing.
        Assert.Null(card.QuerySelector(".scene-wall-scene-title"));
        Assert.Null(card.QuerySelector(".scene-wall-duration"));
        Assert.DoesNotContain("Apex 1", card.TextContent);
        Assert.Contains("Aika", card.TextContent);
        Assert.Contains("Squirt", card.TextContent);
        Assert.Single(cut.FindAll(".scene-wall-fav"));
        Assert.Equal("1 apex", cut.Find(".scene-wall-count").TextContent);
        Assert.Equal("true", cut.Find(".scene-wall-view-btn[data-view=apexes]").GetAttribute("aria-pressed"));
        var still = Assert.Single(cut.FindAll(".scene-wall-card video"));
        Assert.Equal("/apex-image/1/preview?v=555", still.GetAttribute("src"));
        Assert.False(still.HasAttribute("autoplay"));

        cut.Find(".scene-wall-card").MouseEnter();

        // The playing copy goes over the still, which stays as its backdrop.
        var videos = cut.FindAll(".scene-wall-card video");
        Assert.Equal(2, videos.Count);
        Assert.False(videos[0].HasAttribute("autoplay"));
        Assert.True(videos[1].HasAttribute("autoplay"));
        wall.DidNotReceive().GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ApexesView_MissingPreview_IsQueued_AndShowsAPlaceholder()
    {
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([ACard(2, "NOM-1", preview: null)], 1));
        OpenApexesView();

        var cut = Render<MovieScenes>();

        Assert.Empty(cut.FindAll(".scene-wall-card video"));
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
        apexMedia.Received(1).EnsureQueuedAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ApexesView_ItsTagFilterIsTheApexTagSelection_AndOffersNoApexOrHiddenToggle()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([], 0));
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([], 0));
        RememberedState("""{"ApexOnly":true,"ShowHidden":true}""");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies/scenes");
        var cut = Render<MovieScenes>();

        cut.Find(".scene-wall-view-btn[data-view=apexes]").Click();

        Assert.EndsWith("/movies/scenes?view=apexes", nav.Uri);
        wall.Received().GetApexPageAsync(Arg.Is<SceneWallFilter>(f => !f.ApexOnly && !f.IncludeHidden),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
        Assert.Equal("No apexes yet. Mark them from a movie's player (Movie Detail → Edit scenes).", cut.Find(".scene-wall-empty").TextContent.Trim());
        ClickButton(cut, "Filter");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Show hidden scenes"));
        Assert.DoesNotContain(cut.FindAll(".sort-dropdown-item"), b => b.TextContent.Contains("Apex"));
        ClickButton(cut, "Tag");
        ClickButton(cut, "Squirt");

        wall.Received().GetApexPageAsync(Arg.Is<SceneWallFilter>(f => f.ApexTagIds!.Single() == 9 && f.TagIds == null),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());

        // Back in the scene view the same selection is its Apex tag filter, with Contains an apex still on.
        wall.ClearReceivedCalls();
        cut.Find(".scene-wall-view-btn[data-view=scenes]").Click();
        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.ApexOnly && f.ApexTagIds!.Single() == 9 && f.IncludeHidden),
            Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(48), Arg.Any<CancellationToken>());
    }
}
