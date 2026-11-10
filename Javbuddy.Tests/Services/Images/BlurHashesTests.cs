using Javbuddy.Services.Images;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public class BlurHashesTests
{
    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 100).ToArray();
    }

    private static byte[] Solid(SKColor color, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(60, 90);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        return Encode(bitmap, format);
    }

    [Fact]
    public void FromEncoded_ReturnsA28CharacterValidHash()
    {
        var hash = BlurHashes.FromEncoded(Solid(new SKColor(10, 20, 30)));

        Assert.NotNull(hash);
        Assert.Equal(28, hash!.Length);
        Assert.True(BlurHashes.IsValid(hash));
    }

    [Fact]
    public void AverageColor_OfASolidImage_IsThatColor()
    {
        Assert.Equal("#ff0000", BlurHashes.AverageColor(BlurHashes.FromEncoded(Solid(new SKColor(255, 0, 0)))));
        Assert.Equal("#336699", BlurHashes.AverageColor(BlurHashes.FromEncoded(Solid(new SKColor(0x33, 0x66, 0x99)))));
    }

    [Fact]
    public void FromEncoded_DiffersBetweenDifferentPictures()
    {
        using var bitmap = new SKBitmap(80, 120);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 40, 120, white);
        }

        var halves = BlurHashes.FromEncoded(Encode(bitmap, SKEncodedImageFormat.Png))!;
        var flat = BlurHashes.FromEncoded(Solid(SKColors.Gray))!;

        Assert.NotEqual(flat, halves);
        // Half black, half white averages to mid grey (in sRGB, give or take the quantisation).
        var grey = Convert.ToInt32(BlurHashes.AverageColor(halves)![1..3], 16);
        Assert.InRange(grey, 170, 200);
    }

    [Fact]
    public void FromEncoded_WebP_Works()
    {
        var color = BlurHashes.AverageColor(BlurHashes.FromEncoded(Solid(new SKColor(0, 200, 0), SKEncodedImageFormat.Webp)));

        Assert.NotNull(color);
        Assert.StartsWith("#00", color);
    }

    [Fact]
    public void FromEncoded_NotAnImage_ReturnsNull()
    {
        Assert.Null(BlurHashes.FromEncoded([1, 2, 3, 4]));
        Assert.Null(BlurHashes.FromEncoded([]));
    }

    [Fact]
    public void FromFile_ReadsTheFile_AndMissingFileIsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "javbuddy-tests-blurhash-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, Solid(new SKColor(0, 0, 255)));

            Assert.Equal("#0000ff", BlurHashes.AverageColor(BlurHashes.FromFile(path)));
            Assert.Null(BlurHashes.FromFile(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // An independent decoder (the reference algorithm, as the page's script implements it): what a hash actually
    // paints, so a wrong sign, a swapped axis or a bad quantisation shows up as a mirrored or garbled picture.
    private static double[] DecodeLuminanceGrid(string hash, int width, int height)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";
        int Dec(int a, int b) => hash[a..b].Aggregate(0, (v, c) => v * 83 + alphabet.IndexOf(c));
        double ToLinear(int c) { var v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        double SignSquare(double x) => x * Math.Abs(x);

        var size = Dec(0, 1);
        int nx = size % 9 + 1, ny = size / 9 + 1;
        var max = (Dec(1, 2) + 1) / 166.0;
        var colors = new List<double[]>();
        var dc = Dec(2, 6);
        colors.Add([ToLinear(dc >> 16), ToLinear(dc >> 8 & 255), ToLinear(dc & 255)]);
        for (var c = 1; c < nx * ny; c++)
        {
            var q = Dec(4 + c * 2, 6 + c * 2);
            colors.Add([SignSquare((q / 361 - 9) / 9.0) * max, SignSquare((q / 19 % 19 - 9) / 9.0) * max, SignSquare((q % 19 - 9) / 9.0) * max]);
        }

        var grid = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double sum = 0;
                for (var j = 0; j < ny; j++)
                {
                    for (var i = 0; i < nx; i++)
                    {
                        var basis = Math.Cos(Math.PI * x * i / width) * Math.Cos(Math.PI * y * j / height);
                        var col = colors[j * nx + i];
                        sum += basis * (col[0] + col[1] + col[2]) / 3;
                    }
                }

                grid[y * width + x] = sum;
            }
        }

        return grid;
    }

    private static string HashOf(Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(80, 120);
        using (var canvas = new SKCanvas(bitmap)) draw(canvas);
        return BlurHashes.FromEncoded(Encode(bitmap, SKEncodedImageFormat.Png))!;
    }

    [Fact]
    public void Hash_DecodesToTheOriginalLayout_LeftBrightRightDark()
    {
        var hash = HashOf(canvas =>
        {
            canvas.Clear(SKColors.Black);
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 40, 120, white);
        });

        var grid = DecodeLuminanceGrid(hash, 8, 12);
        double Column(int x) => Enumerable.Range(0, 12).Average(y => grid[y * 8 + x]);

        Assert.True(Column(1) > Column(6) + 0.3, $"left {Column(1)} vs right {Column(6)}");
    }

    [Fact]
    public void Hash_DecodesToTheOriginalLayout_TopBrightBottomDark()
    {
        var hash = HashOf(canvas =>
        {
            canvas.Clear(SKColors.Black);
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 80, 60, white);
        });

        var grid = DecodeLuminanceGrid(hash, 8, 12);
        double Row(int y) => Enumerable.Range(0, 8).Average(x => grid[y * 8 + x]);

        Assert.True(Row(2) > Row(9) + 0.3, $"top {Row(2)} vs bottom {Row(9)}");
    }

    [Theory]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdnj", true)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdn", false)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdnjj", false)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMd\"j", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ChecksAlphabetAndLength(string? hash, bool expected)
    {
        Assert.Equal(expected, BlurHashes.IsValid(hash));
        Assert.Equal(expected, BlurHashes.AverageColor(hash) is not null);
    }
}
