using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Javbuddy.Services.Movies;

/// <summary>One external subtitle file beside a video. <see cref="Index"/> is its position in
/// <see cref="SubtitleFiles.Find"/>'s ordering, which the subtitle endpoint uses to name it, so a request never carries a path.</summary>
public sealed record SubtitleFile(int Index, string Path, string Label, string? Language);

/// <summary>Finds the sidecar .srt / .vtt files next to a video and converts them to WebVTT, the only
/// format a browser's &lt;track&gt; takes. Read-only: nothing is written beside the video.</summary>
public static partial class SubtitleFiles
{
    private static readonly string[] Extensions = [".srt", ".vtt"];

    /// <summary>The subtitle files in the video's folder named after it: <c>Name.srt</c>, or with a
    /// language or other suffix, <c>Name.en.srt</c>. Ordered by name, so the order is stable.</summary>
    public static IReadOnlyList<SubtitleFile> Find(string videoPath)
    {
        var folder = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return [];

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var matches = new List<(string Path, string Suffix)>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                matches.Add((path, ""));
            else if (name.Length > baseName.Length + 1 && name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                matches.Add((path, name[(baseName.Length + 1)..]));
        }

        return matches
            .OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .Select((m, i) => new SubtitleFile(i, m.Path, Label(m.Suffix), Language(m.Suffix)))
            .ToList();
    }

    /// <summary>The track's menu label: the language's English name for a language code, else the suffix as written.</summary>
    public static string Label(string suffix)
    {
        if (suffix.Length == 0) return "Subtitles";
        if (Language(suffix) is { } code)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(code);
                if (!string.IsNullOrEmpty(culture.EnglishName) && !culture.EnglishName.StartsWith("Unknown", StringComparison.Ordinal))
                    return culture.EnglishName;
            }
            catch (CultureNotFoundException)
            {
            }
        }
        return suffix;
    }

    /// <summary>The suffix as a language tag for the track's srclang, or null when it isn't shaped like one.</summary>
    public static string? Language(string suffix) => LanguageTag().IsMatch(suffix) ? suffix : null;

    /// <summary>The subtitle file's contents as WebVTT. A .vtt passes through; an SRT gets the WEBVTT header,
    /// its timestamps' decimal commas turned into points, and its text made safe as WebVTT markup
    /// (<c>&amp;</c> and a stray <c>&lt;</c> escaped, <c>font</c> tags dropped, <c>i</c>/<c>b</c>/<c>u</c> kept).</summary>
    public static string ToWebVtt(string path)
    {
        var text = Decode(File.ReadAllBytes(path)).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.StartsWith("WEBVTT", StringComparison.Ordinal)) return text;

        var lines = text.Split('\n').Select(l => l.Contains("-->", StringComparison.Ordinal)
            ? SrtTimestamp().Replace(l, m => m.Groups[1].Value + "." + m.Groups[2].Value.PadRight(3, '0'))
            : EscapeCueText(l));
        return "WEBVTT\n\n" + string.Join('\n', lines);
    }

    private static string EscapeCueText(string line)
    {
        line = FontTag().Replace(line, "").Replace("&", "&amp;");
        return StrayAngle().Replace(line, "&lt;");
    }

    // A UTF-16 file by its BOM, else UTF-8 when it is valid UTF-8, else Shift-JIS, what Japanese subtitles
    // without a BOM are usually saved as.
    private static string Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(bytes);
        if (bytes is [0xFE, 0xFF, ..]) return Encoding.BigEndianUnicode.GetString(bytes);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (CodePagesEncodingProvider.Instance.GetEncoding(932) ?? Encoding.Latin1).GetString(bytes);
        }
    }

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    [GeneratedRegex(@"</?font[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FontTag();

    [GeneratedRegex(@"<(?!/?[ibu]>)", RegexOptions.IgnoreCase)]
    private static partial Regex StrayAngle();

    [GeneratedRegex(@"(\d{1,2}:\d{2}:\d{2}),(\d{1,3})")]
    private static partial Regex SrtTimestamp();
}
