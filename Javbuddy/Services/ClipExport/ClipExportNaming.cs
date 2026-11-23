using System.Globalization;
using System.Text;

namespace Javbuddy.Services.ClipExport;

/// <summary>Pure naming rules for exported clips: "MIDE-400 - Scene 2 [00-12-31 - 00-31-07].mp4", safe on every
/// filesystem (no ':' or other reserved characters).</summary>
public static class ClipExportNaming
{
    /// <summary>Most UTF-8 bytes kept of a code or label, so code + label + range + extension stay under the 255-byte file name
    /// limit of ext4 and most other filesystems (a Japanese character takes 3 bytes).</summary>
    public const int MaxLabelBytes = 150;

    private static readonly char[] Reserved = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>The file name (without folder) for a clip of <paramref name="code"/> labelled <paramref name="label"/>
    /// running from <paramref name="startSeconds"/> to <paramref name="endSeconds"/>, ending in <paramref name="extension"/> (".mp4").</summary>
    public static string FileName(string code, string label, double startSeconds, double endSeconds, string extension) =>
        $"{Sanitize(code)} - {Sanitize(label)} [{FormatTime(startSeconds)} - {FormatTime(endSeconds)}]{extension}";

    /// <summary>hh-mm-ss, whole seconds rounded down (the start of a range never moves past its real start).</summary>
    public static string FormatTime(double seconds)
    {
        var whole = (long)Math.Floor(Math.Max(seconds, 0));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 3600:00}-{whole / 60 % 60:00}-{whole % 60:00}");
    }

    /// <summary>Replaces reserved and control characters with '_', collapses whitespace, caps the length at
    /// <see cref="MaxLabelBytes"/> UTF-8 bytes (never splitting a character), and trims trailing dots and spaces (Windows drops
    /// them). Blank input becomes "clip".</summary>
    public static string Sanitize(string? text)
    {
        var builder = new StringBuilder();
        var lastWasSpace = false;
        foreach (var ch in text ?? string.Empty)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(char.IsControl(ch) || Array.IndexOf(Reserved, ch) >= 0 ? '_' : ch);
            lastWasSpace = false;
        }

        var result = CapUtf8Bytes(builder.ToString(), MaxLabelBytes).TrimEnd(' ', '.');
        return result.Length == 0 ? "clip" : result;
    }

    private static string CapUtf8Bytes(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes) break;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }
}
