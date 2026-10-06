using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Movies;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ClipPlayerModalTests : BunitContext
{
    private readonly IJellyfinClient jellyfinClient = Substitute.For<IJellyfinClient>();
    private readonly IMovieStreamService streamService = Substitute.For<IMovieStreamService>();
    private readonly ITrickplayService trickplayService = Substitute.For<ITrickplayService>();
    private readonly TrickplayGenerationTracker tracker = new();
    private readonly IMovieHighlightService highlightService = Substitute.For<IMovieHighlightService>();
    private readonly IMovieApexService apexService = Substitute.For<IMovieApexService>();
    private readonly IHighlightTrickplayService highlightTrickplay = Substitute.For<IHighlightTrickplayService>();
    private readonly IHighlightTrickplayService workerTrickplay = Substitute.For<IHighlightTrickplayService>();
    private readonly SceneMediaQueue mediaQueue;

    private static readonly PlayerClip Highlight = new(PlayerClipKind.Highlight, 4, 7, "ABC-123", "Climax", 90, 100.5, "item-123", "srv", 180);

    public ClipPlayerModalTests()
    {
        Services.AddSingleton(jellyfinClient);
        Services.AddSingleton(streamService);
        Services.AddSingleton(trickplayService);
        Services.AddSingleton(tracker);
        Services.AddSingleton(highlightService);
        Services.AddSingleton(apexService);
        Services.AddSingleton(highlightTrickplay);
        // A real queue whose worker resolves workerTrickplay, so a test can drive a "generated" event.
        var workerServices = new ServiceCollection().AddSingleton(workerTrickplay).BuildServiceProvider();
        mediaQueue = new SceneMediaQueue(workerServices.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        Services.AddSingleton(mediaQueue);
        JSInterop.Mode = JSRuntimeMode.Loose;
        streamService.GetMainFilePathAsync(Arg.Any<int>()).Returns((string?)null);
        streamService.GetMainFilePathAsync(7).Returns("/library/ABC-123/ABC-123.mp4");
        jellyfinClient.GetWebUrlAsync("item-123", "srv", Arg.Any<CancellationToken>()).Returns("http://jf/web/details?id=item-123");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) mediaQueue.Dispose();
    }

    [Fact]
    public void WithoutAClip_RendersNothing()
    {
        var cut = Render<ClipPlayerModal>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Highlight_PlaysJustItsRange_WithoutTheEditor_AndLinksToIt()
    {
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));

        // Starts at the clip; the clip controls, not a media-fragment end, stop it (so Loop can restart it).
        var video = cut.Find("video.video-player-video");
        Assert.Equal("/api/movies/7/stream#t=90", video.GetAttribute("src"));
        Assert.Equal("ABC-123", cut.Find(".video-player-modal-code").TextContent);
        Assert.Equal("Climax", cut.Find(".video-player-modal-name").TextContent);
        Assert.Empty(cut.FindAll(".video-player-side-panel"));
        Assert.Equal("/movies/ABC-123?highlight=4", cut.Find("a.clip-player-edit-btn").GetAttribute("href"));
        Assert.Equal("http://jf/web/details?id=item-123", cut.Find("a.video-player-external-btn").GetAttribute("href"));
    }

    [Fact]
    public void ShortcutsIcon_ListsTheClipControlsKeys_NextAndPreviousOnlyInAPlaylist()
    {
        static List<string> Keys(IRenderedComponent<ClipPlayerModal> cut) =>
            cut.FindAll(".shortcuts-info-keys").Select(k => k.QuerySelector("kbd")!.TextContent).ToList();

        var single = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));
        var playlist = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.Position, 1)
            .Add(x => x.Count, 3)
            .Add(x => x.OnNext, () => { }));

        Assert.Equal(["Space", "←", ",", "F", "Esc"], Keys(single));
        Assert.Equal(["Space", "N", "P", "←", ",", "F", "Esc"], Keys(playlist));
    }

    [Fact]
    public void TheClipControls_ReplaceTheNativeOnesAndTheWholeMovieTimeline()
    {
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));

        Assert.False(cut.Find("video.video-player-video").HasAttribute("controls"));
        var controls = cut.FindComponent<ClipControls>().Instance;
        Assert.Equal((90d, (double?)100.5), (controls.StartSeconds, controls.EndSeconds));
        Assert.Empty(cut.FindComponents<ScrubBar>());
        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal((90d, 100.5), (keys.MinSeconds, keys.MaxSeconds));
    }

    [Fact]
    public void AScene_JumpsBetweenItsHighlightsAndApexes()
    {
        highlightService.GetHighlightsAsync(7, Arg.Any<CancellationToken>()).Returns(
            [new HighlightItem(4, 1, "Before", null, 30, 40, false, 0), new HighlightItem(5, 2, "Inside", null, 120, 130, false, 0)]);
        apexService.GetApexesAsync(7, Arg.Any<CancellationToken>()).Returns(
            [new ApexItem(1, 1, 93, []), new ApexItem(2, 2, 160, []), new ApexItem(3, 3, 190, [])]);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { Kind = PlayerClipKind.Scene, Id = 3, EndSeconds = 180 }));

        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal([120d], keys.HighlightTargets);
        Assert.Equal([90d, 155d], keys.ApexTargets);
        Assert.Contains("Shift+H", cut.FindAll(".shortcuts-info-keys kbd").Select(k => k.TextContent));
    }

    [Fact]
    public async Task ReachingTheClipsEnd_ClosesThePlayer()
    {
        var closed = false;
        var cut = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.OnClose, () => closed = true));

        await cut.InvokeAsync(cut.FindComponent<ClipControls>().Instance.ClipEndedAsync);

        Assert.True(closed);
    }

    [Fact]
    public void SceneWithoutAnEnd_PlaysToTheEndOfTheFile()
    {
        var scene = Highlight with { Kind = PlayerClipKind.Scene, Id = 3, EndSeconds = null };

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, scene));

        Assert.Equal("/api/movies/7/stream#t=90", cut.Find("video.video-player-video").GetAttribute("src"));
        Assert.Null(cut.FindComponent<ClipControls>().Instance.EndSeconds);
        Assert.Equal("/movies/ABC-123?scene=3", cut.Find("a.clip-player-edit-btn").GetAttribute("href"));
    }

    [Fact]
    public void Scene_ShowsTheHighlightsInItsRange_CutToTheClip()
    {
        // The track runs 0:00 → 1:30 for a scene at 1:30 → 3:00.
        static HighlightItem Range(int id, double start, double end) => new(id, id, $"Highlight {id}", null, start, end, false, 0);
        highlightService.GetHighlightsAsync(7, Arg.Any<CancellationToken>()).Returns(
            [Range(1, 10, 20), Range(2, 100, 110), Range(3, 170, 200)]);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { Kind = PlayerClipKind.Scene, Id = 3, EndSeconds = 180 }));

        var segments = cut.FindAll(".clip-controls .highlight-segment");
        Assert.Equal(["Highlight 2", "Highlight 3"], segments.Select(s => s.GetAttribute("data-highlight-name")));
        Assert.Equal(["10", "80"], segments.Select(s => s.GetAttribute("data-start")));
        Assert.Contains("left:11.111%;width:11.111%", segments[0].GetAttribute("style"));
        Assert.Contains("left:88.889%;width:11.111%", segments[1].GetAttribute("style"));
    }

    [Fact]
    public void Highlight_ShowsNoHighlightTrack()
    {
        highlightService.GetHighlightsAsync(7, Arg.Any<CancellationToken>()).Returns(
            [new HighlightItem(4, 1, "Climax", "Climax", 90, 100.5, false, 0)]);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));

        Assert.Empty(cut.FindAll(".highlight-segment"));
        highlightService.DidNotReceiveWithAnyArgs().GetHighlightsAsync(default, default);
    }

    [Fact]
    public void Highlight_PreviewsFromItsOwnTrickplay_OverTheMovies()
    {
        var movieSet = new TrickplayLayout(320, 180, 10, 10, 18, 10000, 180, "/trickplay/7/movie/{index}.webp");
        var clipSet = new TrickplayLayout(320, 180, 10, 10, 11, 1000, 10.5, "/trickplay/7/clip/{index}.webp", 90);
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(movieSet);
        highlightTrickplay.GetAsync(4, Arg.Any<CancellationToken>()).Returns(clipSet);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));

        cut.WaitForAssertion(() => Assert.Equal(clipSet, cut.FindComponent<ClipControls>().Instance.Trickplay));
    }

    [Fact]
    public void Scene_UsesTheMoviesTrickplay()
    {
        var movieSet = new TrickplayLayout(320, 180, 10, 10, 18, 10000, 180, "/trickplay/7/movie/{index}.webp");
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(movieSet);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { Kind = PlayerClipKind.Scene }));

        cut.WaitForAssertion(() => Assert.Equal(movieSet, cut.FindComponent<ClipControls>().Instance.Trickplay));
        highlightTrickplay.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Highlight_SwitchesToItsOwnTrickplay_OnceItIsGenerated()
    {
        var movieSet = new TrickplayLayout(320, 180, 10, 10, 18, 10000, 180, "/trickplay/7/movie/{index}.webp");
        var clipSet = new TrickplayLayout(320, 180, 10, 10, 11, 1000, 10.5, "/trickplay/7/clip/{index}.webp", 90);
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(movieSet);
        highlightTrickplay.GetAsync(4, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));
        cut.WaitForAssertion(() => Assert.Equal(movieSet, cut.FindComponent<ClipControls>().Instance.Trickplay));

        // The set appears (as if the worker wrote it) and the queue reports it.
        highlightTrickplay.GetAsync(4, Arg.Any<CancellationToken>()).Returns(clipSet);
        workerTrickplay.GenerateAsync(4, Arg.Any<CancellationToken>()).Returns(7);
        await mediaQueue.StartAsync(CancellationToken.None);
        mediaQueue.EnqueueHighlightTrickplay(4, 7, new SceneMediaWindow(90_000, 100_500));

        cut.WaitForAssertion(() => Assert.Equal(clipSet, cut.FindComponent<ClipControls>().Instance.Trickplay), TimeSpan.FromSeconds(5));
        await mediaQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void WhileTheHighlightsOwnSetGenerates_ThePreviewShowsItsProgress_OverTheMovies_ThenSwitchesToIt()
    {
        var movieSet = new TrickplayLayout(320, 180, 10, 10, 18, 10000, 180, "/trickplay/7/movie/{index}.webp");
        var clipSet = new TrickplayLayout(320, 180, 10, 10, 11, 1000, 10.5, "/trickplay/7/clip/{index}.webp", 90);
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns(movieSet);
        highlightTrickplay.GetAsync(4, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        var run = tracker.Start(TrickplayTarget.ForHighlight(7, 90, 100.5));
        run.Report(30);

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));
        cut.WaitForAssertion(() => Assert.Equal("Generating previews… 30%", cut.Find(".trickplay-generating-label").TextContent));
        Assert.Null(cut.FindComponent<ClipControls>().Instance.Trickplay);

        highlightTrickplay.GetAsync(4, Arg.Any<CancellationToken>()).Returns(clipSet);
        run.Dispose();

        cut.WaitForAssertion(() => Assert.Equal(clipSet, cut.FindComponent<ClipControls>().Instance.Trickplay));
        Assert.Empty(cut.FindAll(".trickplay-generating"));
    }

    [Fact]
    public void AScene_ShowsTheMoviesGeneration_WhileTheMovieHasNoTrickplay()
    {
        trickplayService.GetAsync(7, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        using var run = tracker.Start(new TrickplayTarget(7));

        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { Kind = PlayerClipKind.Scene }));

        cut.WaitForAssertion(() => Assert.Equal(new TrickplayGenerating(null), cut.FindComponent<ClipControls>().Instance.Generating));
    }

    [Fact]
    public void WithoutAJellyfinItem_StillPlaysTheLocalFile_WithoutTheJellyfinLink()
    {
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { JellyfinItemId = null, JellyfinServerId = null }));

        Assert.Equal("/api/movies/7/stream#t=90", cut.Find("video.video-player-video").GetAttribute("src"));
        Assert.Empty(cut.FindAll("a.video-player-external-btn"));
    }

    [Fact]
    public void WithoutALocalFile_ExplainsWhy_AndStillLinksToTheEditor()
    {
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight with { MovieId = 8 }));

        Assert.Contains("Video file not available locally.", cut.Find(".video-player-error").TextContent);
        Assert.Equal("/movies/ABC-123?highlight=4", cut.Find("a.clip-player-edit-btn").GetAttribute("href"));
    }

    [Fact]
    public void CloseButton_FiresOnClose()
    {
        var closed = false;
        var cut = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find(".video-player-modal-close-btn").Click();

        Assert.True(closed);
    }

    [Fact]
    public async Task InAPlaylist_ShowsThePosition_AndHandsTheEndAndNavigationToThePage()
    {
        var (ended, next, previous, closed) = (0, 0, 0, 0);
        var cut = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.Position, 3)
            .Add(x => x.Count, 42)
            .Add(x => x.OnEnded, () => ended++)
            .Add(x => x.OnNext, () => next++)
            .Add(x => x.OnPrevious, () => previous++)
            .Add(x => x.OnClose, () => closed++));

        Assert.Equal("3 / 42", cut.Find(".clip-player-position").TextContent);
        var controls = cut.FindComponent<ClipControls>().Instance;
        Assert.True(controls.HasPrevious);
        await cut.InvokeAsync(controls.ClipEndedAsync);
        await cut.InvokeAsync(controls.NextAsync);
        await cut.InvokeAsync(controls.PreviousAsync);

        Assert.Equal((1, 1, 1, 0), (ended, next, previous, closed));
    }

    [Fact]
    public void ASingleClip_ShowsNoPositionOrPlaylistControls()
    {
        var cut = Render<ClipPlayerModal>(p => p.Add(x => x.Clip, Highlight));

        Assert.Empty(cut.FindAll(".clip-player-position"));
        Assert.False(cut.FindComponent<ClipControls>().Instance.OnNext.HasDelegate);
    }

    [Fact]
    public void TheFirstClip_HasNoPreviousToGoBackTo()
    {
        var cut = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.Position, 1)
            .Add(x => x.Count, 2)
            .Add(x => x.OnNext, () => { }));

        Assert.False(cut.FindComponent<ClipControls>().Instance.HasPrevious);
    }

    [Fact]
    public void WithOnEdit_TheEditButtonHandsTheClipToThePage_InsteadOfLinking()
    {
        // Movie Detail opens its own editor rather than navigating to itself.
        PlayerClip? edited = null;
        var cut = Render<ClipPlayerModal>(p => p
            .Add(x => x.Clip, Highlight)
            .Add(x => x.OnEdit, clip => edited = clip));

        Assert.Empty(cut.FindAll("a.clip-player-edit-btn"));
        cut.Find("button.clip-player-edit-btn").Click();

        Assert.Equal(Highlight, edited);
    }
}
