using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieScenesSectionTests : BunitContext
{
    private readonly IMovieSceneService sceneService = Substitute.For<IMovieSceneService>();
    private readonly IMovieHighlightService highlightService = Substitute.For<IMovieHighlightService>();
    private readonly ISceneMediaService sceneMedia = Substitute.For<ISceneMediaService>();
    private readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    private readonly IMovieApexService apexService = Substitute.For<IMovieApexService>();
    private readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();
    private readonly ISceneMediaService workerMedia = Substitute.For<ISceneMediaService>();
    private readonly ITrickplayService trickplayService = Substitute.For<ITrickplayService>();
    private readonly IHighlightTrickplayService highlightTrickplay = Substitute.For<IHighlightTrickplayService>();
    private readonly SceneMediaQueue mediaQueue;

    private static readonly Movie PlayableMovie = new()
    {
        Id = 7,
        Code = "ABC-123",
        JellyfinItemId = "jf-item-42",
        JellyfinServerId = "srv",
        MediaDurationSeconds = 3600,
    };

    private static readonly Movie LocalMovie = new()
    {
        Id = 7,
        Code = "ABC-123",
        MediaDurationSeconds = 3600,
        LocalFileSizeBytes = 1_000_000,
    };

    private static readonly IReadOnlyList<SceneItem> TwoScenes =
    [
        new(1, 1, "Interview", "Interview", 0, 750, 750) { Actors = [new SceneActorItem(11, "Alice"), new SceneActorItem(12, "Bea")], Tags = [new SceneTagItem(5, "Kiss", "Play")] },
        new(2, 2, "Scene 2", null, 750, null, 3600) { IsFavorite = true, ThumbVersion = 99, PreviewVersion = 100 },
    ];

    // The first two start close together, so their pins take separate lanes; the third is far away.
    private static readonly IReadOnlyList<HighlightItem> ThreeHighlights =
    [
        new(21, 1, "Highlight 1", null, 600, 640, false, 0) { ThumbVersion = 5 },
        new(22, 2, "Kiss", "Kiss", 700, 745, true, 0),
        new(23, 3, "Highlight 3", null, 3000, 3030, false, 0),
    ];

    public MovieScenesSectionTests()
    {
        Services.AddSingleton(sceneService);
        Services.AddSingleton(highlightService);
        Services.AddSingleton(apexService);
        Services.AddSingleton(apexMedia);
        apexMedia.ServesPreview.Returns(true);
        Services.AddSingleton(sceneMedia);
        Services.AddSingleton(highlightMedia);
        sceneMedia.ServesVariant(SceneMediaService.VariantThumb).Returns(true);
        highlightMedia.ServesVariant(SceneMediaService.VariantThumb).Returns(true);
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        Services.AddSingleton(StreamServiceStubs.AllPlayable());
        Services.AddSingleton(trickplayService);
        Services.AddSingleton(new TrickplayGenerationTracker());
        Services.AddSingleton(highlightTrickplay);
        // A real queue whose worker resolves workerMedia, so a test can drive a "generated" event.
        var workerServices = new ServiceCollection().AddSingleton(workerMedia).BuildServiceProvider();
        mediaQueue = new SceneMediaQueue(workerServices.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        Services.AddSingleton(mediaQueue);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) mediaQueue.Dispose();
    }

    private IRenderedComponent<MovieScenesSection> RenderSection(
        Movie? movie = null,
        IReadOnlyList<SceneItem>? scenes = null,
        IReadOnlyList<HighlightItem>? highlights = null,
        bool canPlay = true,
        Action<IReadOnlyList<SceneItem>>? onScenesChanged = null,
        Action<IReadOnlyList<HighlightItem>>? onHighlightsChanged = null,
        Action<(double? Start, double? End)>? onEdit = null) =>
        Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, movie ?? PlayableMovie)
            .Add(x => x.Scenes, scenes ?? TwoScenes)
            .Add(x => x.Highlights, highlights ?? ThreeHighlights)
            .Add(x => x.HighlightCounts, new Dictionary<int, int> { [1] = 2 })
            .Add(x => x.CanPlay, canPlay)
            .Add(x => x.ActorImageUrl, id => id == 11 ? "/actor-image/11/thumb" : null)
            .Add(x => x.OnScenesChanged, onScenesChanged ?? (_ => { }))
            .Add(x => x.OnHighlightsChanged, onHighlightsChanged ?? (_ => { }))
            .Add(x => x.OnEdit, onEdit ?? (_ => { })));

    [Fact]
    public void Timeline_DrawsEachSceneToScale_WithTimeMarksToTheMoviesEnd()
    {
        var cut = RenderSection();

        var segments = cut.FindAll(".movie-scenes-timeline-segment");
        Assert.Equal(["left:0%;width:20.833%", "left:20.833%;width:79.167%"], segments.Select(s => s.GetAttribute("style")));
        Assert.Equal(["Interview", "Scene 2"], segments.Select(s => s.TextContent.Trim()));
        Assert.Equal(["0:00", "10:00", "20:00", "30:00", "40:00", "50:00", "1:00:00"],
            cut.FindAll(".movie-scenes-timeline-tick").Select(t => t.TextContent));
    }

    [Fact]
    public void Timeline_PinsHighlights_DroppingOnesThatWouldCollideToANewLane()
    {
        var cut = RenderSection();

        var pins = cut.FindAll(".movie-scenes-timeline-pin");
        Assert.Equal(["Highlight 1", "Kiss", "Highlight 3"], pins.Select(p => p.QuerySelector(".movie-scenes-timeline-pin-name")!.TextContent));
        Assert.Equal(["top:10px", "top:34px", "top:10px"], pins.Select(p => p.GetAttribute("style")!.Split(';')[1]));
    }

    [Fact]
    public void Timeline_MarksApexes_LabelledByTags()
    {
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, [new(1, 1, 900, [new SceneTagItem(5, "Squirt", null)]), new(2, 2, 1800, [])])
            .Add(x => x.CanPlay, true));

        var markers = cut.FindAll(".movie-scenes-timeline-apex");
        Assert.Equal(["left:25%", "left:50%"], markers.Select(m => m.GetAttribute("style")));
        Assert.Equal(["Squirt", "Apex"], markers.Select(m => m.GetAttribute("data-highlight-name")));
        Assert.Equal("900", markers[0].GetAttribute("data-start"));
    }

    [Fact]
    public async Task AnApexMarker_PlaysItsClipAmongTheApexes_NotTheFullPlayer()
    {
        double? playedAt = null;
        // The first by its own window, the last cut at the movie's end.
        IReadOnlyList<ApexItem> apexes = [new(1, 1, 900, [new SceneTagItem(5, "Squirt", null)]) { Window = new(8, 3) }, new(2, 2, 3598, [])];
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, apexes)
            .Add(x => x.CanPlay, true)
            .Add(x => x.OnPlayAt, seconds => playedAt = seconds));

        cut.FindAll(".movie-scenes-timeline-apex")[0].Click();

        Assert.Null(playedAt);
        var player = cut.FindComponent<ClipPlayerModal>();
        Assert.Equal((PlayerClipKind.Apex, 1, "Squirt", 892d, (double?)903d), (player.Instance.Clip!.Kind, player.Instance.Clip.Id, player.Instance.Clip.DisplayTitle, player.Instance.Clip.StartSeconds, player.Instance.Clip.EndSeconds));
        Assert.Equal(("jf-item-42", "srv", (double?)3600), (player.Instance.Clip.JellyfinItemId, player.Instance.Clip.JellyfinServerId, player.Instance.Clip.DurationSeconds));
        Assert.Equal((1, 2), (player.Instance.Position, player.Instance.Count));

        await cut.InvokeAsync(player.Instance.OnNext.InvokeAsync);
        Assert.Equal((2, 3593d, (double?)3600d, 2), (player.Instance.Clip!.Id, player.Instance.Clip.StartSeconds, player.Instance.Clip.EndSeconds, player.Instance.Position));
        await cut.InvokeAsync(player.Instance.OnPrevious.InvokeAsync);
        Assert.Equal(1, player.Instance.Clip!.Id);
    }

    [Fact]
    public void AnApexMarker_PlaysNothing_WhenTheMovieCantPlay()
    {
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, TwoApexes)
            .Add(x => x.CanPlay, false));

        cut.FindAll(".movie-scenes-timeline-apex")[0].Click();

        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);
    }

    [Fact]
    public void Timeline_MarksScenesAndPins_WithTheRangeAndNameTheHoverPreviewShows()
    {
        var cut = RenderSection();

        var segments = cut.FindAll(".movie-scenes-timeline-segment");
        Assert.Equal(["0|750|Interview", "750|3600|Scene 2"],
            segments.Select(s => $"{s.GetAttribute("data-start")}|{s.GetAttribute("data-end")}|{s.GetAttribute("data-scene-name")}"));
        var pins = cut.FindAll(".movie-scenes-timeline-pin");
        Assert.Equal(["21|600|640|Highlight 1", "22|700|745|Kiss", "23|3000|3030|Highlight 3"],
            pins.Select(p => $"{p.GetAttribute("data-highlight-id")}|{p.GetAttribute("data-start")}|{p.GetAttribute("data-end")}|{p.GetAttribute("data-highlight-name")}"));
    }

    [Fact]
    public void Timeline_HandsTheMoviesTrickplayToTheHoverPreview()
    {
        var layout = new TrickplayLayout(320, 180, 10, 10, 360, 10_000, 3600, "/trickplay/7/abc/{index}.webp");
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(layout);
        var module = JSInterop.SetupModule("./Components/Pages/MovieDetailSections/MovieScenesSection.razor.js");

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Contains(module.Invocations["initTimeline"], i => Equals(i.Arguments[1], layout)));
        Assert.Equal(3600d, module.Invocations["initTimeline"].Last().Arguments[3]);
        Assert.Single(cut.FindAll(".movie-scenes-timeline-preview .movie-scenes-timeline-thumb"));
    }

    [Fact]
    public void Timeline_HandsHighlightsDenseTrickplayToTheirPins()
    {
        var dense = new TrickplayLayout(320, 180, 10, 10, 46, 1000, 45, "/trickplay/7/dense/{index}.webp", 700);
        highlightTrickplay.GetAsync(22, Arg.Any<CancellationToken>()).Returns(dense);
        var module = JSInterop.SetupModule("./Components/Pages/MovieDetailSections/MovieScenesSection.razor.js");

        var cut = RenderSection();

        // Only the highlight with a set of its own; the others preview from the movie's (none here).
        cut.WaitForAssertion(() => Assert.Contains(module.Invocations["initTimeline"],
            i => i.Arguments[2] is IReadOnlyDictionary<int, TrickplayLayout> sets && sets.Count == 1 && sets[22] == dense));
        Assert.Null(module.Invocations["initTimeline"].Last().Arguments[1]);
        Assert.Single(cut.FindAll(".movie-scenes-timeline-thumb"));
    }

    [Fact]
    public void Timeline_WithoutTrickplay_PreviewsJustTheTimeAndName()
    {
        var module = JSInterop.SetupModule("./Components/Pages/MovieDetailSections/MovieScenesSection.razor.js");

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Null(module.VerifyInvoke("initTimeline").Arguments[1]));
        Assert.Empty(cut.FindAll(".movie-scenes-timeline-thumb"));
        Assert.Single(cut.FindAll(".movie-scenes-timeline-preview .movie-scenes-timeline-time"));
    }

    [Fact]
    public void Timeline_IsLeftOut_WhileTheRuntimeIsUnknown()
    {
        var cut = RenderSection(movie: new Movie { Id = 7, Code = "ABC-123", JellyfinItemId = "jf-item-42" });

        Assert.Empty(cut.FindAll(".movie-scenes-timeline"));
        Assert.Equal(2, cut.FindAll(".movie-scenes-grid .movie-scenes-card").Count);
    }

    [Fact]
    public void HighlightShelf_ShowsTitleBelowThePicture_WithDurationRangeAndFavorite()
    {
        var cut = RenderSection();

        Assert.Equal("Highlights (3)", cut.Find(".movie-scenes-subheader h3").TextContent);
        var card = cut.FindAll(".movie-scenes-shelf .movie-scenes-card")[1];
        Assert.Equal("0:45", card.QuerySelector(".movie-scenes-media .movie-scenes-duration")!.TextContent);
        Assert.DoesNotContain("Kiss", card.QuerySelector(".movie-scenes-media")!.TextContent);
        Assert.Equal("Kiss", card.QuerySelector(".movie-scenes-card-body .movie-scenes-card-title")!.TextContent);
        Assert.Equal("11:40–12:25", card.QuerySelector(".movie-scenes-range")!.TextContent);
        Assert.Equal("true", card.QuerySelector(".movie-scenes-fav-btn")!.GetAttribute("aria-pressed"));
        Assert.Equal("/highlight-image/21/thumb?v=5", cut.Find(".movie-scenes-shelf img").GetAttribute("src"));
    }

    [Fact]
    public void SceneCards_ShowPositionTitleRangeHighlightCountActorsAndTags()
    {
        var cut = RenderSection();

        var cards = cut.FindAll(".movie-scenes-grid .movie-scenes-card");
        Assert.Equal("1. Interview", cards[0].QuerySelector(".movie-scenes-card-title")!.TextContent);
        Assert.Equal("0:00–12:30", cards[0].QuerySelector(".movie-scenes-range")!.TextContent);
        Assert.Equal("2 highlights", cards[0].QuerySelector(".movie-scenes-highlight-count")!.GetAttribute("title"));
        Assert.Equal("/actor-image/11/thumb", cards[0].QuerySelector("img.movie-scenes-actor-avatar")!.GetAttribute("src"));
        Assert.Equal("B", cards[0].QuerySelector(".movie-scenes-actor-initial")!.TextContent);
        Assert.Equal("Play › Kiss", cards[0].QuerySelector(".movie-scenes-tag")!.TextContent);
        Assert.Null(cards[1].QuerySelector(".movie-scenes-highlight-count"));
        Assert.Equal("12:30–1:00:00", cards[1].QuerySelector(".movie-scenes-range")!.TextContent);
        Assert.Contains("movie-scenes-card-title-default", cards[1].QuerySelector(".movie-scenes-card-title")!.ClassName);
    }

    [Fact]
    public void SceneCards_WithoutOwnActors_ShowTheCastGrayed()
    {
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Cast, [new SceneActorItem(13, "Cleo")]));

        var cards = cut.FindAll(".movie-scenes-grid .movie-scenes-card");
        Assert.DoesNotContain("movie-scenes-actors-inherited", cards[0].QuerySelector(".movie-scenes-actors")!.ClassName);
        var inherited = cards[1].QuerySelector(".movie-scenes-actors")!;
        Assert.Contains("movie-scenes-actors-inherited", inherited.ClassName);
        Assert.Equal("From the cast", inherited.GetAttribute("title"));
        Assert.Equal("Cleo", inherited.QuerySelector(".movie-scenes-actor")!.LastChild!.TextContent.Trim());
    }

    [Fact]
    public void HighlightCards_ShowOwnActors_AndInheritedOnesGrayed()
    {
        IReadOnlyList<HighlightItem> highlights =
        [
            ThreeHighlights[0],
            ThreeHighlights[1] with { Actors = [new SceneActorItem(12, "Bea")] },
            ThreeHighlights[2],
        ];
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Highlights, highlights)
            .Add(x => x.Cast, [new SceneActorItem(13, "Cleo")])
            .Add(x => x.ActorImageUrl, id => id == 11 ? "/actor-image/11/thumb" : null));

        var actors = cut.FindAll(".movie-scenes-card-highlight .movie-scenes-actors");
        Assert.Equal(3, actors.Count);
        // From its scene's actors, grayed.
        Assert.Equal(["Alice", "Bea"], actors[0].QuerySelectorAll(".movie-scenes-actor").Select(a => a.LastChild!.TextContent.Trim()));
        Assert.Contains("movie-scenes-actors-inherited", actors[0].ClassName);
        Assert.Equal("From scene 1", actors[0].GetAttribute("title"));
        Assert.Equal("/actor-image/11/thumb", actors[0].QuerySelector("img.movie-scenes-actor-avatar")!.GetAttribute("src"));
        // Its own, not grayed.
        Assert.Equal("Bea", actors[1].QuerySelector(".movie-scenes-actor")!.LastChild!.TextContent.Trim());
        Assert.DoesNotContain("movie-scenes-actors-inherited", actors[1].ClassName);
        Assert.Null(actors[1].GetAttribute("title"));
        // Its scene has no actors, so the cast, grayed.
        Assert.Equal("Cleo", actors[2].QuerySelector(".movie-scenes-actor")!.LastChild!.TextContent.Trim());
        Assert.Equal("From the cast", actors[2].GetAttribute("title"));
    }

    [Fact]
    public void Timeline_DrawsFavoriteApexesDistinctly()
    {
        IReadOnlyList<ApexItem> apexes = [new(1, 1, 900, []) { IsFavorite = true }, new(2, 2, 1800, [])];
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, apexes));

        var markers = cut.FindAll(".movie-scenes-timeline-apex");
        Assert.Equal([true, false], markers.Select(m => m.ClassList.Contains("movie-scenes-timeline-apex-fav")));
    }

    [Fact]
    public void Apexes_ShowACountOnEachSceneCardThatHasSome()
    {
        IReadOnlyList<ApexItem> apexes = [new(1, 1, 900, []), new(2, 2, 1000, []), new(3, 3, 1800, [])];
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, apexes)
            .Add(x => x.ApexCounts, new Dictionary<int, int> { [1] = 1, [2] = 2 }));

        Assert.Equal("Scenes (2)", cut.Find(".movie-scenes-header h2").TextContent);
        var badges = cut.FindAll(".movie-scenes-grid .movie-scenes-apex-count");
        Assert.Equal(["1 apex", "2 apexes"], badges.Select(b => b.GetAttribute("title")));
        Assert.Equal("2", badges[1].TextContent.Trim());
    }

    [Fact]
    public void WithoutApexes_NoApexCountsAreShown()
    {
        var cut = RenderSection();

        Assert.Empty(cut.FindAll(".movie-scenes-apex-count"));
    }

    [Fact]
    public void HoveringASceneCard_PlaysItsPreviewOverTheScreenshot()
    {
        var cut = RenderSection();

        var card = cut.FindAll(".movie-scenes-grid .movie-scenes-card")[1];
        Assert.Null(card.QuerySelector("video"));
        card.MouseEnter();

        card = cut.FindAll(".movie-scenes-grid .movie-scenes-card")[1];
        Assert.Equal("/scene-image/2/thumb?v=99", card.QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("/scene-image/2/preview?v=100", card.QuerySelector("video")!.GetAttribute("src"));
        cut.FindAll(".movie-scenes-grid .movie-scenes-card")[1].MouseLeave();
        Assert.Null(cut.FindAll(".movie-scenes-grid .movie-scenes-card")[1].QuerySelector("video"));
    }

    [Fact]
    public async Task AHighlightPin_PlaysAmongTheHighlights_AndNextStepsThroughThemThenCloses()
    {
        var cut = RenderSection();

        cut.FindAll(".movie-scenes-timeline-pin")[1].Click();

        var player = cut.FindComponent<ClipPlayerModal>();
        Assert.Equal((PlayerClipKind.Highlight, 22, 700d, (double?)745d), (player.Instance.Clip!.Kind, player.Instance.Clip.Id, player.Instance.Clip.StartSeconds, player.Instance.Clip.EndSeconds));
        Assert.Equal(("jf-item-42", "srv", (double?)3600), (player.Instance.Clip.JellyfinItemId, player.Instance.Clip.JellyfinServerId, player.Instance.Clip.DurationSeconds));
        Assert.Equal((2, 3), (player.Instance.Position, player.Instance.Count));

        await cut.InvokeAsync(player.Instance.OnNext.InvokeAsync);
        Assert.Equal(23, player.Instance.Clip!.Id);
        await cut.InvokeAsync(player.Instance.OnNext.InvokeAsync);
        Assert.Null(player.Instance.Clip);
    }

    [Fact]
    public void ASceneSegmentOrCard_PlaysAmongTheScenes()
    {
        var cut = RenderSection();

        cut.FindAll(".movie-scenes-timeline-segment")[1].Click();
        var player = cut.FindComponent<ClipPlayerModal>().Instance;
        Assert.Equal((PlayerClipKind.Scene, 2, 2, 2), (player.Clip!.Kind, player.Clip.Id, player.Position, player.Count));

        cut.FindAll(".movie-scenes-grid .movie-scenes-play")[0].Click();
        Assert.Equal((1, 1), (player.Clip!.Id, player.Position));
    }

    [Fact]
    public void TheClipPlayersEdit_ClosesItAndOpensTheEditorForTheClipsRange()
    {
        var edits = new List<(double?, double?)>();
        var cut = RenderSection(onEdit: edits.Add);

        cut.FindAll(".movie-scenes-shelf .movie-scenes-play")[0].Click();
        cut.Find("button.clip-player-edit-btn").Click();
        cut.FindAll(".movie-scenes-grid .movie-scenes-play")[1].Click();
        cut.Find("button.clip-player-edit-btn").Click();
        cut.Find(".movie-scenes-edit-btn").Click();

        Assert.Equal([(600d, 640d), (750d, null), (null, null)], edits);
        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);
    }

    [Fact]
    public void WithoutAPlayableItem_NothingPlays_AndThereIsNoEditButton()
    {
        var cut = RenderSection(canPlay: false);

        Assert.Empty(cut.FindAll(".movie-scenes-play"));
        Assert.Empty(cut.FindAll(".movie-scenes-edit-btn"));
        Assert.All(cut.FindAll(".movie-scenes-timeline-segment, .movie-scenes-timeline-pin"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void Favorites_AreSavedFromTheCards_AndReportedToThePage()
    {
        sceneService.ToggleFavoriteAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        highlightService.ToggleFavoriteAsync(21, Arg.Any<CancellationToken>()).Returns(true);
        IReadOnlyList<SceneItem>? changedScenes = null;
        IReadOnlyList<HighlightItem>? changedHighlights = null;
        var cut = RenderSection(onScenesChanged: s => changedScenes = s, onHighlightsChanged: h => changedHighlights = h);

        cut.FindAll(".movie-scenes-grid .movie-scenes-fav-btn")[0].Click();
        cut.FindAll(".movie-scenes-shelf .movie-scenes-fav-btn")[0].Click();

        Assert.Equal([true, true], changedScenes!.Select(s => s.IsFavorite));
        Assert.Equal([true, true, false], changedHighlights!.Select(h => h.IsFavorite));
    }

    [Fact]
    public void NoScenesOrHighlights_SaysSo_UnderABareTimeline()
    {
        var cut = RenderSection(scenes: [], highlights: []);

        Assert.Equal("Scenes", cut.Find("h2").TextContent);
        Assert.Equal("No scenes yet.", cut.Find(".movie-scenes-empty").TextContent);
        Assert.Single(cut.FindAll(".movie-scenes-timeline-bar.movie-scenes-timeline-bar-playable"));
        Assert.Empty(cut.FindAll(".movie-scenes-timeline-segment"));
        Assert.Equal(["0:00", "10:00", "20:00", "30:00", "40:00", "50:00", "1:00:00"],
            cut.FindAll(".movie-scenes-timeline-tick").Select(t => t.TextContent));
    }

    [Fact]
    public void NoScenes_NeitherPlayableNorWithTrickplay_LeavesTheTimelineOut()
    {
        var cut = RenderSection(scenes: [], highlights: [], canPlay: false);

        Assert.Empty(cut.FindAll(".movie-scenes-timeline"));
        Assert.Equal("No scenes yet.", cut.Find(".movie-scenes-empty").TextContent);
    }

    [Fact]
    public void NoScenes_WithTrickplay_ShowsTheTimelineToScrub_EvenWhenNotPlayable()
    {
        var layout = new TrickplayLayout(320, 180, 10, 10, 360, 10_000, 3600, "/trickplay/7/abc/{index}.webp");
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(layout);
        var module = JSInterop.SetupModule("./Components/Pages/MovieDetailSections/MovieScenesSection.razor.js");

        var cut = RenderSection(scenes: [], highlights: [], canPlay: false);

        cut.WaitForAssertion(() => Assert.Contains(module.Invocations["initTimeline"], i => Equals(i.Arguments[1], layout)));
        Assert.Empty(cut.FindAll(".movie-scenes-timeline-bar-playable"));
    }

    [Theory]
    [InlineData(true, 1234d)]
    [InlineData(false, null)]
    public async Task ClickingTheBareTimeline_PlaysFromThere_OnlyWhenPlayable(bool canPlay, double? expected)
    {
        double? played = null;
        var cut = Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, PlayableMovie)
            .Add(x => x.CanPlay, canPlay)
            .Add(x => x.OnPlayAt, seconds => played = seconds));

        await cut.InvokeAsync(() => cut.Instance.PlayFromTimelineAsync(1234));

        Assert.Equal(expected, played);
    }

    [Fact]
    public async Task GeneratedMediaForThisMovie_ReloadsTheCards_AndReportsThemToThePage()
    {
        IReadOnlyList<SceneItem>? changedScenes = null;
        var changedHighlights = new TaskCompletionSource<IReadOnlyList<HighlightItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = RenderSection(scenes: [TwoScenes[0]], highlights: [ThreeHighlights[1]],
            onScenesChanged: s => changedScenes = s, onHighlightsChanged: h => changedHighlights.TrySetResult(h));
        Assert.Empty(cut.FindAll(".movie-scenes-media img"));

        // The media appears (as if the worker wrote it); another movie's is reported first and ignored.
        var scene = TwoScenes[0] with { ThumbVersion = 1, PreviewVersion = 2 };
        var highlight = ThreeHighlights[1] with { ThumbVersion = 3, PreviewVersion = 4 };
        sceneService.GetScenesAsync(PlayableMovie.Id, Arg.Any<CancellationToken>()).Returns([scene]);
        highlightService.GetHighlightsAsync(PlayableMovie.Id, Arg.Any<CancellationToken>()).Returns([highlight]);
        workerMedia.GenerateAsync(90, Arg.Any<CancellationToken>()).Returns(8);
        workerMedia.GenerateAsync(1, Arg.Any<CancellationToken>()).Returns(PlayableMovie.Id);
        await mediaQueue.StartAsync(CancellationToken.None);
        mediaQueue.Enqueue(90, 8, new SceneMediaWindow(0, 60_000));
        mediaQueue.Enqueue(1, PlayableMovie.Id, new SceneMediaWindow(0, 750_000));

        Assert.Equal([highlight], await changedHighlights.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal([scene], changedScenes);
        await sceneService.DidNotReceive().GetScenesAsync(8, Arg.Any<CancellationToken>());
        await mediaQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void CardsWithoutAScreenshot_ShimmerWhileItIsGenerated()
    {
        var cut = RenderSection(movie: LocalMovie);

        // Scene 1 and highlights 22 and 23 have no screenshot yet; scene 2 and highlight 21 do.
        Assert.Equal(3, cut.FindAll(".movie-scenes-media .scene-preview-shimmer").Count);
        Assert.Empty(cut.FindAll(".scene-preview-none"));
    }

    [Fact]
    public void CardsWithoutAScreenshot_SayNoPreview_WhenNoneCanBeMade()
    {
        highlightMedia.ServesVariant(SceneMediaService.VariantThumb).Returns(false);

        // No local video: nothing to take a scene's screenshot from.
        var withoutVideo = RenderSection();
        Assert.Equal(3, withoutVideo.FindAll(".scene-preview-none").Count);
        Assert.Empty(withoutVideo.FindAll(".scene-preview-shimmer"));

        // A cache mode that doesn't keep highlight screenshots: only the scene card shimmers.
        var withVideo = RenderSection(movie: LocalMovie);
        Assert.Single(withVideo.FindAll(".movie-scenes-grid .scene-preview-shimmer"));
        Assert.Equal(["No preview", "No preview"], withVideo.FindAll(".movie-scenes-shelf .scene-preview-none").Select(p => p.TextContent));
    }

    private static readonly IReadOnlyList<ApexItem> TwoApexes =
    [
        new(1, 1, 900, [new SceneTagItem(5, "Squirt", null)]) { PreviewVersion = 77 },
        new(2, 2, 1800, []),
    ];

    private IRenderedComponent<MovieScenesSection> RenderWithApexes(Movie movie, Action<IReadOnlyList<ApexItem>>? onApexesChanged = null) =>
        Render<MovieScenesSection>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.Scenes, TwoScenes)
            .Add(x => x.Apexes, TwoApexes)
            .Add(x => x.CanPlay, true)
            .Add(x => x.OnApexesChanged, onApexesChanged ?? (_ => { })));

    [Fact]
    public void HoveringAnApex_ShowsItsPreviewAboveIt_UntilThePointerLeaves()
    {
        var cut = RenderWithApexes(LocalMovie);
        Assert.Empty(cut.FindAll(".apex-preview"));
        var marker = cut.FindAll(".movie-scenes-timeline-apex")[0];
        // The popover replaces the browser tooltip.
        Assert.False(marker.HasAttribute("title"));

        marker.TriggerEvent("onmouseenter", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal("/apex-image/1/preview?v=77", cut.Find(".apex-preview video").GetAttribute("src"));
        Assert.Equal(["Squirt"], cut.FindAll(".apex-preview-tag").Select(t => t.TextContent));
        Assert.Equal("15:00", cut.Find(".apex-preview-time").TextContent);
        Assert.Contains("--x:0.25", cut.Find(".movie-scenes-timeline-apex-popover").GetAttribute("style"));

        cut.FindAll(".movie-scenes-timeline-apex")[0].TriggerEvent("onmouseleave", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Empty(cut.FindAll(".apex-preview"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AnApexWithoutAPreviewYet_ShimmersWhileOneCanBeMade(bool servesPreview, bool shimmer)
    {
        apexMedia.ServesPreview.Returns(servesPreview);
        var cut = RenderWithApexes(LocalMovie);

        cut.FindAll(".movie-scenes-timeline-apex")[1].TriggerEvent("onmouseenter", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Empty(cut.FindAll(".apex-preview video"));
        Assert.Equal(shimmer, cut.FindAll(".apex-preview .scene-preview-shimmer").Count == 1);
        Assert.Equal(["Apex"], cut.FindAll(".apex-preview-tag").Select(t => t.TextContent));
    }

    [Fact]
    public void AnApexOfAMovieWithoutLocalVideo_HasNoPreviewComing()
    {
        var cut = RenderWithApexes(PlayableMovie);

        cut.FindAll(".movie-scenes-timeline-apex")[1].TriggerEvent("onmouseenter", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal("No preview", cut.Find(".apex-preview .scene-preview-none").TextContent);
    }

    [Fact]
    public async Task GeneratedMediaForThisMovie_ReloadsTheApexes_AndReportsThemToThePage()
    {
        var changedApexes = new TaskCompletionSource<IReadOnlyList<ApexItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderWithApexes(LocalMovie, a => changedApexes.TrySetResult(a));
        IReadOnlyList<ApexItem> reloaded = [TwoApexes[0], TwoApexes[1] with { PreviewVersion = 5 }];
        apexService.GetApexesAsync(LocalMovie.Id, Arg.Any<CancellationToken>()).Returns(reloaded);
        workerMedia.GenerateAsync(1, Arg.Any<CancellationToken>()).Returns(LocalMovie.Id);
        await mediaQueue.StartAsync(CancellationToken.None);

        mediaQueue.Enqueue(1, LocalMovie.Id, new SceneMediaWindow(0, 750_000));

        Assert.Equal(reloaded, await changedApexes.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await mediaQueue.StopAsync(CancellationToken.None);
    }
}
