namespace Javbuddy.Services.Ffmpeg;

/// <summary>Maps ffmpeg/ffprobe stderr onto a small fixed set of failure reasons, the label values of
/// <c>javbuddy_ffmpeg_failures_total</c>. Bounded on purpose: free-form error text must never become a label.</summary>
public static class FfmpegFailureClassifier
{
    public const string MoovAtomMissing = "moov_atom_missing";
    public const string NoSuchFile = "no_such_file";
    public const string Permission = "permission";
    public const string InvalidData = "invalid_data";
    public const string Other = "other";

    // Most specific first: a truncated MP4 prints "moov atom not found" and then also "Invalid data found when processing input".
    private static readonly string[] Priority = [MoovAtomMissing, NoSuchFile, Permission, InvalidData, Other];

    /// <summary>The most specific reason found in <paramref name="text"/> (one line or a whole stderr), or <see cref="Other"/>.</summary>
    public static string Classify(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Other;
        if (Has(text, "moov atom not found")) return MoovAtomMissing;
        if (Has(text, "No such file or directory")) return NoSuchFile;
        if (Has(text, "Permission denied") || Has(text, "Operation not permitted")) return Permission;
        if (Has(text, "Invalid data found when processing input")) return InvalidData;
        return Other;
    }

    /// <summary>True when <paramref name="candidate"/> is a more specific reason than <paramref name="current"/>.</summary>
    internal static bool MoreSpecific(string candidate, string current) => Array.IndexOf(Priority, candidate) < Array.IndexOf(Priority, current);

    private static bool Has(string text, string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
