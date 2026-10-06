using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class ApexMarkersTests : BunitContext
{
    [Fact]
    public void Renders_OneMarkerPerApex_ByPercent_WithTagsAsLabel()
    {
        IReadOnlyList<ApexItem> apexes =
        [
            new(1, 1, 45, [new(1, "Squirt", null), new(2, "Cowgirl", null)]),
            new(2, 2, 90, []),
        ];

        var cut = Render<ApexMarkers>(p => p.Add(x => x.Apexes, apexes).Add(x => x.DurationSeconds, 180d));

        var markers = cut.FindAll(".apex-marker");
        Assert.Equal("left:25%", markers[0].GetAttribute("style"));
        Assert.Equal("45", markers[0].GetAttribute("data-start"));
        Assert.Equal("Squirt, Cowgirl", markers[0].GetAttribute("data-highlight-name"));
        Assert.Equal("Squirt, Cowgirl (0:45)", markers[0].GetAttribute("title"));
        Assert.Equal("Apex (1:30)", markers[1].GetAttribute("title"));
    }

    [Fact]
    public void RendersNothing_WithoutApexesOrDuration()
    {
        Assert.Empty(Render<ApexMarkers>(p => p.Add(x => x.DurationSeconds, 180d)).FindAll(".apex-marker"));
        Assert.Empty(Render<ApexMarkers>(p => p.Add(x => x.Apexes, [new ApexItem(1, 1, 5, [])])).FindAll(".apex-marker"));
    }

    [Fact]
    public void FavoriteApexes_GetTheFavoriteMarker()
    {
        IReadOnlyList<ApexItem> apexes = [new(1, 1, 45, []) { IsFavorite = true }, new(2, 2, 90, [])];

        var cut = Render<ApexMarkers>(p => p.Add(x => x.Apexes, apexes).Add(x => x.DurationSeconds, 180d));

        Assert.Equal([true, false], cut.FindAll(".apex-marker").Select(m => m.ClassList.Contains("apex-marker-fav")));
    }
}
