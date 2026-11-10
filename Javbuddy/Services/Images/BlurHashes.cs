using SkiaSharp;

namespace Javbuddy.Services.Images;

/// <summary>A BlurHash (https://blurha.sh) of a poster thumbnail — a ~28-character string that decodes to a blurred
/// stand-in of the picture. A poster card paints it behind its image while the image loads, so a fast scroll fills
/// the grid with the covers' own colours and shapes instead of empty boxes. Computed from the already-small
/// thumbnail when it is written (the cache's conversion paths), and backfilled for thumbnails cached before the
/// column existed (ImageCacheTask). The decoder lives in App.razor's script; <see cref="AverageColor"/> reads the
/// first component so the card has a plain background colour before any script runs.</summary>
public static class BlurHashes
{
    // 3 across by 4 down suits a 2:3 portrait cover: 12 components, a 28-character hash.
    private const int ComponentsX = 3;
    private const int ComponentsY = 4;

    // The picture is shrunk to about this size first; a blur this coarse needs nothing more, and it keeps the
    // cosine sums cheap.
    private const int SampleWidth = 24;
    private const int SampleHeight = 36;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    /// <summary>Null when the bytes aren't a decodable image.</summary>
    public static string? FromEncoded(byte[] bytes)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            return bitmap is null ? null : Encode(bitmap);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Null when the file is missing or isn't a decodable image.</summary>
    public static string? FromFile(string path)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            return bitmap is null ? null : Encode(bitmap);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>True for a hash this encoder (or any standard BlurHash encoder) could have produced: only alphabet
    /// characters, and the length its size byte promises. Only a valid hash is ever rendered into the page.</summary>
    public static bool IsValid(string? hash)
    {
        if (hash is not { Length: >= 6 and <= 40 }) return false;
        foreach (var c in hash)
        {
            if (Alphabet.IndexOf(c) < 0) return false;
        }

        var size = Alphabet.IndexOf(hash[0]);
        var x = size % 9 + 1;
        var y = size / 9 + 1;
        return hash.Length == 6 + 2 * (x * y - 1);
    }

    /// <summary>The hash's average colour as "#rrggbb" (its first, DC component), or null if the hash isn't valid.</summary>
    public static string? AverageColor(string? hash)
    {
        if (!IsValid(hash)) return null;
        var value = DecodeBase83(hash!, 2, 6);
        return $"#{value >> 16 & 255:x2}{value >> 8 & 255:x2}{value & 255:x2}";
    }

    private static string? Encode(SKBitmap source)
    {
        if (source.Width == 0 || source.Height == 0) return null;
        using var small = source.Resize(new SKImageInfo(SampleWidth, SampleHeight, SKColorType.Rgba8888, SKAlphaType.Opaque),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (small is null) return null;

        var pixels = new float[SampleWidth * SampleHeight * 3];
        for (var y = 0; y < SampleHeight; y++)
        {
            for (var x = 0; x < SampleWidth; x++)
            {
                var pixel = small.GetPixel(x, y);
                var o = (y * SampleWidth + x) * 3;
                pixels[o] = SrgbToLinear(pixel.Red);
                pixels[o + 1] = SrgbToLinear(pixel.Green);
                pixels[o + 2] = SrgbToLinear(pixel.Blue);
            }
        }

        var factors = new (float R, float G, float B)[ComponentsX * ComponentsY];
        for (var j = 0; j < ComponentsY; j++)
        {
            for (var i = 0; i < ComponentsX; i++)
            {
                factors[j * ComponentsX + i] = Factor(pixels, i, j);
            }
        }

        var dc = factors[0];
        var ac = factors.Skip(1).ToArray();

        var hash = new System.Text.StringBuilder(6 + 2 * ac.Length);
        hash.Append(EncodeBase83(ComponentsX - 1 + (ComponentsY - 1) * 9, 1));

        double maximumValue;
        if (ac.Length > 0)
        {
            var actualMaximum = ac.Max(f => Math.Max(Math.Abs(f.R), Math.Max(Math.Abs(f.G), Math.Abs(f.B))));
            var quantisedMaximum = Math.Clamp((int)Math.Floor(actualMaximum * 166 - 0.5), 0, 82);
            maximumValue = (quantisedMaximum + 1) / 166.0;
            hash.Append(EncodeBase83(quantisedMaximum, 1));
        }
        else
        {
            maximumValue = 1;
            hash.Append(EncodeBase83(0, 1));
        }

        hash.Append(EncodeBase83(
            (LinearToSrgb(dc.R) << 16) + (LinearToSrgb(dc.G) << 8) + LinearToSrgb(dc.B), 4));
        foreach (var f in ac)
        {
            hash.Append(EncodeBase83(
                Quantise(f.R, maximumValue) * 19 * 19 + Quantise(f.G, maximumValue) * 19 + Quantise(f.B, maximumValue), 2));
        }

        return hash.ToString();
    }

    private static (float R, float G, float B) Factor(float[] pixels, int xComponent, int yComponent)
    {
        double r = 0, g = 0, b = 0;
        var normalisation = xComponent == 0 && yComponent == 0 ? 1 : 2;
        for (var y = 0; y < SampleHeight; y++)
        {
            for (var x = 0; x < SampleWidth; x++)
            {
                var basis = Math.Cos(Math.PI * xComponent * x / SampleWidth) * Math.Cos(Math.PI * yComponent * y / SampleHeight);
                var o = (y * SampleWidth + x) * 3;
                r += basis * pixels[o];
                g += basis * pixels[o + 1];
                b += basis * pixels[o + 2];
            }
        }

        var scale = normalisation / (double)(SampleWidth * SampleHeight);
        return ((float)(r * scale), (float)(g * scale), (float)(b * scale));
    }

    private static int Quantise(float value, double maximumValue) =>
        Math.Clamp((int)Math.Floor(SignPow(value / maximumValue, 0.5) * 9 + 9.5), 0, 18);

    private static double SignPow(double value, double exponent) => Math.Sign(value) * Math.Pow(Math.Abs(value), exponent);

    private static float SrgbToLinear(int value)
    {
        var v = value / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static int LinearToSrgb(float value)
    {
        var v = Math.Clamp(value, 0f, 1f);
        return v <= 0.0031308f ? (int)(v * 12.92f * 255f + 0.5f) : (int)((1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f) * 255f + 0.5f);
    }

    private static string EncodeBase83(int value, int length)
    {
        var chars = new char[length];
        for (var i = length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[value % 83];
            value /= 83;
        }

        return new string(chars);
    }

    private static int DecodeBase83(string text, int start, int end)
    {
        var value = 0;
        for (var i = start; i < end; i++) value = value * 83 + Alphabet.IndexOf(text[i]);
        return value;
    }
}
