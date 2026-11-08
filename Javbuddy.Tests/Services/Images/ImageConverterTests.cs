using Javbuddy.Services.Images;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public class ImageConverterTests
{
    private static byte[] CreateSampleImageBytes(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ConvertBytesToBothWebP_GeneratesThumbAndFullBytes()
    {
        var sourceBytes = CreateSampleImageBytes(800, 600, SKColors.CornflowerBlue);
        var (thumbBytes, fullBytes) = ImageConverter.ConvertBytesToBothWebP(sourceBytes);

        Assert.NotNull(thumbBytes);
        Assert.NotEmpty(thumbBytes);
        Assert.NotNull(fullBytes);
        Assert.NotEmpty(fullBytes);

        using var thumbBitmap = SKBitmap.Decode(thumbBytes);
        Assert.NotNull(thumbBitmap);
        Assert.True(Math.Max(thumbBitmap.Width, thumbBitmap.Height) <= ImageConverter.ThumbMaxEdge);

        using var fullBitmap = SKBitmap.Decode(fullBytes);
        Assert.NotNull(fullBitmap);
        Assert.True(Math.Max(fullBitmap.Width, fullBitmap.Height) <= ImageConverter.FullMaxEdge);
    }

    [Fact]
    public void CropAndConvertToBothWebP_CropsToNormalizedCoordinates()
    {
        // 1000 x 500 image, crop center square 500 x 500
        var sourceBytes = CreateSampleImageBytes(1000, 500, SKColors.Crimson);
        var cropRect = new NormalizedCropRect(0.25, 0.0, 0.5, 1.0);

        var (thumbBytes, fullBytes) = ImageConverter.CropAndConvertToBothWebP(sourceBytes, cropRect);

        Assert.NotNull(thumbBytes);
        Assert.NotEmpty(thumbBytes);
        Assert.NotNull(fullBytes);
        Assert.NotEmpty(fullBytes);

        using var fullBitmap = SKBitmap.Decode(fullBytes);
        Assert.NotNull(fullBitmap);
        // Aspect ratio of cropped square should be 1:1
        Assert.Equal(fullBitmap.Width, fullBitmap.Height);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RotateQuarterTurnToPng_SwapsDimensionsAndMovesPixelsToTheRightCorner(bool clockwise)
    {
        // 4 x 2 image: top-left red, everything else blue.
        using var source = new SKBitmap(4, 2);
        source.Erase(SKColors.Blue);
        source.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(source);
        var sourceBytes = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        var rotatedBytes = ImageConverter.RotateQuarterTurnToPng(sourceBytes, clockwise);

        using var rotated = SKBitmap.Decode(rotatedBytes);
        Assert.Equal(2, rotated.Width);
        Assert.Equal(4, rotated.Height);
        // Clockwise sends the top-left pixel to the top-right; counter-clockwise to the bottom-left.
        var (rx, ry) = clockwise ? (1, 0) : (0, 3);
        Assert.Equal(SKColors.Red, rotated.GetPixel(rx, ry));
        Assert.Equal(SKColors.Blue, rotated.GetPixel(0, 1));
    }

    [Fact]
    public void RotateByDegreesToPng_AtNinetyDegrees_MatchesAClockwiseQuarterTurn()
    {
        using var source = new SKBitmap(4, 2);
        source.Erase(SKColors.Blue);
        source.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(source);
        var sourceBytes = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        using var rotated = SKBitmap.Decode(ImageConverter.RotateByDegreesToPng(sourceBytes, 90));

        Assert.Equal((2, 4), (rotated.Width, rotated.Height));
        // Top-left goes to top-right, like the clockwise quarter turn.
        Assert.Equal(SKColors.Red, rotated.GetPixel(1, 0));
        Assert.Equal(SKColors.Blue, rotated.GetPixel(0, 0));
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(-30.0)]
    public void RotateByDegreesToPng_AtAnArbitraryAngle_GrowsTheFrameAndLeavesTransparentCorners(double degrees)
    {
        var sourceBytes = CreateSampleImageBytes(200, 100, SKColors.SeaGreen);

        using var rotated = SKBitmap.Decode(ImageConverter.RotateByDegreesToPng(sourceBytes, degrees));

        // 200 x 100 turned by 30°: 200·cos30 + 100·sin30 = 223, 200·sin30 + 100·cos30 = 187.
        Assert.Equal((223, 187), (rotated.Width, rotated.Height));
        Assert.Equal(0, rotated.GetPixel(0, 0).Alpha);
        Assert.Equal(0, rotated.GetPixel(rotated.Width - 1, rotated.Height - 1).Alpha);
        var center = rotated.GetPixel(rotated.Width / 2, rotated.Height / 2);
        Assert.Equal(255, center.Alpha);
        Assert.Equal(SKColors.SeaGreen.Green, center.Green);
    }

    [Fact]
    public void RotateByDegreesToPng_RotatesClockwiseForPositiveAngles()
    {
        // Red left half, blue right half; a small clockwise turn lifts the right side down:
        // the top-right of the frame stays uncovered further than the bottom-right does.
        using var source = new SKBitmap(200, 100);
        source.Erase(SKColors.Blue);
        for (var x = 0; x < 100; x++) for (var y = 0; y < 100; y++) source.SetPixel(x, y, SKColors.Red);
        using var image = SKImage.FromBitmap(source);
        var sourceBytes = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        using var rotated = SKBitmap.Decode(ImageConverter.RotateByDegreesToPng(sourceBytes, 20));

        // Clockwise: the image's top-right corner moves down and right, its top-left moves up and right,
        // so the frame's top edge is touched near the left (x small) and its bottom edge near the right.
        var topLeftHalf = Enumerable.Range(0, rotated.Width / 2).Count(x => rotated.GetPixel(x, 0).Alpha > 0);
        var topRightHalf = Enumerable.Range(rotated.Width / 2, rotated.Width - rotated.Width / 2).Count(x => rotated.GetPixel(x, 0).Alpha > 0);
        Assert.True(topLeftHalf > topRightHalf);
    }

    /// <summary>A 40 x 20 JPEG whose top-left 20 x 10 block is red and the rest blue, tagged with the
    /// given EXIF orientation (an APP1 segment spliced in right after the SOI marker).</summary>
    private static byte[] CreateOrientedJpeg(ushort orientation)
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.Blue);
        for (var x = 0; x < 20; x++) for (var y = 0; y < 10; y++) bitmap.SetPixel(x, y, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();

        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static bool IsRedish(SKColor c) => c.Red > 180 && c.Blue < 90;

    [Fact]
    public void ApplyExifOrientation_WithoutAnOrientation_ReturnsTheSameBytes()
    {
        var plain = CreateSampleImageBytes(40, 20, SKColors.Blue);
        Assert.Same(plain, ImageConverter.ApplyExifOrientation(plain));

        var topLeft = CreateOrientedJpeg(1);
        Assert.Same(topLeft, ImageConverter.ApplyExifOrientation(topLeft));
    }

    [Theory]
    [InlineData(2, 40, 20, 30, 5)]   // mirrored left-right: the red block moves to the top-right
    [InlineData(3, 40, 20, 30, 15)]  // 180°: bottom-right
    [InlineData(4, 40, 20, 10, 15)]  // mirrored top-bottom: bottom-left
    [InlineData(5, 20, 40, 5, 10)]   // transposed: still top-left, but 20 x 40
    [InlineData(6, 20, 40, 15, 10)]  // rotated clockwise: top-right of a 20 x 40 image
    [InlineData(7, 20, 40, 15, 30)]  // transverse: bottom-right of a 20 x 40 image
    [InlineData(8, 20, 40, 5, 30)]   // rotated counter-clockwise: bottom-left of a 20 x 40 image
    public void ApplyExifOrientation_BakesTheOrientationIntoThePixels(ushort orientation, int width, int height, int redX, int redY)
    {
        using (var codec = SKCodec.Create(new MemoryStream(CreateOrientedJpeg(orientation))))
        {
            Assert.Equal((SKEncodedOrigin)orientation, codec.EncodedOrigin);
        }

        var upright = ImageConverter.ApplyExifOrientation(CreateOrientedJpeg(orientation));

        using var bitmap = SKBitmap.Decode(upright);
        Assert.Equal((width, height), (bitmap.Width, bitmap.Height));
        Assert.True(IsRedish(bitmap.GetPixel(redX, redY)), $"expected red at ({redX},{redY}) but was {bitmap.GetPixel(redX, redY)}");
        // The opposite corner of the image is blue.
        Assert.False(IsRedish(bitmap.GetPixel(width - 1 - redX, height - 1 - redY)));
        using var codecAfter = SKCodec.Create(new MemoryStream(upright));
        Assert.Equal(SKEncodedOrigin.TopLeft, codecAfter.EncodedOrigin);
    }

    [Fact]
    public void CropImageToJpeg_CropsToNormalizedCoordinatesAndEncodesAsJpeg()
    {
        // 1000 x 500 image, crop right half (front cover) 500 x 500
        var sourceBytes = CreateSampleImageBytes(1000, 500, SKColors.SeaGreen);
        var cropRect = new NormalizedCropRect(0.5, 0.0, 0.5, 1.0);

        var jpegBytes = ImageConverter.CropImageToJpeg(sourceBytes, cropRect, quality: 92);

        Assert.NotNull(jpegBytes);
        Assert.NotEmpty(jpegBytes);

        using var bitmap = SKBitmap.Decode(jpegBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(bitmap.Width, bitmap.Height);

        using var codec = SKCodec.Create(new MemoryStream(jpegBytes));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
    }

    private static byte[] CreateSampleJpegBytes(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "javbuddy-imageconverter-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (int Width, int Height) DecodedSize(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return (bitmap.Width, bitmap.Height);
    }

    // Large landscape jackets are decoded only as big as the front-cover crop needs,
    // but the cropped poster must still come out at the same size as before: the right ~47.2% of
    // the width at full height, downscaled (never upscaled) so its long edge is at most
    // FullMaxEdge. Includes sizes where a JPEG decoder's n/8 scale steps round below the
    // requested scale.
    [Theory]
    [InlineData(3600, 2420)]
    [InlineData(5000, 3360)]
    [InlineData(6000, 4000)]
    [InlineData(8000, 5376)]
    public void ConvertPosterToBothWebP_LargeLandscapeJacket_CropsToFullSizeFrontCover(int width, int height)
    {
        var path = WriteTempFile(CreateSampleJpegBytes(width, height, SKColors.DarkOrange));
        try
        {
            var (thumbBytes, fullBytes) = ImageConverter.ConvertPosterToBothWebP(path);

            var expectedHeight = Math.Min(height, ImageConverter.FullMaxEdge);
            var expectedWidth = (width - (int)(width / 1.895734597)) * (double)expectedHeight / height;
            var (fullWidth, fullHeight) = DecodedSize(fullBytes);
            Assert.Equal(expectedHeight, fullHeight);
            Assert.InRange(fullWidth, expectedWidth - 2, expectedWidth + 2);

            var (_, thumbHeight) = DecodedSize(thumbBytes);
            Assert.Equal(ImageConverter.ThumbMaxEdge, thumbHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertPosterToBothWebP_LargePortraitPoster_IsNotCroppedAndCapsLongEdge()
    {
        var path = WriteTempFile(CreateSampleJpegBytes(3000, 4500, SKColors.Teal));
        try
        {
            var (_, fullBytes) = ImageConverter.ConvertPosterToBothWebP(path);

            var (fullWidth, fullHeight) = DecodedSize(fullBytes);
            Assert.Equal(ImageConverter.FullMaxEdge, fullHeight);
            Assert.InRange(fullWidth, 1706, 1707);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertPosterToBothWebP_SmallLandscapeJacket_CropsAtSourceResolution()
    {
        var path = WriteTempFile(CreateSampleJpegBytes(1600, 1076, SKColors.Olive));
        try
        {
            var (_, fullBytes) = ImageConverter.ConvertPosterToBothWebP(path);

            Assert.Equal((1600 - (int)(1600 / 1.895734597), 1076), DecodedSize(fullBytes));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CropCoverToBothWebP_LargeCover_CropsToFullSizeFrontCover()
    {
        var (_, fullBytes) = ImageConverter.CropCoverToBothWebP(CreateSampleJpegBytes(5000, 3360, SKColors.Purple));

        var (fullWidth, fullHeight) = DecodedSize(fullBytes);
        Assert.Equal(ImageConverter.FullMaxEdge, fullHeight);
        Assert.InRange(fullWidth, 1797, 1801);
    }

    // dropped the SKImage.FromBitmap pixel copy before each encode — the encoded bytes
    // must be exactly what the FromBitmap path produced.
    [Fact]
    public void ConvertBytesToBothWebP_FullVariant_IsByteIdenticalToImageEncodePath()
    {
        var sourceBytes = CreateSampleJpegBytes(800, 600, SKColors.Goldenrod);

        var (_, fullBytes) = ImageConverter.ConvertBytesToBothWebP(sourceBytes);

        using var codec = SKCodec.Create(new MemoryStream(sourceBytes));
        using var decoded = new SKBitmap(codec.Info);
        codec.GetPixels(codec.Info, decoded.GetPixels());
        using var image = SKImage.FromBitmap(decoded);
        using var expected = image.Encode(SKEncodedImageFormat.Webp, ImageConverter.WebPQualityFull);
        Assert.Equal(expected.ToArray(), fullBytes);
    }

    [Fact]
    public void CropImageToJpeg_IsByteIdenticalToImageEncodePath()
    {
        var sourceBytes = CreateSampleImageBytes(1000, 500, SKColors.SlateBlue);

        var jpegBytes = ImageConverter.CropImageToJpeg(sourceBytes, new NormalizedCropRect(0.5, 0.0, 0.5, 1.0), quality: 92);

        using var codec = SKCodec.Create(new MemoryStream(sourceBytes));
        using var decoded = new SKBitmap(codec.Info);
        codec.GetPixels(codec.Info, decoded.GetPixels());
        using var cropped = new SKBitmap();
        Assert.True(decoded.ExtractSubset(cropped, new SKRectI(500, 0, 1000, 500)));
        using var image = SKImage.FromBitmap(cropped);
        using var expected = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        Assert.Equal(expected.ToArray(), jpegBytes);
    }
}
