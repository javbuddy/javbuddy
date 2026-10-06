using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using SkiaSharp;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrTimelineTests
{
    // Eight thumbnails, a different colour each, 2x2 to a sheet: two sheets.
    private static readonly SKColor[] Colors =
        [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow, SKColors.Magenta, SKColors.Cyan, SKColors.White, SKColors.Orange];

    private static TrickplaySet Set(int count = 8, double duration = 80, int tiles = 2, int width = 32, int height = 18) => new()
    {
        Width = width,
        Height = height,
        TileWidth = tiles,
        TileHeight = tiles,
        ThumbnailCount = count,
        IntervalMs = 10000,
        DurationSeconds = duration,
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 0)]
    [InlineData(32, 1)]
    [InlineData(126, 4)]
    [InlineData(251, 7)]
    public void ThumbnailIndex_SpreadsTheFramesEvenlyOverTheVideo(int frame, int expected) =>
        Assert.Equal(expected, DeoVrTimeline.ThumbnailIndex(Set(), frame));

    [Fact]
    public void ThumbnailIndex_OnALongVideo_SkipsThumbnails_AndStaysInRange()
    {
        var set = Set(count: 707, duration: 7065.387, tiles: 10);

        Assert.Equal(703, DeoVrTimeline.ThumbnailIndex(set, DeoVrTimeline.FrameCount - 1));
        Assert.Equal(0, DeoVrTimeline.ThumbnailIndex(Set(count: 0), 100));
    }

    [Theory]
    [InlineData(707, 8)]
    [InlineData(700, 7)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    public void SheetCount_IsTheSheetsTheThumbnailsFill(int count, int expected) =>
        Assert.Equal(expected, DeoVrTimeline.SheetCount(Set(count: count, tiles: 10)));

    [Fact]
    public void Compose_DrawsDeoVrsGrid_FromTheNearestThumbnails()
    {
        var set = Set();
        var sheets = new[] { Sheet(set, 0), Sheet(set, 4) };

        using var mosaic = SKBitmap.Decode(DeoVrTimeline.Compose(set, sheets));

        Assert.Equal((4096, 4096), (mosaic.Width, mosaic.Height));
        AssertFrame(mosaic, 0, Colors[0]);
        AssertFrame(mosaic, 32, Colors[1]);
        AssertFrame(mosaic, 126, Colors[4]);
        AssertFrame(mosaic, 251, Colors[7]);
    }

    [Fact]
    public void Compose_LeavesFramesOfAMissingOrBrokenSheetBlack()
    {
        var set = Set();

        using var mosaic = SKBitmap.Decode(DeoVrTimeline.Compose(set, [Sheet(set, 0), [1, 2, 3]]));

        AssertFrame(mosaic, 0, Colors[0]);
        AssertFrame(mosaic, 251, SKColors.Black);
    }

    /// <summary>A sheet of the set's geometry, its thumbnails coloured from Colors[first] on.</summary>
    private static byte[] Sheet(TrickplaySet set, int first)
    {
        using var bitmap = new SKBitmap(set.Width * set.TileWidth, set.Height * set.TileHeight);
        using (var canvas = new SKCanvas(bitmap))
        {
            for (var i = 0; i < set.TileWidth * set.TileHeight; i++)
            {
                using var paint = new SKPaint { Color = Colors[first + i] };
                canvas.DrawRect(SKRect.Create(i % set.TileWidth * set.Width, i / set.TileWidth * set.Height, set.Width, set.Height), paint);
            }
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 100);
        return data.ToArray();
    }

    private static void AssertFrame(SKBitmap mosaic, int frame, SKColor expected)
    {
        var x = frame % DeoVrTimeline.Columns * DeoVrTimeline.FrameWidth + DeoVrTimeline.FrameWidth / 2;
        var y = frame / DeoVrTimeline.Columns * DeoVrTimeline.FrameHeight + DeoVrTimeline.FrameHeight / 2;
        var actual = mosaic.GetPixel(x, y);
        // JPEG is lossy: close enough per channel.
        Assert.True(
            Math.Abs(actual.Red - expected.Red) < 24 && Math.Abs(actual.Green - expected.Green) < 24 && Math.Abs(actual.Blue - expected.Blue) < 24,
            $"Frame {frame} is {actual}, expected {expected}");
    }
}
