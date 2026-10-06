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
