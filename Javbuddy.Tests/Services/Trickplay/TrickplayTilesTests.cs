using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayTilesTests
{
    // Jellyfin's default layout: 320x180 thumbs, 10x10 per sheet, every 10 s.
    private static readonly TrickplayLayout Layout = new(320, 180, 10, 10, 250, 10000, 2500, "https://jf/Videos/i/Trickplay/320/{index}.jpg?api_key=k");

    [Fact]
    public void Style_PicksTheTileInItsSheet_ScaledToTheDisplayWidth()
    {
        // 1234 s -> thumbnail 123 -> sheet 1, 23rd on it -> column 3, row 2; scale 64/320 = 0.2.
        Assert.Equal(
            "width:64px;height:36px;background-image:url(\"https://jf/Videos/i/Trickplay/320/1.jpg?api_key=k\");background-size:640px 360px;background-position:-192px -72px",
            TrickplayTiles.Style(Layout, 1234, 64));
    }

    [Fact]
    public void Style_ClampsPastTheLastThumbnail()
    {
        Assert.Contains("/2.jpg", TrickplayTiles.Style(Layout, 99999, 64)); // thumbnail 249 -> sheet 2
        Assert.Contains("background-position:0px 0px", TrickplayTiles.Style(Layout, -5, 64));
    }

    [Fact]
    public void Style_CountsFromTheSetsStart_ForAHighlightsOwnSet()
    {
        // One thumbnail a second from 100 s; 112.4 s -> thumbnail 12 -> column 2, row 1.
        var clip = new TrickplayLayout(320, 180, 10, 10, 30, 1000, 30, "/trickplay/1/clip/{index}.webp", StartSeconds: 100);

        Assert.Contains("background-position:-128px -36px", TrickplayTiles.Style(clip, 112.4, 64));
        Assert.Contains("background-position:0px 0px", TrickplayTiles.Style(clip, 50, 64));
    }
}
