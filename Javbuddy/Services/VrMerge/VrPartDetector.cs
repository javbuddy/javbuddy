using System.Text;
using System.Text.RegularExpressions;

namespace Javbuddy.Services.VrMerge;

/// <summary>One file found in the scanned download folder.</summary>
public record VrFileEntry(string Path, string Name, long SizeBytes);

/// <summary>Pure, dependency-free helpers for detecting a VR release's multi-part video files and
/// building the ffmpeg concat list / FFMETADATA chapter file for merging them. Kept free of any I/O
/// so the detection and formatting logic can be unit tested directly against the real devr-041
/// filename set without touching disk or ffmpeg.</summary>
public static partial class VrPartDetector
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".wmv", ".iso", ".ts", ".m2ts", ".mov", ".flv"];

    /// <summary>Detects which files in a scanned folder are parts of the same multi-part release.
    /// Rejects everything else (spam files, ad shortcuts) by construction:
    /// 1. Only video extensions are considered.
    /// 2. Only the extension of the largest candidate file is kept (concat + -c copy needs one
    ///    container) — this alone rejects a differently-named/typed spam file in most cases.
    /// 3. Files smaller than 25% of the remaining candidates' median size are dropped — this is
    ///    what rejects a same-extension spam file sitting alongside much larger real parts.
    /// 4. The survivors must share a non-empty longest-common filename prefix, or they're treated
    ///    as unrelated files rather than parts of one release.
    /// Returns the parts in merge order (natural-sorted on the first differing numeric token, e.g.
    /// "..._1_8k.mp4" &lt; "..._2_8k.mp4" &lt; "..._3_8k.mp4"), or an empty list if fewer than 2
    /// parts survive.</summary>
    public static IReadOnlyList<VrFileEntry> DetectParts(IReadOnlyList<VrFileEntry> files)
    {
        var videoFiles = files.Where(f => IsVideoFile(f.Name)).ToList();
        if (videoFiles.Count < 2) return [];

        var dominantExtension = Path.GetExtension(videoFiles.OrderByDescending(f => f.SizeBytes).First().Name);
        var sameExtension = videoFiles
            .Where(f => string.Equals(Path.GetExtension(f.Name), dominantExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameExtension.Count < 2) return [];

        var median = MedianSize(sameExtension);
        var threshold = median * 0.25;
        var survivors = sameExtension.Where(f => f.SizeBytes >= threshold).ToList();
        if (survivors.Count < 2) return [];

        if (LongestCommonPrefixLength(survivors.Select(f => f.Name).ToList()) == 0) return [];

        return survivors.OrderBy(f => f.Name, NaturalFilenameComparer).ToList();
    }

    /// <summary>A-Z for the first 26 parts, then the 1-based part number beyond that.</summary>
    public static string ChapterTitle(string codeBase, int index) =>
        index < 26 ? $"{codeBase}-{(char)('A' + index)}" : $"{codeBase}-{index + 1}";

    /// <summary>Builds an FFMETADATA1 chapter file, one [CHAPTER] block per part with cumulative
    /// microsecond START/END boundaries, escaping '=', ';', '#', '\' and newlines in titles per the
    /// FFMETADATA spec.</summary>
    public static string BuildFfMetadata(IReadOnlyList<(string Title, long DurationMicroseconds)> parts)
    {
        var sb = new StringBuilder();
        sb.Append(";FFMETADATA1\n");

        var cumulative = 0L;
        foreach (var (title, durationMicroseconds) in parts)
        {
            var start = cumulative;
            var end = cumulative + durationMicroseconds;
            sb.Append("[CHAPTER]\n");
            sb.Append("TIMEBASE=1/1000000\n");
            sb.Append($"START={start}\n");
            sb.Append($"END={end}\n");
            sb.Append($"title={EscapeFfMetadataValue(title)}\n");
            cumulative = end;
        }

        return sb.ToString();
    }

    /// <summary>Builds an ffmpeg concat-demuxer list, one "file '...'" line per part, escaping
    /// single quotes the way the concat demuxer requires (' -> '\'').</summary>
    public static string BuildConcatList(IReadOnlyList<string> absolutePaths)
    {
        var sb = new StringBuilder();
        foreach (var path in absolutePaths)
        {
            sb.Append($"file '{path.Replace("'", "'\\''")}'\n");
        }

        return sb.ToString();
    }

    public static bool IsVideoFile(string name)
    {
        var ext = Path.GetExtension(name);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static long MedianSize(IReadOnlyList<VrFileEntry> files)
    {
        var sizes = files.Select(f => f.SizeBytes).OrderBy(s => s).ToList();
        var mid = sizes.Count / 2;
        return sizes.Count % 2 == 1 ? sizes[mid] : (sizes[mid - 1] + sizes[mid]) / 2;
    }

    private static int LongestCommonPrefixLength(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return 0;

        var prefixLength = names[0].Length;
        foreach (var name in names.Skip(1))
        {
            var max = Math.Min(prefixLength, name.Length);
            var i = 0;
            while (i < max && name[i] == names[0][i]) i++;
            prefixLength = i;
            if (prefixLength == 0) break;
        }

        return prefixLength;
    }

    public static string EscapeFfMetadataValue(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c is '=' or ';' or '#' or '\\' or '\n') sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static readonly IComparer<string> NaturalFilenameComparer = Comparer<string>.Create(CompareNatural);

    /// <summary>Splits both names into digit/non-digit runs and compares the first run where they
    /// differ — numerically for digit runs, ordinally otherwise — falling back to a full ordinal
    /// comparison if every run matches (should only happen for identical names).</summary>
    private static int CompareNatural(string? a, string? b)
    {
        var tokensA = TokenPattern().Matches(a ?? "").Select(m => m.Value).ToList();
        var tokensB = TokenPattern().Matches(b ?? "").Select(m => m.Value).ToList();

        for (var i = 0; i < Math.Min(tokensA.Count, tokensB.Count); i++)
        {
            var (ta, tb) = (tokensA[i], tokensB[i]);
            var cmp = char.IsDigit(ta[0]) && char.IsDigit(tb[0])
                ? long.Parse(ta).CompareTo(long.Parse(tb))
                : string.CompareOrdinal(ta, tb);
            if (cmp != 0) return cmp;
        }

        return tokensA.Count != tokensB.Count
            ? tokensA.Count.CompareTo(tokensB.Count)
            : string.CompareOrdinal(a, b);
    }

    [GeneratedRegex(@"\d+|\D+")]
    private static partial Regex TokenPattern();
}
