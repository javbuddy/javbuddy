using SkiaSharp;

namespace Javbuddy.Services.Images;

/// <summary>Decodes a source image and re-encodes it as WebP, optionally downscaling first —
/// the one place that touches SkiaSharp, shared by the background scan (ImageCacheTask) and
/// the on-demand cache-miss path (ILocalImageCacheService). Optimized for low memory footprint
/// via SKCodec scaled decoding, native GC memory pressure tracking, and single-pass multi-variant generation.</summary>
public static class ImageConverter
{
    public const int ThumbMaxEdge = 400;
    public const int FullMaxEdge = 2560; // Max edge for full WebP cache (avoids storing 8K bitmaps while keeping retina/4K quality)
    public const int WebPQualityFull = 82; // Visually lossless / high quality
    public const int WebPQualityThumb = 85; // Compact thumbnail quality

    /// <summary>Converts sourcePath to WebP bytes. If maxEdge is given and the source's longer
    /// edge exceeds it, downscales (preserving aspect ratio) — never upscales. Pass maxEdge: null
    /// for full variant (capped at FullMaxEdge to keep cache size reasonable).</summary>
    public static byte[] ConvertToWebP(string sourcePath, int? maxEdge)
    {
        var targetMaxEdge = maxEdge ?? FullMaxEdge;
        using var bitmap = DecodeScaled(sourcePath, targetMaxEdge);

        var pressure = (long)bitmap.Width * bitmap.Height * 4;
        GC.AddMemoryPressure(pressure);
        try
        {
            return EncodeWebP(bitmap, maxEdge: null, QualityFor(maxEdge));
        }
        finally
        {
            GC.RemoveMemoryPressure(pressure);
        }
    }

    /// <summary>Converts source image to both Thumb and Full WebP bytes in a single decode pass,
    /// avoiding redundant disk/network reads and 8K uncompressed bitmap decodes.</summary>
    public static (byte[] ThumbBytes, byte[] FullBytes) ConvertToBothWebP(
        string sourcePath,
        int thumbMaxEdge = ThumbMaxEdge,
        int fullMaxEdge = FullMaxEdge,
        int qualityFull = WebPQualityFull,
        int qualityThumb = WebPQualityThumb)
    {
        using var bitmap = DecodeScaled(sourcePath, fullMaxEdge);
        return EncodeBothWebP(bitmap, thumbMaxEdge, fullMaxEdge, qualityFull, qualityThumb);
    }

    /// <summary>Converts a local poster image to both Thumb and Full WebP bytes in a single decode/crop pass.</summary>
    public static (byte[] ThumbBytes, byte[] FullBytes) ConvertPosterToBothWebP(
        string sourcePath,
        int thumbMaxEdge = ThumbMaxEdge,
        int fullMaxEdge = FullMaxEdge,
        int qualityFull = WebPQualityFull,
        int qualityThumb = WebPQualityThumb)
    {
        using var original = DecodeFileForCrop(sourcePath, fullMaxEdge, LocalPosterCropRect);
        return CropAndEncodeBothWebP(original, LocalPosterCropRect(original.Width, original.Height), thumbMaxEdge, fullMaxEdge, qualityFull, qualityThumb);
    }

    /// <summary>Crops a downloaded cover image into a poster (see PosterCropGeometry), then
    /// generates both Thumb and Full WebP bytes in a single pass.</summary>
    public static (byte[] ThumbBytes, byte[] FullBytes) CropCoverToBothWebP(
        byte[] sourceBytes,
        int thumbMaxEdge = ThumbMaxEdge,
        int fullMaxEdge = FullMaxEdge,
        int qualityFull = WebPQualityFull,
        int qualityThumb = WebPQualityThumb)
    {
        using var original = DecodeBytesForCrop(sourceBytes, fullMaxEdge, CoverCropRect);
        return CropAndEncodeBothWebP(original, CoverCropRect(original.Width, original.Height), thumbMaxEdge, fullMaxEdge, qualityFull, qualityThumb);
    }

    /// <summary>Converts an in-memory image to both Thumb and Full WebP bytes in a single decode pass.</summary>
    public static (byte[] ThumbBytes, byte[] FullBytes) ConvertBytesToBothWebP(
        byte[] sourceBytes,
        int thumbMaxEdge = ThumbMaxEdge,
        int fullMaxEdge = FullMaxEdge,
        int qualityFull = WebPQualityFull,
        int qualityThumb = WebPQualityThumb)
    {
        using var bitmap = DecodeScaledFromBytes(sourceBytes, fullMaxEdge);
        return EncodeBothWebP(bitmap, thumbMaxEdge, fullMaxEdge, qualityFull, qualityThumb);
    }

    /// <summary>Crops an in-memory image using normalized coordinates (0.0 to 1.0) and generates
    /// both Thumb and Full WebP bytes in a single pass.</summary>
    public static (byte[] ThumbBytes, byte[] FullBytes) CropAndConvertToBothWebP(
        byte[] sourceBytes,
        NormalizedCropRect cropRect,
        int thumbMaxEdge = ThumbMaxEdge,
        int fullMaxEdge = FullMaxEdge,
        int qualityFull = WebPQualityFull,
        int qualityThumb = WebPQualityThumb)
    {
        var clamped = cropRect.Clamp();
        using var original = DecodeBytesForCrop(sourceBytes, fullMaxEdge, (w, h) => NormalizedCropPixelRect(clamped, w, h));
        return CropAndEncodeBothWebP(original, NormalizedCropPixelRect(clamped, original.Width, original.Height), thumbMaxEdge, fullMaxEdge, qualityFull, qualityThumb);
    }

    /// <summary>Crops an in-memory image using normalized coordinates (0.0 to 1.0) and encodes
    /// the cropped result as JPEG bytes (default quality 92) for saving as a local poster.jpg.</summary>
    public static byte[] CropImageToJpeg(
        byte[] sourceBytes,
        NormalizedCropRect cropRect,
        int quality = 92)
    {
        var clamped = cropRect.Clamp();
        using var original = DecodeScaledFromBytes(sourceBytes, FullMaxEdge * 2);

        var pressure = (long)original.Width * original.Height * 4;
        GC.AddMemoryPressure(pressure);
        try
        {
            using var cropped = Crop(original, NormalizedCropPixelRect(clamped, original.Width, original.Height));
            using var data = cropped.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data.ToArray();
        }
        finally
        {
            GC.RemoveMemoryPressure(pressure);
        }
    }

    /// <summary>Checks if a physical poster image on disk is landscape (aspect ratio > 1.05),
    /// and if so, crops it to the front cover and overwrites the file as JPEG. Returns true if cropped.</summary>
    public static bool EnsureLocalPosterCropped(string posterFilePath)
    {
        if (!File.Exists(posterFilePath)) return false;
        try
        {
            using var original = SKBitmap.Decode(posterFilePath);
            if (original is null) return false;

            var aspectRatio = (double)original.Width / original.Height;
            if (aspectRatio <= 1.05) return false;

            var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(original.Width, original.Height);
            var rect = new SKRectI(left, top, right, bottom);

            using var cropped = new SKBitmap(rect.Width, rect.Height);
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.DrawBitmap(original, rect, new SKRect(0, 0, rect.Width, rect.Height), SKSamplingOptions.Default);
            }

            using var data = cropped.Encode(SKEncodedImageFormat.Jpeg, 92);

            var tmpFile = posterFilePath + ".tmp";
            File.WriteAllBytes(tmpFile, data.ToArray());
            File.Move(tmpFile, posterFilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int QualityFor(int? maxEdge) =>
        maxEdge.HasValue && maxEdge.Value <= ThumbMaxEdge ? WebPQualityThumb : WebPQualityFull;

    // A local poster.jpg is only cropped when it's landscape (an uncropped full DVD jacket);
    // anything else is used whole.
    private static SKRectI LocalPosterCropRect(int width, int height) =>
        (double)width / height > 1.05 ? CoverCropRect(width, height) : new SKRectI(0, 0, width, height);

    private static SKRectI CoverCropRect(int width, int height)
    {
        var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(width, height);
        return new SKRectI(left, top, right, bottom);
    }

    private static SKRectI NormalizedCropPixelRect(NormalizedCropRect clamped, int width, int height)
    {
        var left = (int)Math.Clamp(Math.Round(clamped.X * width), 0, width - 1);
        var top = (int)Math.Clamp(Math.Round(clamped.Y * height), 0, height - 1);
        var cropWidth = (int)Math.Clamp(Math.Round(clamped.Width * width), 1, width - left);
        var cropHeight = (int)Math.Clamp(Math.Round(clamped.Height * height), 1, height - top);
        return new SKRectI(left, top, left + cropWidth, top + cropHeight);
    }

    private static (byte[] ThumbBytes, byte[] FullBytes) CropAndEncodeBothWebP(
        SKBitmap original, SKRectI rect, int thumbMaxEdge, int fullMaxEdge, int qualityFull, int qualityThumb)
    {
        var pressure = (long)original.Width * original.Height * 4;
        GC.AddMemoryPressure(pressure);
        try
        {
            using var cropped = Crop(original, rect);
            var fullBytes = EncodeWebP(cropped, fullMaxEdge, qualityFull);
            var thumbBytes = EncodeWebP(cropped, thumbMaxEdge, qualityThumb);
            return (thumbBytes, fullBytes);
        }
        finally
        {
            GC.RemoveMemoryPressure(pressure);
        }
    }

    private static (byte[] ThumbBytes, byte[] FullBytes) EncodeBothWebP(
        SKBitmap bitmap, int thumbMaxEdge, int fullMaxEdge, int qualityFull, int qualityThumb)
    {
        var pressure = (long)bitmap.Width * bitmap.Height * 4;
        GC.AddMemoryPressure(pressure);
        try
        {
            // Full variant straight from the decoded bitmap, thumb downscaled from it in memory.
            var fullBytes = EncodeWebP(bitmap, fullMaxEdge, qualityFull);
            var thumbBytes = EncodeWebP(bitmap, thumbMaxEdge, qualityThumb);
            return (thumbBytes, fullBytes);
        }
        finally
        {
            GC.RemoveMemoryPressure(pressure);
        }
    }

    /// <summary>Encodes source as WebP, downscaled first only when it exceeds maxEdge — a source
    /// that already fits is encoded as-is rather than copied into a second full bitmap. Like every
    /// encode in this class, it encodes straight from the bitmap's pixels — byte-identical output
    /// to SKImage.FromBitmap(..).Encode, minus the full pixel copy FromBitmap makes of a mutable
    /// bitmap first.</summary>
    private static byte[] EncodeWebP(SKBitmap source, int? maxEdge, int quality)
    {
        var resized = Downscale(source, maxEdge);
        try
        {
            using var data = (resized ?? source).Encode(SKEncodedImageFormat.Webp, quality);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    /// <summary>A view of rect within source that shares its pixels (no copy) — source must
    /// outlive the returned bitmap. The same pixels a 1:1 DrawBitmap copy would produce.</summary>
    private static SKBitmap Crop(SKBitmap source, SKRectI rect)
    {
        var subset = new SKBitmap();
        if (source.ExtractSubset(subset, rect)) return subset;
        subset.Dispose();

        var cropped = new SKBitmap(rect.Width, rect.Height);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(source, rect, new SKRect(0, 0, rect.Width, rect.Height), SKSamplingOptions.Default);
        return cropped;
    }

    /// <summary>Uses SKCodec to decode an image at a scale close to maxEdge if possible, avoiding
    /// allocating 100MB+ uncompressed 8K bitmaps in memory just to downscale them.</summary>
    private static SKBitmap DecodeScaled(string sourcePath, int? maxEdge) =>
        DecodeFile(sourcePath, codec => DecodeWithCodec(codec, maxEdge), maxEdge);

    private static SKBitmap DecodeScaledFromBytes(byte[] sourceBytes, int? maxEdge) =>
        DecodeBytes(sourceBytes, codec => DecodeWithCodec(codec, maxEdge), maxEdge);

    /// <summary>Decodes only as large as needed for cropRect's region of the decoded image to
    /// still reach maxEdge on its longer edge — rather than a fixed 2×maxEdge for
    /// the whole image — so the crop is then downscaled to exactly the same size as before.</summary>
    private static SKBitmap DecodeFileForCrop(string sourcePath, int maxEdge, Func<int, int, SKRectI> cropRect) =>
        DecodeFile(sourcePath, codec => DecodeWithCodecForCrop(codec, maxEdge, cropRect), fallbackMaxEdge: null);

    private static SKBitmap DecodeBytesForCrop(byte[] sourceBytes, int maxEdge, Func<int, int, SKRectI> cropRect) =>
        DecodeBytes(sourceBytes, codec => DecodeWithCodecForCrop(codec, maxEdge, cropRect), fallbackMaxEdge: null);

    private static SKBitmap DecodeFile(string sourcePath, Func<SKCodec, SKBitmap?> decode, int? fallbackMaxEdge)
    {
        try
        {
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: false);
            using var codec = SKCodec.Create(stream);
            if (codec is not null)
            {
                var bitmap = decode(codec);
                if (bitmap is not null) return bitmap;
            }
        }
        catch
        {
            // Fall back to regular decode on any codec exception
        }

        var full = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidOperationException($"Could not decode image: {sourcePath}");
        return DownscaleOwned(full, fallbackMaxEdge);
    }

    private static SKBitmap DecodeBytes(byte[] sourceBytes, Func<SKCodec, SKBitmap?> decode, int? fallbackMaxEdge)
    {
        try
        {
            using var stream = new MemoryStream(sourceBytes);
            using var codec = SKCodec.Create(stream);
            if (codec is not null)
            {
                var bitmap = decode(codec);
                if (bitmap is not null) return bitmap;
            }
        }
        catch
        {
            // Fall back
        }

        var full = SKBitmap.Decode(sourceBytes)
            ?? throw new InvalidOperationException("Could not decode image bytes.");
        return DownscaleOwned(full, fallbackMaxEdge);
    }

    private static SKBitmap? DecodeWithCodec(SKCodec codec, int? maxEdge)
    {
        var origInfo = codec.Info;
        if (maxEdge is not int max || max <= 0 || Math.Max(origInfo.Width, origInfo.Height) <= max)
        {
            return DecodeAt(codec, origInfo);
        }

        var longerEdge = Math.Max(origInfo.Width, origInfo.Height);
        var scale = (float)max / longerEdge;
        var scaledDimensions = codec.GetScaledDimensions(scale);
        var decodedBitmap = DecodeAt(codec, WithSize(origInfo, scaledDimensions));
        return decodedBitmap is null ? null : DownscaleOwned(decodedBitmap, max);
    }

    private static SKBitmap? DecodeWithCodecForCrop(SKCodec codec, int maxEdge, Func<int, int, SKRectI> cropRect)
    {
        var origInfo = codec.Info;
        var cropLongerEdge = LongerEdge(cropRect(origInfo.Width, origInfo.Height));
        if (cropLongerEdge <= maxEdge)
        {
            return DecodeAt(codec, origInfo);
        }

        // Codecs only decode at the scales they support (JPEG: n/8) and round to the nearest one,
        // which can land below the requested scale — step up until the crop still covers maxEdge.
        var scale = (float)maxEdge / cropLongerEdge;
        var dimensions = codec.GetScaledDimensions(scale);
        while (scale < 1f && LongerEdge(cropRect(dimensions.Width, dimensions.Height)) < maxEdge)
        {
            scale = Math.Min(1f, scale + (1f / 32));
            dimensions = codec.GetScaledDimensions(scale);
        }

        return DecodeAt(codec, WithSize(origInfo, dimensions));
    }

    private static SKBitmap? DecodeAt(SKCodec codec, SKImageInfo info)
    {
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput) return bitmap;
        bitmap.Dispose();
        return null;
    }

    private static SKImageInfo WithSize(SKImageInfo info, SKSizeI size) =>
        new(size.Width, size.Height, info.ColorType, info.AlphaType, info.ColorSpace);

    private static int LongerEdge(SKRectI rect) => Math.Max(rect.Width, rect.Height);

    /// <summary>Downscales source (preserving aspect ratio) so its longer edge is maxEdge, or
    /// returns null when it already fits — never upscales.</summary>
    private static SKBitmap? Downscale(SKBitmap source, int? maxEdge)
    {
        if (maxEdge is not int max || max <= 0) return null;

        var longerEdge = Math.Max(source.Width, source.Height);
        if (longerEdge <= max) return null;

        var scale = (double)max / longerEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        return source.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
    }

    /// <summary>Like Downscale, but takes ownership of source: returns it unchanged when it
    /// already fits, or disposes it and returns the downscaled copy.</summary>
    private static SKBitmap DownscaleOwned(SKBitmap source, int? maxEdge)
    {
        var resized = Downscale(source, maxEdge);
        if (resized is null) return source;
        source.Dispose();
        return resized;
    }
}
