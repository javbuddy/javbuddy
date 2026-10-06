using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Components.Shared;

public class SceneSegmentsTests : BunitContext
{
    private static readonly IReadOnlyList<SceneItem> Scenes =
    [
        new(1, 1, "Interview", "Interview", 0, 30, 30),
        new(2, 2, "Scene 2", null, 90, null, 200),
    ];

    [Fact]
    public void Renders_OneSegmentPerScene_PositionedByPercentOfDuration()
    {
        var cut = Render<SceneSegments>(p => p
            .Add(x => x.Scenes, Scenes)
            .Add(x => x.DurationSeconds, 180d));

        var segments = cut.FindAll(".scene-segment");
        Assert.Equal(2, segments.Count);
        Assert.Equal("left:0%;width:16.667%", segments[0].GetAttribute("style"));
        // Effective end past the duration is clipped to it.
        Assert.Equal("left:50%;width:50%", segments[1].GetAttribute("style"));
        Assert.Equal("Interview (0:00)", segments[0].GetAttribute("title"));
        Assert.Equal("SPAN", segments[0].TagName);
    }

    [Fact]
    public void Segments_CarryTheirRangeAndName_ForTheScrubBarHoverLabel()
    {
        var cut = Render<SceneSegments>(p => p
            .Add(x => x.Scenes, [new SceneItem(1, 1, "Interview", "Interview", 12.5, 30, 30), .. Scenes.Skip(1)])
            .Add(x => x.DurationSeconds, 180d));

        var segments = cut.FindAll(".scene-segment");
        Assert.Equal(("12.5", "30", "Interview"), (segments[0].GetAttribute("data-start"), segments[0].GetAttribute("data-end"), segments[0].GetAttribute("data-scene-name")));
        // An untitled scene's display name; its effective end is clipped to the duration.
        Assert.Equal(("90", "180", "Scene 2"), (segments[1].GetAttribute("data-start"), segments[1].GetAttribute("data-end"), segments[1].GetAttribute("data-scene-name")));
    }

    [Fact]
    public void RendersNothing_WithoutDurationOrScenes()
    {
        Assert.Empty(Render<SceneSegments>(p => p.Add(x => x.Scenes, Scenes)).FindAll(".scene-segments"));
        Assert.Empty(Render<SceneSegments>(p => p.Add(x => x.Scenes, []).Add(x => x.DurationSeconds, 180d)).FindAll(".scene-segments"));
    }

    [Fact]
    public void WithClickHandler_SegmentsAreButtonsThatReportTheScene()
    {
        SceneItem? clicked = null;
        var cut = Render<SceneSegments>(p => p
            .Add(x => x.Scenes, Scenes)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.OnSegmentClick, (SceneItem s) => clicked = s));

        cut.FindAll("button.scene-segment")[1].Click();

        Assert.Equal(2, clicked!.Id);
    }

    [Fact]
    public void ScrubBar_DrawsScenesOnItsTrack()
    {
        JSInterop.SetupModule("./Components/Shared/ScrubBar.razor.js").Mode = JSRuntimeMode.Loose;
        var trickplay = new TrickplayLayout(320, 180, 10, 10, 18, 10000, 180, "https://jf/{index}.jpg");

        var cut = Render<ScrubBar>(p => p
            .Add(x => x.Trickplay, trickplay)
            .Add(x => x.VideoSelector, "video.cleanup-video")
            .Add(x => x.Scenes, Scenes));

        Assert.Equal(2, cut.FindAll(".scrub-bar-track .scene-segment").Count);
    }

    [Fact]
    public void Suggestions_AreGhostTicks_EvenWithoutScenes()
    {
        var cut = Render<SceneSegments>(p => p
            .Add(x => x.Scenes, [])
            .Add(x => x.Suggestions, [45d, 90d])
            .Add(x => x.DurationSeconds, 180d));

        var ticks = cut.FindAll(".scene-suggestion-tick");
        Assert.Equal(["left:25%", "left:50%"], ticks.Select(t => t.GetAttribute("style")));
        Assert.Equal("Suggested boundary (0:45)", ticks[0].GetAttribute("title"));
        Assert.Empty(cut.FindAll(".scene-segment"));
    }
}
