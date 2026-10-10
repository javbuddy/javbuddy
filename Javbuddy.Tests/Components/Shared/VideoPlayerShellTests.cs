using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class VideoPlayerShellTests : BunitContext
{
    private readonly IMovieStreamService streamService = Substitute.For<IMovieStreamService>();
    private readonly ITrickplayService trickplayService = Substitute.For<ITrickplayService>();
    private readonly TrickplayGenerationTracker tracker = new();

    public VideoPlayerShellTests()
    {
        Services.AddSingleton(streamService);
        Services.AddSingleton(trickplayService);
        Services.AddSingleton(tracker);
        JSInterop.Mode = JSRuntimeMode.Loose;
        // Unconfigured, NSubstitute would answer "" rather than null: only movie 1 has a file.
        streamService.GetMainFilePathAsync(Arg.Any<int>()).Returns((string?)null);
        streamService.GetMainFilePathAsync(1).Returns("/library/ABC-123/ABC-123.mp4");
    }

    [Fact]
    public void StreamsTheMoviesLocalFile_WithoutJellyfin()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1));

        Assert.Equal("/api/movies/1/stream", cut.Find("video.video-player-video").GetAttribute("src"));
    }

    [Fact]
    public void WithoutALocalFile_SaysSo()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 99));

        Assert.Empty(cut.FindAll("video"));
        Assert.Equal("Video file not available locally.", cut.Find(".video-player-error-text").TextContent);
    }

    [Fact]
    public void TheMoviesTrickplay_DrawsTheScrubBar()
    {
        trickplayService.GetAsync(1).Returns(new TrickplayLayout(320, 180, 10, 10, 100, 10000, 1000, "http://jf/{index}.jpg"));

        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scrub-bar")));
    }

    [Fact]
    public void WithoutSlots_HasNoSidePanelOrExtraActions()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1));

        Assert.Empty(cut.FindAll(".video-player-side-panel"));
        Assert.Empty(cut.FindAll(".test-action"));
    }

    [Fact]
    public void Slots_RenderInTheHeaderActionsAndSidePanel()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.HeaderActions, (RenderFragment)(b => b.AddMarkupContent(0, "<a class=\"test-action\">Edit</a>")))
            .Add(x => x.SidePanel, (RenderFragment<TrickplayLayout?>)(_ => b => b.AddMarkupContent(0, "<div class=\"test-panel\">Panel</div>"))));

        Assert.Single(cut.FindAll(".video-player-modal-actions .test-action"));
        Assert.Single(cut.FindAll(".video-player-side-panel .test-panel"));
    }

    [Fact]
    public void Code_LinksToTheMoviesDetailPage()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1));

        Assert.Equal("/movies/ABC-123", cut.Find("a.video-player-modal-code").GetAttribute("href"));
    }

    [Fact]
    public void Code_PlainClick_ClosesThePlayer()
    {
        var closed = false;
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("a.video-player-modal-code").Click();

        Assert.True(closed);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 1)]
    public void Code_NewTabClick_KeepsThePlayerOpen(bool ctrlKey, bool metaKey, long button)
    {
        var closed = false;
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("a.video-player-modal-code").Click(new MouseEventArgs { CtrlKey = ctrlKey, MetaKey = metaKey, Button = button });

        Assert.False(closed);
    }

    private static List<string> ListedKeys(IRenderedComponent<VideoPlayerShell> cut) =>
        cut.FindAll(".video-player-modal-actions .shortcuts-info-keys").Select(k => k.QuerySelector("kbd")!.TextContent).ToList();

    [Fact]
    public void ShortcutsIcon_ListsTheHostsShortcutsFirst_ThenSeekFrameStepAndEsc()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.Shortcuts, [KeyboardShortcut.NewScene]));

        // No trickplay, VR or host controls, so the browser's own controls: no F.
        Assert.Equal(["M", "←", ",", "Esc"], ListedKeys(cut));

        cut.Find(".video-player-vr-toggle").Click();

        Assert.Equal(["M", "←", ",", "F", "Esc"], ListedKeys(cut));
    }

    [Theory]
    [InlineData("VR180 SBS", "equirect")]
    [InlineData("Fisheye SBS", "fisheye")]
    public void AVideoTheViewerSupports_OpensInTheVr2dViewer_WithItsProjection(string vrType, string projection)
    {
        streamService.GetVrTypeAsync(1, Arg.Any<int?>()).Returns(vrType);

        var cut = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1));

        Assert.Equal("true", cut.Find(".video-player-vr-toggle").GetAttribute("aria-pressed"));
        Assert.Equal(projection, cut.Find(".vr-viewer-projection option[selected]").GetAttribute("value"));
    }

    [Theory]
    [InlineData("VR180 TB")]
    [InlineData("VR360 SBS")]
    [InlineData(null)]
    public void AnyOtherVideo_PlaysFlat(string? vrType)
    {
        streamService.GetVrTypeAsync(1, Arg.Any<int?>()).Returns(vrType);

        var cut = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1));

        Assert.Equal("false", cut.Find(".video-player-vr-toggle").GetAttribute("aria-pressed"));
        Assert.Empty(cut.FindAll(".vr-viewer"));
    }

    [Fact]
    public void TheVr2dToggle_TurnsAnAutoOpenedViewerOff()
    {
        streamService.GetVrTypeAsync(1, Arg.Any<int?>()).Returns("VR180 SBS");
        var cut = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1));

        cut.Find(".video-player-vr-toggle").Click();

        Assert.Empty(cut.FindAll(".vr-viewer"));
    }

    [Fact]
    public void HighlightsAndApexes_AreJumpedBetween_AndTheirKeysListed()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)])
            .Add(x => x.Apexes, [new ApexItem(2, 1, 300, [])]));

        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal([100d], keys.HighlightTargets);
        Assert.Equal([295d], keys.ApexTargets);
        Assert.Equal(["←", ",", "H", "A", "Esc"], ListedKeys(cut));

        cut.Render(p => p.Add(x => x.Highlights, []));

        Assert.Empty(keys.HighlightTargets);
        Assert.Equal(["←", ",", "A", "Esc"], ListedKeys(cut));
    }

    [Fact]
    public void WithMarkerJumpKeysOff_NothingIsJumpedToOrListed()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.MarkerJumpKeys, false)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)])
            .Add(x => x.Apexes, [new ApexItem(2, 1, 300, [])]));

        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Empty(keys.HighlightTargets);
        Assert.Empty(keys.ApexTargets);
        Assert.Equal(["←", ",", "Esc"], ListedKeys(cut));
    }

    [Fact]
    public void JumpMarkers_OverrideTheScrubBarsOnes_WithinTheSeekRange()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.SeekMinSeconds, 100d)
            .Add(x => x.SeekMaxSeconds, 200d)
            .Add(x => x.JumpHighlights, [new HighlightItem(4, 1, "A", null, 50, 60, false, 0), new HighlightItem(5, 2, "B", null, 150, 160, false, 0)])
            .Add(x => x.JumpApexes, [new ApexItem(2, 1, 102, []), new ApexItem(3, 2, 250, [])]));

        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal([150d], keys.HighlightTargets);
        Assert.Equal([100d], keys.ApexTargets);
    }

    [Fact]
    public void ShortcutsIcon_IsLeftOutWithoutAStream()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 99));

        Assert.Empty(cut.FindAll(".shortcuts-info"));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void WithoutTrickplay_TheTimelineShowsTheMarkers_OnlyWhenAskedFor(bool showTimeline, int segments)
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, showTimeline)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)]));

        Assert.Equal(segments, cut.FindAll(".video-player-column .highlight-segment").Count);
    }

    [Fact]
    public void WithoutTrickplay_TheTimelineIsTheScrubBarWithoutThumbnails_InPlaceOfTheNativeSeekBar()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true));

        var scrubBar = cut.FindComponent<ScrubBar>().Instance;
        Assert.Null(scrubBar.Trickplay);
        Assert.Equal(180d, scrubBar.DurationSeconds);
        Assert.Empty(cut.FindAll(".scrub-bar-thumb"));
        var video = cut.Find("video.video-player-video");
        Assert.Contains("video-player-scrub", video.ClassList);
        Assert.Equal("nofullscreen", video.GetAttribute("controlslist"));
        Assert.Contains("F", ListedKeys(cut));
    }

    [Fact]
    public void WithoutTrickplayOrAStoredDuration_TheScrubBarUsesTheVideosOwnLength()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ClipEditor.razor.js");
        module.Setup<double?>("duration", "video.video-player-video").SetResult(180d);
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)]));
        // Until the browser knows the length, the native seek bar stays.
        Assert.Empty(cut.FindAll(".scrub-bar"));

        cut.Find("video.video-player-video").TriggerEvent("ondurationchange", new EventArgs());

        cut.WaitForAssertion(() => Assert.Equal(180d, cut.FindComponent<ScrubBar>().Instance.DurationSeconds));
        Assert.Contains("video-player-scrub", cut.Find("video.video-player-video").ClassList);
        Assert.Single(cut.FindAll(".scrub-bar .highlight-segment"));
    }

    [Fact]
    public void AStoredDuration_IsntReplacedByTheVideosOwn()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ClipEditor.razor.js");
        module.Setup<double?>("duration", "video.video-player-video").SetResult(999d);
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true));

        cut.Find("video.video-player-video").TriggerEvent("ondurationchange", new EventArgs());

        Assert.Equal(180d, cut.FindComponent<ScrubBar>().Instance.DurationSeconds);
    }

    [Fact]
    public void TrickplayArrivingLater_SwapsInTheScrubBarWithThumbnails()
    {
        var layout = new TrickplayLayout(320, 180, 10, 10, 100, 10000, 1000, "http://jf/{index}.jpg");
        var pending = new TaskCompletionSource<TrickplayLayout?>();
        trickplayService.GetAsync(1).Returns(pending.Task);
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true));
        Assert.Null(cut.FindComponent<ScrubBar>().Instance.Trickplay);

        pending.SetResult(layout);

        cut.WaitForAssertion(() => Assert.Equal(layout, cut.FindComponent<ScrubBar>().Instance.Trickplay));
        Assert.Single(cut.FindAll(".scrub-bar-thumb"));
    }

    [Fact]
    public void WhileTheMoviesTrickplayGenerates_TheScrubBarShowsItsProgress_ThenItsThumbnails()
    {
        var layout = new TrickplayLayout(320, 180, 10, 10, 100, 10000, 1000, "/trickplay/1/abc/{index}.webp");
        trickplayService.GetAsync(1).Returns((TrickplayLayout?)null);
        var run = tracker.Start(new TrickplayTarget(1));
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true));
        cut.WaitForAssertion(() => Assert.Equal("Generating previews…", cut.Find(".trickplay-generating-label").TextContent));

        run.Report(42.5);
        cut.WaitForAssertion(() => Assert.Equal("Generating previews… 42%", cut.Find(".trickplay-generating-label").TextContent));

        trickplayService.GetAsync(1).Returns(layout);
        run.Dispose();
        cut.WaitForAssertion(() => Assert.Equal(layout, cut.FindComponent<ScrubBar>().Instance.Trickplay));
        Assert.Empty(cut.FindAll(".trickplay-generating"));
    }

    [Fact]
    public void AnotherMoviesOrAHighlightsGeneration_IsntShown()
    {
        using var other = tracker.Start(new TrickplayTarget(2));
        using var highlight = tracker.Start(TrickplayTarget.ForHighlight(1, 10, 40));

        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true));

        Assert.Null(cut.FindComponent<ScrubBar>().Instance.Generating);
    }

    [Fact]
    public void ByDefault_TheVideoHasNativeControls_AndSeeksAnywhere()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1));

        Assert.True(cut.Find("video.video-player-video").HasAttribute("controls"));
        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal((null, null), (keys.MinSeconds, keys.MaxSeconds));
    }

    [Fact]
    public void CustomControls_ReplaceTheNativeOnesAndTheTimeline_AndTheSeekRangeIsPassedOn()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)])
            .Add(x => x.NativeControls, false)
            .Add(x => x.Controls, (RenderFragment<VideoPlayerShell.ControlsContext>)(_ => b => b.AddMarkupContent(0, "<div class=\"test-controls\"></div>")))
            .Add(x => x.SeekMinSeconds, 100d)
            .Add(x => x.SeekMaxSeconds, 120d));

        Assert.False(cut.Find("video.video-player-video").HasAttribute("controls"));
        Assert.Single(cut.FindAll(".video-player-column .test-controls"));
        Assert.Empty(cut.FindAll(".highlight-segment"));
        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Equal((100d, 120d), (keys.MinSeconds, keys.MaxSeconds));
    }

    [Fact]
    public void ANewStartInTheSameVideo_RestartsThereWithoutReloadingTheStream()
    {
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.StartAtSeconds, 90d)
            .Add(x => x.EndAtSeconds, 100d));

        cut.Render(p => p.Add(x => x.StartAtSeconds, 200d).Add(x => x.EndAtSeconds, 210d));

        Assert.Equal("/api/movies/1/stream#t=200,210", cut.Find("video.video-player-video").GetAttribute("src"));
        streamService.Received(1).GetMainFilePathAsync(1);
    }

    [Fact]
    public void AnotherVideo_KeepsThePlayerOnScreen_UntilItsStreamIsReady()
    {
        var pending = new TaskCompletionSource<string?>();
        streamService.GetMainFilePathAsync(2).Returns(pending.Task);
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.StartAtSeconds, 90d));

        cut.Render(p => p.Add(x => x.MovieId, 2).Add(x => x.StartAtSeconds, 30d));

        // No loading state in between: the column (fullscreen, when it is) stays.
        Assert.Empty(cut.FindAll(".video-player-loading"));
        Assert.Single(cut.FindAll(".video-player-column video.video-player-video"));

        pending.SetResult("/library/DEF-456/DEF-456.mp4");
        cut.WaitForAssertion(() => Assert.Equal("/api/movies/2/stream#t=30", cut.Find("video.video-player-video").GetAttribute("src")));
    }

    [Fact]
    public void TheControls_AreToldWhenTheVideoIsntTheCurrentItemsYet()
    {
        var pending = new TaskCompletionSource<string?>();
        streamService.GetMainFilePathAsync(2).Returns(pending.Task);
        var seen = new List<bool>();
        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.NativeControls, false)
            .Add(x => x.Controls, (RenderFragment<VideoPlayerShell.ControlsContext>)(context => b => seen.Add(context.StreamReady))));
        Assert.True(seen[^1]);

        cut.Render(p => p.Add(x => x.MovieId, 2));
        Assert.False(seen[^1]);

        pending.SetResult("/library/DEF-456/DEF-456.mp4");
        cut.WaitForAssertion(() => Assert.True(seen[^1]));
    }

    private static readonly IReadOnlyList<MovieVersionOption> TwoVersions =
    [
        new(10, "Original · 1080p", true),
        new(11, "RIFE-3.1 · 1080p · 60 fps", false),
    ];

    [Fact]
    public void AGivenVersion_StreamsThatFile()
    {
        streamService.GetFilePathAsync(1, 11).Returns("/library/ABC-123/ABC-123-RIFE-3.1.mkv");

        var cut = Render<VideoPlayerShell>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.FileId, 11));

        Assert.Equal("/api/movies/1/stream?fileId=11", cut.Find("video.video-player-video").GetAttribute("src"));
        streamService.DidNotReceive().GetMainFilePathAsync(1);
    }

    [Fact]
    public void TheVersionPicker_ShowsOnlyWhenAskedFor_AndThereIsMoreThanOneVersion()
    {
        streamService.GetVersionsAsync(1).Returns(TwoVersions);

        var without = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1));
        Assert.Empty(without.FindAll(".video-player-version-select"));

        var with = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1).Add(x => x.ShowVersions, true));
        var options = with.FindAll(".video-player-version-select option");
        Assert.Equal(["Original · 1080p (Primary)", "RIFE-3.1 · 1080p · 60 fps"], options.Select(o => o.TextContent));
        Assert.Equal("10", with.Find(".video-player-version-select").GetAttribute("value"));

        streamService.GetVersionsAsync(1).Returns([TwoVersions[0]]);
        var single = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1).Add(x => x.ShowVersions, true));
        Assert.Empty(single.FindAll(".video-player-version-select"));
    }

    [Fact]
    public void SwitchingVersion_CarriesOnFromTheCurrentPosition()
    {
        streamService.GetVersionsAsync(1).Returns(TwoVersions);
        streamService.GetFilePathAsync(1, 11).Returns("/library/ABC-123/ABC-123-RIFE-3.1.mkv");
        var module = JSInterop.SetupModule("./Components/Shared/ClipEditor.razor.js");
        module.Setup<double?>("currentTime", "video.video-player-video").SetResult(754.25);
        var cut = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1).Add(x => x.ShowVersions, true));

        cut.Find(".video-player-version-select").Change("11");

        cut.WaitForAssertion(() => Assert.Equal("/api/movies/1/stream?fileId=11#t=754.25", cut.Find("video.video-player-video").GetAttribute("src")));
        Assert.Equal("11", cut.Find(".video-player-version-select").GetAttribute("value"));

        // A later re-render from the host doesn't jump back to where the player was opened.
        cut.Render(p => p.Add(x => x.Title, "Retitled"));
        Assert.Equal("/api/movies/1/stream?fileId=11#t=754.25", cut.Find("video.video-player-video").GetAttribute("src"));
    }

    [Fact]
    public void SwitchingToAVersionThatsGone_SaysSo()
    {
        streamService.GetVersionsAsync(1).Returns(TwoVersions);
        streamService.GetFilePathAsync(1, 11).Returns((string?)null);
        var cut = Render<VideoPlayerShell>(p => p.Add(x => x.Show, true).Add(x => x.MovieId, 1).Add(x => x.ShowVersions, true));

        cut.Find(".video-player-version-select").Change("11");

        cut.WaitForAssertion(() => Assert.Equal("That version isn't available locally.", cut.Find(".video-player-error-text").TextContent));
    }
}
