using Javbuddy.Models;
using SkiaSharp;

namespace Javbuddy.Services.DeoVr;

/// <summary>DeoVR's timeline (seek-bar) preview, built from a movie's trickplay set:
/// one 4096x4096 JPEG holding a 12x21 grid of 341x195 frames, row by row from the top left, spaced
/// evenly over the whole video — the layout of DeoVR's own <c>timelinePreview</c> mosaics
///. Each frame is the trickplay thumbnail nearest its time, stretched to 341x195 as
/// DeoVR's are (a side-by-side VR set is already cropped to the left eye).</summary>
public static class DeoVrTimeline
{
    public const int Columns = 12;
    public const int Rows = 21;
    public const int FrameCount = Columns * Rows;
    public const int FrameWidth = 341;
    public const int FrameHeight = 195;
    public const int CanvasSize = 4096;
    public const int JpegQuality = 80;

    /// <summary>DeoVR's own name for the mosaic, which the URL ends in, in case the player reads the
    /// frame size from it.</summary>
    public const string FileName = "4096_timelinePreview341x195.jpg";

    /// <summary>The mosaic's image-cache role and variant: one CachedImage row per movie,
    /// a JPEG under the cache directory.</summary>
    public const string CacheRole = "deovr-timeline";
    public const string CacheVariant = "timeline";

    /// <summary>The index of the set's thumbnail shown as the given frame: the one covering
    /// <c>frame × duration / FrameCount</c>, clamped to the thumbnails there are.</summary>
    public static int ThumbnailIndex(TrickplaySet set, int frame)
    {
        var milliseconds = frame * set.DurationSeconds * 1000 / FrameCount;
        return (int)Math.Clamp(Math.Floor(milliseconds / set.IntervalMs), 0, Math.Max(set.ThumbnailCount - 1, 0));
    }

    /// <summary>How many tile sheets the set's thumbnails fill.</summary>
    public static int SheetCount(TrickplaySet set) =>
        (Math.Max(set.ThumbnailCount, 1) + set.TileWidth * set.TileHeight - 1) / (set.TileWidth * set.TileHeight);

    /// <summary>Draws the mosaic from the set's tile sheets (encoded images, in index order) and
    /// returns it as JPEG bytes. A frame whose sheet is missing or doesn't decode stays black.</summary>
    public static byte[] Compose(TrickplaySet set, IReadOnlyList<byte[]> sheets)
    {
        var thumbsPerSheet = set.TileWidth * set.TileHeight;
        using var mosaic = new SKBitmap(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var pressure = (long)CanvasSize * CanvasSize * 4;
        GC.AddMemoryPressure(pressure);
        try
        {
            using (var canvas = new SKCanvas(mosaic))
            {
                canvas.Clear(SKColors.Black);
                var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

                // Frames are in time order, so each sheet is decoded once, and only one is held at a time.
                var sheetIndex = -1;
                SKImage? sheet = null;
                try
                {
                    for (var frame = 0; frame < FrameCount; frame++)
                    {
                        var index = ThumbnailIndex(set, frame);
                        if (index / thumbsPerSheet != sheetIndex)
                        {
                            sheet?.Dispose();
                            sheetIndex = index / thumbsPerSheet;
                            sheet = sheetIndex < sheets.Count ? Decode(sheets[sheetIndex]) : null;
                        }
                        if (sheet is null) continue;

                        var within = index % thumbsPerSheet;
                        var source = SKRect.Create(within % set.TileWidth * set.Width, within / set.TileWidth * set.Height, set.Width, set.Height);
                        var target = SKRect.Create(frame % Columns * FrameWidth, frame / Columns * FrameHeight, FrameWidth, FrameHeight);
                        canvas.DrawImage(sheet, source, target, sampling);
                    }
                }
                finally
                {
                    sheet?.Dispose();
                }
            }

            using var data = mosaic.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data.ToArray();
        }
        finally
        {
            GC.RemoveMemoryPressure(pressure);
        }
    }

    /// <summary>Decodes a sheet up front, so drawing its 100-odd thumbnails doesn't decode it again
    /// each time; null when it doesn't decode.</summary>
    private static SKImage? Decode(byte[] encoded)
    {
        // SKBitmap.Decode throws on bytes no codec recognizes; SKCodec.Create returns null instead.
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) return null;
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }
}
