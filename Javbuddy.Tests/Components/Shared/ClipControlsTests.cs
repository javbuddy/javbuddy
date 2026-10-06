using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Components.Shared;

public class ClipControlsTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/ClipControls.razor.js";
    private static readonly TrickplayLayout Trickplay = new(320, 180, 5, 5, 100, 10000, 1000, "http://jf/tile_{index}.jpg");

    private readonly BunitJSModuleInterop module;

    public ClipControlsTests()
    {
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<ClipControls> RenderControls(TrickplayLayout? trickplay = null, double? end = 100.5, Action? onEnded = null) =>
        Render<ClipControls>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.StartSeconds, 90d)
            .Add(x => x.EndSeconds, end)
            .Add(x => x.Trickplay, trickplay)
            .Add(x => x.OnEnded, onEnded ?? (() => { })));

    [Fact]
    public void RendersTheClipControls_AndHandsTheRangeToTheModule()
    {
        var cut = RenderControls(Trickplay);

        foreach (var selector in new[] { ".clip-controls-play", ".clip-controls-track", ".clip-controls-time", ".clip-controls-mute", ".clip-controls-volume", ".clip-controls-loop", ".clip-controls-fullscreen" })
        {
            Assert.Single(cut.FindAll(selector));
        }
        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        var options = Assert.IsType<ClipControls.ClipRange>(init.Arguments[2]);
        Assert.Equal(new ClipControls.ClipRange("video.video-player-video", 90, 100.5, Trickplay, Playlist: false, HasPrevious: false), options);
        Assert.Empty(cut.FindAll(".clip-controls-prev, .clip-controls-next"));
    }

    [Fact]
    public void Highlights_AreCutToTheClip_WhichRunsToTheMoviesEndWithoutAnEnd()
    {
        // An open-ended clip at 1:30 of a 3:00 movie is 1:30 long.
        var cut = Render<ClipControls>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.StartSeconds, 90d)
            .Add(x => x.EndSeconds, (double?)null)
            .Add(x => x.MovieDurationSeconds, 180d)
            .Add(x => x.Highlights, [new Javbuddy.Services.Scenes.HighlightItem(1, 1, "Kiss", null, 80, 135, false, 2)]));

        var segment = cut.Find(".clip-controls-track-wrap .highlight-segment");
        Assert.Equal("0", segment.GetAttribute("data-start"));
        Assert.Equal("left:0%;width:50%;top:0px", segment.GetAttribute("style"));
    }

    [Fact]
    public void WithoutHighlights_ThereIsNoHighlightTrack() =>
        Assert.Empty(RenderControls().FindAll(".highlight-segments"));

    [Fact]
    public async Task InAPlaylist_PreviousAndNext_AreOfferedAndPassedOn()
    {
        var previous = 0;
        var next = 0;
        var cut = Render<ClipControls>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.StartSeconds, 90d)
            .Add(x => x.EndSeconds, 100.5)
            .Add(x => x.HasPrevious, true)
            .Add(x => x.OnPrevious, () => previous++)
            .Add(x => x.OnNext, () => next++));

        Assert.Single(cut.FindAll(".clip-controls-prev"));
        Assert.Single(cut.FindAll(".clip-controls-next"));
        var options = Assert.IsType<ClipControls.ClipRange>(Assert.Single(module.Invocations, i => i.Identifier == "init").Arguments[2]);
        Assert.True(options.Playlist);
        Assert.True(options.HasPrevious);

        await cut.InvokeAsync(cut.Instance.PreviousAsync);
        await cut.InvokeAsync(cut.Instance.NextAsync);

        Assert.Equal((1, 1), (previous, next));
    }

    [Fact]
    public void AnotherClipWithTheSameRange_StillReinitialisesTheModule()
    {
        // e.g. two single-scene movies in a row: both scenes run 0 → the end of their file.
        var cut = Render<ClipControls>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.ClipKey, "Scene:1")
            .Add(x => x.EndSeconds, (double?)null)
            .Add(x => x.HasPrevious, true)
            .Add(x => x.OnNext, () => { }));

        cut.Render(p => p.Add(x => x.ClipKey, "Scene:2"));

        Assert.Equal(2, module.Invocations.Count(i => i.Identifier == "init"));
    }

    [Fact]
    public void AnotherClip_WaitsForItsVideo_BeforeReinitialising()
    {
        var cut = RenderControls();

        // Another movie's clip: the player still shows the previous video until its stream is ready.
        cut.Render(p => p.Add(x => x.StartSeconds, 30d).Add(x => x.StreamReady, false));
        Assert.Single(module.Invocations, i => i.Identifier == "init");

        cut.Render(p => p.Add(x => x.StreamReady, true));
        var options = Assert.IsType<ClipControls.ClipRange>(module.Invocations.Where(i => i.Identifier == "init").Last().Arguments[2]);
        Assert.Equal(30d, options.StartSeconds);
    }

    [Fact]
    public void AnotherClip_ReinitialisesTheModuleWithItsRange()
    {
        var cut = RenderControls();

        cut.Render(p => p.Add(x => x.StartSeconds, 200d).Add(x => x.EndSeconds, 230d));

        var inits = module.Invocations.Where(i => i.Identifier == "init").ToList();
        Assert.Equal(2, inits.Count);
        var options = Assert.IsType<ClipControls.ClipRange>(inits[1].Arguments[2]);
        Assert.Equal((200d, (double?)230d), (options.StartSeconds, options.EndSeconds));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void TheRememberedLoopChoice_IsShown(bool remembered, string pressed)
    {
        module.Setup<bool>("init", _ => true).SetResult(remembered);

        var cut = RenderControls();

        Assert.Equal(pressed, cut.Find(".clip-controls-loop").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Loop_Toggles_AndIsPassedOnToBeRemembered()
    {
        var cut = RenderControls();

        cut.Find(".clip-controls-loop").Click();

        Assert.Equal("true", cut.Find(".clip-controls-loop").GetAttribute("aria-pressed"));
        Assert.Equal(true, Assert.Single(module.Invocations, i => i.Identifier == "setLoop").Arguments[1]);
    }

    [Fact]
    public async Task ReachingTheEnd_WithoutLoop_FiresOnEnded()
    {
        var ended = false;
        var cut = RenderControls(onEnded: () => ended = true);

        await cut.InvokeAsync(cut.Instance.ClipEndedAsync);

        Assert.True(ended);
    }

    [Fact]
    public void TrickplayArrivingLater_IsHandedToTheModule()
    {
        var cut = RenderControls();

        cut.Render(p => p.Add(x => x.Trickplay, Trickplay));

        Assert.Equal(Trickplay, Assert.Single(module.Invocations, i => i.Identifier == "setTrickplay").Arguments[1]);
    }
}
