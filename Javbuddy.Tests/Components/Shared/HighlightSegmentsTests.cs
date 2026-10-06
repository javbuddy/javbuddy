using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class HighlightSegmentsTests : BunitContext
{
    private static readonly IReadOnlyList<HighlightItem> Highlights =
    [
        new(1, 1, "Climax", "Climax", 0, 30, false, 0),
        new(2, 2, "Highlight 2", null, 18, 90, false, 1),
    ];

    [Fact]
    public void Renders_OneSpanPerHighlight_ByPercentAndLane()
    {
        var cut = Render<HighlightSegments>(p => p
            .Add(x => x.Highlights, Highlights)
            .Add(x => x.DurationSeconds, 180d));

        // Two lanes of 6px, minus the trailing gap.
        Assert.Equal("height:11px", cut.Find(".highlight-segments").GetAttribute("style"));
        var segments = cut.FindAll(".highlight-segment");
        Assert.Equal("left:0%;width:16.667%;top:0px", segments[0].GetAttribute("style"));
        Assert.Equal("left:10%;width:40%;top:6px", segments[1].GetAttribute("style"));
        Assert.Equal(("0", "Climax"), (segments[0].GetAttribute("data-start"), segments[0].GetAttribute("data-highlight-name")));
        Assert.Equal("Highlight 2 (0:18–1:30)", segments[1].GetAttribute("title"));
        Assert.Equal("SPAN", segments[0].TagName);
    }

    [Fact]
    public void WithClickHandler_SegmentsAreButtons()
    {
        HighlightItem? clicked = null;
        var cut = Render<HighlightSegments>(p => p
            .Add(x => x.Highlights, Highlights)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.OnHighlightClick, h => clicked = h));

        cut.FindAll("button.highlight-segment")[1].Click();

        Assert.Equal(2, clicked!.Id);
    }

    [Fact]
    public void RendersNothing_WithoutDurationOrHighlights()
    {
        Assert.Empty(Render<HighlightSegments>(p => p.Add(x => x.Highlights, Highlights)).FindAll(".highlight-segments"));
        Assert.Empty(Render<HighlightSegments>(p => p.Add(x => x.Highlights, []).Add(x => x.DurationSeconds, 180d)).FindAll(".highlight-segments"));
    }
}
