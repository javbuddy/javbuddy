using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Trickplay;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class VideoPlayerColumnTests : BunitContext
{
    private static readonly TrickplayLayout Trickplay = new(
        Width: 320, Height: 180, TileWidth: 10, TileHeight: 10, ThumbnailCount: 18, IntervalMs: 10000,
        DurationSeconds: 180, TileUrlTemplate: "/trickplay/1/0123456789abcdef/{index}.webp");

    public VideoPlayerColumnTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void WithoutSrc_OnlyTheChildContentIsShown()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Trickplay, Trickplay)
            .AddChildContent("<img class=\"test-poster\" />"));

        Assert.Single(cut.FindAll(".video-player-area .test-poster"));
        Assert.Empty(cut.FindAll("video"));
        Assert.Empty(cut.FindAll(".scrub-bar"));
    }

    [Fact]
    public void WithoutTrickplayOrTimeline_TheNativeSeekBarStays()
    {
        var shown = new List<bool>();
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ScrubBarShownChanged, shown.Add));

        var video = cut.Find("video.test-video");
        Assert.DoesNotContain("video-player-scrub", video.ClassList);
        Assert.False(video.HasAttribute("controlslist"));
        Assert.Empty(cut.FindAll(".scrub-bar"));
        Assert.Equal([false], shown);
    }

    [Fact]
    public void WithTheTimeline_TheScrubBarReplacesTheNativeSeekBar_AndTakesTheTrickplayOnceItLoads()
    {
        var shown = new List<bool>();
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.ScrubBarShownChanged, shown.Add));

        var scrubBar = cut.FindComponent<ScrubBar>();
        Assert.Null(scrubBar.Instance.Trickplay);
        Assert.Equal("video.test-video", scrubBar.Instance.VideoSelector);
        Assert.Contains("video-player-scrub", cut.Find("video.test-video").ClassList);
        Assert.Equal([true], shown);

        cut.Render(p => p.Add(x => x.Trickplay, Trickplay));

        Assert.Same(Trickplay, cut.FindComponent<ScrubBar>().Instance.Trickplay);
        Assert.Equal([true], shown);
    }

    [Fact]
    public void TheHostsControls_ReplaceTheScrubBar()
    {
        RenderFragment controls = b => b.AddMarkupContent(0, "<div class=\"test-controls\"></div>");
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Trickplay, Trickplay)
            .Add(x => x.NativeControls, false)
            .Add(x => x.Controls, controls));

        Assert.Single(cut.FindAll(".video-player-column > .test-controls"));
        Assert.Empty(cut.FindAll(".scrub-bar"));
        Assert.False(cut.Find("video.test-video").HasAttribute("controls"));
    }

    [Fact]
    public void WithTheScrubBar_ThePlayerControlsReplaceTheBrowsers_ExceptInVrMode()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Trickplay, Trickplay));

        Assert.False(cut.Find("video.test-video").HasAttribute("controls"));
        Assert.True(cut.FindComponent<ScrubBar>().Instance.PlayerControls);

        cut.Render(p => p.Add(x => x.VrMode, true));

        Assert.True(cut.Find("video.test-video").HasAttribute("controls"));
        Assert.False(cut.FindComponent<ScrubBar>().Instance.PlayerControls);
    }

    [Fact]
    public void AnotherStream_ForgetsTheVideosOwnLength()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ClipEditor.razor.js");
        module.Setup<double?>("duration", "video.test-video").SetResult(180d);
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.ShowTimeline, true));
        cut.Find("video.test-video").TriggerEvent("ondurationchange", new EventArgs());
        cut.WaitForAssertion(() => Assert.Equal(180d, cut.FindComponent<ScrubBar>().Instance.DurationSeconds));

        // A clip of the same file keeps it; another movie's stream waits for its own.
        cut.Render(p => p.Add(x => x.Src, "/api/movies/1/stream#t=10,20"));
        Assert.Single(cut.FindAll(".scrub-bar"));
        cut.Render(p => p.Add(x => x.Src, "/api/movies/2/stream"));
        Assert.Empty(cut.FindAll(".scrub-bar"));
    }
}
