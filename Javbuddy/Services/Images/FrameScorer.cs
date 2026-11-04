using SkiaSharp;

namespace Javbuddy.Services.Images;

/// <summary>Rates a video frame as a poster/fanart candidate: sharp frames score high, and near-black or blown-out
/// frames (fades, title cards) are heavily penalised.</summary>
public static class FrameScorer
{
    private const int AnalysisWidth = 320;
    private const double DarkLuma = 40;
    private const double BrightLuma = 215;
    private const double ExposurePenalty = 0.1;

    public static double Score(SKBitmap bitmap)
    {
        var height = Math.Max(3, (int)Math.Round((double)bitmap.Height * AnalysisWidth / bitmap.Width));
        using var small = bitmap.Resize(new SKImageInfo(AnalysisWidth, height, SKColorType.Gray8), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (small is null) return 0;

        return Score(small.Bytes, small.Width, small.Height);
    }

    /// <summary>Variance of the Laplacian over 8-bit grayscale pixels (the usual sharpness measure), scaled down
    /// when the mean brightness is too low or too high.</summary>
    public static double Score(ReadOnlySpan<byte> gray, int width, int height)
    {
        if (width < 3 || height < 3 || gray.Length < width * height) return 0;

        double sum = 0, sumSquares = 0, lumaSum = 0;
        var count = 0;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                double laplacian = 4 * gray[i] - gray[i - 1] - gray[i + 1] - gray[i - width] - gray[i + width];
                sum += laplacian;
                sumSquares += laplacian * laplacian;
                lumaSum += gray[i];
                count++;
            }
        }

        var variance = sumSquares / count - Math.Pow(sum / count, 2);
        var meanLuma = lumaSum / count;
        return meanLuma is < DarkLuma or > BrightLuma ? variance * ExposurePenalty : variance;
    }
}
