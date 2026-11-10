using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
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

    private static readonly IReadOnlyList<SubtitleTrack> TwoTracks =
        [new("/api/movies/1/subtitles/0", "English", "en"), new("/api/movies/1/subtitles/1", "Japanese", "ja")];

    [Fact]
    public void Subtitles_RenderAsTracks_WithOnlyTheFirstDefault_AndACcButton()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Subtitles, TwoTracks));

        var tracks = cut.FindAll("video track");
        Assert.Equal(["/api/movies/1/subtitles/0", "/api/movies/1/subtitles/1"], tracks.Select(t => t.GetAttribute("src")));
        Assert.True(tracks[0].HasAttribute("default"));
        Assert.False(tracks[1].HasAttribute("default"));
        Assert.Contains("video-player-subtitles-on", cut.Find(".video-player-subtitles-button").ClassList);
    }

    [Fact]
    public void NoSubtitles_OrVrMode_ShowsNoCcButton()
    {
        var none = Render<VideoPlayerColumn>(p => p.Add(x => x.VideoClass, "v").Add(x => x.Src, "/s"));
        var vr = Render<VideoPlayerColumn>(p => p.Add(x => x.VideoClass, "v").Add(x => x.Src, "/s")
            .Add(x => x.Subtitles, TwoTracks).Add(x => x.VrMode, true));

        Assert.Empty(none.FindAll(".video-player-subtitles-button"));
        Assert.Empty(vr.FindAll(".video-player-subtitles-button"));
    }

    [Fact]
    public void WithTheScrubBarControls_TheCcButtonSitsInTheBarBeforeTheMute()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.Subtitles, TwoTracks));

        Assert.Single(cut.FindAll(".scrub-bar .video-player-subtitles-in-bar .video-player-subtitles-button"));
        Assert.Empty(cut.FindAll(".video-player-subtitles-overlay"));
        Assert.Contains("video-player-subtitles-in-bar", cut.Find(".scrub-bar-volume-group").PreviousElementSibling!.ClassList);
    }

    [Fact]
    public void CcMenu_ListsOffAndTheTracks_AndPicksOne()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Subtitles, TwoTracks));

        Assert.Empty(cut.FindAll(".video-player-subtitles-menu"));
        cut.Find(".video-player-subtitles-button").Click();
        var items = cut.FindAll(".video-player-subtitles-item");
        Assert.Equal(["Off", "English", "Japanese", "Small", "Medium", "Large", "Extra large", "Transparent", "Semi-transparent", "Solid"],
            items.Select(i => i.TextContent.Replace("✓", "").Trim()));
        Assert.Equal("true", items[1].GetAttribute("aria-checked"));

        items[2].Click();

        Assert.Empty(cut.FindAll(".video-player-subtitles-menu"));
        Assert.Contains("Japanese", cut.Find(".video-player-subtitles-button").GetAttribute("title"));
        JSInterop.VerifyInvoke("setSubtitleTrack", 2); // once for the new tracks, once for the pick
        JSInterop.VerifyInvoke("saveSubtitlePrefs");
    }

    [Fact]
    public void CcMenu_Off_TurnsSubtitlesOff_AndTheNextClipStaysOff()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Subtitles, TwoTracks));
        cut.Find(".video-player-subtitles-button").Click();
        cut.FindAll(".video-player-subtitles-item")[0].Click();
        Assert.DoesNotContain("video-player-subtitles-on", cut.Find(".video-player-subtitles-button").ClassList);

        cut.Render(p => p.Add(x => x.Subtitles, [new SubtitleTrack("/api/movies/2/subtitles/0", "French", "fr")]));

        Assert.DoesNotContain("video-player-subtitles-on", cut.Find(".video-player-subtitles-button").ClassList);
        Assert.False(cut.Find("video track").HasAttribute("default"));
    }

    [Fact]
    public void CcMenu_SizeAndBackground_AreAppliedAndSaved()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Subtitles, TwoTracks));
        cut.Find(".video-player-subtitles-button").Click();

        cut.FindAll(".video-player-subtitles-item").First(i => i.TextContent.Contains("Large")).Click();
        cut.FindAll(".video-player-subtitles-item").First(i => i.TextContent.Contains("Solid")).Click();

        var style = JSInterop.Invocations.Where(i => i.Identifier == "applySubtitleStyle").Last();
        Assert.Equal(["video.test-video", "large", "solid"], style.Arguments.Select(a => a?.ToString()));
        Assert.Equal(2, JSInterop.Invocations.Count(i => i.Identifier == "saveSubtitlePrefs"));
    }

    [Fact]
    public void ChangingTheTracks_ResetsToTheFirst_AndAlwaysSetsTheTrack()
    {
        var cut = Render<VideoPlayerColumn>(p => p
            .Add(x => x.VideoClass, "test-video")
            .Add(x => x.Src, "/api/movies/1/stream")
            .Add(x => x.Subtitles, TwoTracks));
        cut.Find(".video-player-subtitles-button").Click();
        cut.FindAll(".video-player-subtitles-item")[2].Click();

        cut.Render(p => p.Add(x => x.Subtitles, [new SubtitleTrack("/api/movies/1/subtitles/0?fileId=2", "French", "fr")]));
        Assert.Contains("video-player-subtitles-on", cut.Find(".video-player-subtitles-button").ClassList);

        var before = JSInterop.Invocations.Count(i => i.Identifier == "setSubtitleTrack");
        cut.Render(p => p.Add(x => x.Subtitles, []));
        Assert.True(JSInterop.Invocations.Count(i => i.Identifier == "setSubtitleTrack") > before);
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
