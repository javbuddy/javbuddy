using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class FrameScorerTests
{
    private const int Size = 32;

    private static byte[] Checkerboard(byte low, byte high)
    {
        var pixels = new byte[Size * Size];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (i / Size + i % Size) % 2 == 0 ? low : high;
        return pixels;
    }

    [Fact]
    public void A_detailed_frame_outscores_a_flat_one()
    {
        var flat = Enumerable.Repeat((byte)128, Size * Size).ToArray();

        Assert.True(FrameScorer.Score(Checkerboard(100, 156), Size, Size) > FrameScorer.Score(flat, Size, Size));
    }

    [Fact]
    public void A_dark_frame_is_penalised_even_when_it_has_detail()
    {
        Assert.True(FrameScorer.Score(Checkerboard(60, 116), Size, Size) > FrameScorer.Score(Checkerboard(0, 56), Size, Size));
    }

    [Fact]
    public void A_buffer_too_small_for_the_stated_size_scores_zero()
    {
        Assert.Equal(0, FrameScorer.Score(new byte[4], Size, Size));
    }

    [Fact]
    public void The_bitmap_overload_scores_a_detailed_frame_above_a_flat_one()
    {
        using var flat = new SkiaSharp.SKBitmap(640, 360);
        flat.Erase(new SkiaSharp.SKColor(128, 128, 128));
        using var detailed = new SkiaSharp.SKBitmap(640, 360);
        for (var y = 0; y < detailed.Height; y++)
        {
            for (var x = 0; x < detailed.Width; x++)
            {
                var v = (byte)(((x / 4) + (y / 4)) % 2 == 0 ? 90 : 170);
                detailed.SetPixel(x, y, new SkiaSharp.SKColor(v, v, v));
            }
        }

        Assert.True(FrameScorer.Score(detailed) > FrameScorer.Score(flat));
    }
}
