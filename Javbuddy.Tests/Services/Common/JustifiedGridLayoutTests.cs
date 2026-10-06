using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class JustifiedGridLayoutTests
{
    [Fact]
    public void CountRows_EmptyCollection_ReturnsZero()
    {
        Assert.Equal(0, JustifiedGridLayout.CountRows([], 900));
    }

    [Fact]
    public void CountRows_ZeroContainerWidth_ReturnsZero()
    {
        Assert.Equal(0, JustifiedGridLayout.CountRows([1.0, 1.0], 0));
    }

    [Fact]
    public void CountRows_SingleTile_AlwaysOneRow_EvenIfWiderThanContainer()
    {
        // 10x220 = 2200px wide tile, clamped to the 100px container but still alone on its row.
        Assert.Equal(1, JustifiedGridLayout.CountRows([10.0], 100));
    }

    [Fact]
    public void CountRows_WrapsWhenRowWidthExceeded()
    {
        // Three square (ar=1) 220px tiles + 4px gaps in a 450px container:
        // row 1 fits two (220 + 4 + 220 = 444 <= 450), the third wraps to row 2.
        var rows = JustifiedGridLayout.CountRows([1.0, 1.0, 1.0], 450);

        Assert.Equal(2, rows);
    }

    [Fact]
    public void CountRows_ExactFit_DoesNotWrapEarly()
    {
        // Two 220px tiles + one 4px gap exactly fill a 444px container.
        var rows = JustifiedGridLayout.CountRows([1.0, 1.0], 444);

        Assert.Equal(1, rows);
    }

    [Fact]
    public void CalculateHeight_MultipliesRowCountByTileHeightPlusGap()
    {
        var height = JustifiedGridLayout.CalculateHeight([1.0, 1.0, 1.0], 450);

        Assert.Equal(2 * (JustifiedGridLayout.TileHeight + JustifiedGridLayout.TileGap), height);
    }

    [Fact]
    public void CountRows_FourTilesPerRow_At900pxContainer()
    {
        // Matches ActorPhotosTests.SetUpLargeCollection's uniform ar=1.0 photos:
        // 220, 444, 668, 892 (<=900), 1116 (>900) -> 4 tiles per row.
        var aspectRatios = Enumerable.Repeat(1.0, 130);

        var rows = JustifiedGridLayout.CountRows(aspectRatios, 900);

        Assert.Equal(33, rows); // ceil(130 / 4)
    }
}
