using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class MarkerJumpsTests
{
    private static HighlightItem Highlight(double start) => new(1, 1, "H", null, start, start + 10, false, 0);

    private static ApexItem Apex(double seconds) => new(1, 1, seconds, []);

    [Fact]
    public void Highlights_JumpToTheirStarts_SortedAndDistinct()
    {
        Assert.Equal([10d, 40d, 75.5],
            MarkerJumps.HighlightTargets([Highlight(75.5), Highlight(10), Highlight(40), Highlight(10)], null, null));
    }

    [Fact]
    public void Apexes_JumpFiveSecondsBeforeThem_NotBeforeTheStartOfTheVideo()
    {
        Assert.Equal([0d, 95d, 195d], MarkerJumps.ApexTargets([Apex(200), Apex(3), Apex(100)], null, null));
    }

    [Fact]
    public void Apexes_JumpByTheirOwnLeadIn()
    {
        // An apex's own window moves only where the jump lands, not whether it's in range.
        Assert.Equal([88d, 95d], MarkerJumps.ApexTargets([Apex(100) with { Window = new(12, 5) }, Apex(100)], null, null));
        Assert.Equal([90d], MarkerJumps.ApexTargets([Apex(95) with { Window = new(20, 5) }, Apex(85) with { Window = new(0, 5) }], 90, 150));
    }

    [Fact]
    public void InAClip_OnlyItsMarkersCount_AndAnApexsLeadInStopsAtItsStart()
    {
        // The clip runs [90, 150): a highlight starting at its end, or outside it, isn't in it.
        Assert.Equal([90d, 120d],
            MarkerJumps.HighlightTargets([Highlight(80), Highlight(90), Highlight(120), Highlight(150)], 90, 150));
        Assert.Equal([90d, 125d],
            MarkerJumps.ApexTargets([Apex(85), Apex(92), Apex(130), Apex(150), Apex(170)], 90, 150));
    }

    [Fact]
    public void NoMarkers_NoTargets()
    {
        Assert.Empty(MarkerJumps.HighlightTargets(null, null, null));
        Assert.Empty(MarkerJumps.ApexTargets([], 10, 20));
    }
}
