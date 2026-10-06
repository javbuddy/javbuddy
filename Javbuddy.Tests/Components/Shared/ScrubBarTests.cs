using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Components.Shared;

public class ScrubBarTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/ScrubBar.razor.js";

    private static readonly TrickplayLayout Trickplay = new(
        Width: 320, Height: 180, TileWidth: 10, TileHeight: 10, ThumbnailCount: 18, IntervalMs: 10000,
        DurationSeconds: 180, TileUrlTemplate: "https://jf.example.com/Videos/item-1/Trickplay/320/{index}.jpg?api_key=k");

    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    [Fact]
    public void ScrubBar_RendersTimelineAndHiddenPreview()
    {
        SetUpModule();

        var cut = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.VideoSelector, "video.cleanup-video"));

        Assert.Single(cut.FindAll(".scrub-bar .scrub-bar-track .scrub-bar-progress"));
        Assert.Single(cut.FindAll(".scrub-bar .scrub-bar-preview .scrub-bar-thumb"));
        Assert.Single(cut.FindAll(".scrub-bar .scrub-bar-preview .scrub-bar-time"));
    }

    [Fact]
    public void PlayerControls_AddPlayTimeMuteAndVolume_OnlyWhenAsked()
    {
        SetUpModule();
        var without = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.VideoSelector, "video.test-video"));
        var with = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.PlayerControls, true)
            .Add(x => x.VideoSelector, "video.test-video"));

        Assert.Empty(without.FindAll(".scrub-bar-play, .scrub-bar-mute, .scrub-bar-volume, .scrub-bar-clock"));
        Assert.Single(with.FindAll(".scrub-bar > .scrub-bar-play"));
        Assert.Single(with.FindAll(".scrub-bar > .scrub-bar-clock"));
        Assert.Single(with.FindAll(".scrub-bar > .scrub-bar-mute"));
        Assert.Single(with.FindAll(".scrub-bar > input.scrub-bar-volume[type=range]"));
    }

    [Fact]
    public void WhileGenerating_ThePreviewShowsTheShimmerAndProgress_OnlyWithoutTrickplay()
    {
        SetUpModule();
        var generating = Render<ScrubBar>(p => p
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.Generating, new TrickplayGenerating(7))
            .Add(x => x.VideoSelector, "video.cleanup-video"));
        var withTrickplay = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.Generating, new TrickplayGenerating(7))
            .Add(x => x.VideoSelector, "video.cleanup-video"));

        Assert.Single(generating.FindAll(".scrub-bar-preview .trickplay-generating .scene-preview-shimmer"));
        Assert.Equal("Generating previews… 7%", generating.Find(".trickplay-generating-label").TextContent);
        Assert.Single(generating.FindAll(".scrub-bar-preview .scrub-bar-time"));
        Assert.Empty(withTrickplay.FindAll(".trickplay-generating"));
    }

    [Fact]
    public void ScrubBar_InitialisesModuleWithTrickplayLayoutAndVideoSelector()
    {
        var module = SetUpModule();

        var cut = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.VideoSelector, "video.cleanup-video"));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal(3, init.Arguments.Count);
        Assert.Equal(Trickplay, init.Arguments[1]);
        Assert.Equal("video.cleanup-video", init.Arguments[2]);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scrub-bar")));
    }

    [Fact]
    public void ScrubBar_WithoutTrickplay_HasNoThumbnail_AndInitialisesWithJustTheDuration()
    {
        var module = SetUpModule();

        var cut = Render<ScrubBar>(p => p
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.VideoSelector, "video.video-player-video"));

        Assert.Empty(cut.FindAll(".scrub-bar-thumb"));
        Assert.Single(cut.FindAll(".scrub-bar .scrub-bar-preview .scrub-bar-time"));
        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal(180d, init.Arguments[1]!.GetType().GetProperty("durationSeconds")!.GetValue(init.Arguments[1]));
    }

    [Fact]
    public void ScrubBar_TrickplayArrivingLater_ReinitialisesWithIt_OnlyOnce()
    {
        var module = SetUpModule();
        var cut = Render<ScrubBar>(p => p
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.VideoSelector, "video.video-player-video"));

        cut.Render(p => p.Add(x => x.Trickplay, Trickplay));
        cut.Render(p => p.Add(x => x.Highlights, []));

        var inits = module.Invocations.Where(i => i.Identifier == "init").ToList();
        Assert.Equal(2, inits.Count);
        Assert.Equal(Trickplay, inits[1].Arguments[1]);
        Assert.Single(cut.FindAll(".scrub-bar-thumb"));
    }

    [Fact]
    public async Task ScrubBar_Dispose_DetachesTheModuleListeners()
    {
        var module = SetUpModule();
        var cut = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.VideoSelector, "video.cleanup-video"));

        await cut.Instance.DisposeAsync();

        Assert.Single(module.Invocations, i => i.Identifier == "dispose");
    }
}
