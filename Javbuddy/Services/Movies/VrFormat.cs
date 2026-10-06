using System.Text.RegularExpressions;

namespace Javbuddy.Services.Movies;

/// <summary>A video's VR / 3D format, as stored in MovieFile.VrType and Movie.VrType:
/// one of <see cref="All"/>, or null for flat 2D video.</summary>
public static partial class VrFormat
{
    public const string Vr180Sbs = "VR180 SBS";
    public const string Vr180Tb = "VR180 TB";
    public const string Vr180 = "VR180";
    public const string Vr360Sbs = "VR360 SBS";
    public const string Vr360Tb = "VR360 TB";
    public const string Vr360 = "VR360";
    public const string FisheyeSbs = "Fisheye SBS";
    public const string FisheyeTb = "Fisheye TB";
    public const string Fisheye = "Fisheye";
    public const string Vr = "VR";
    public const string ThreeDHsbs = "3D HSBS";
    public const string ThreeDFsbs = "3D FSBS";
    public const string ThreeDSbs = "3D SBS";
    public const string ThreeDHtab = "3D HTAB";
    public const string ThreeDFtab = "3D FTAB";
    public const string ThreeDTab = "3D TAB";
    public const string ThreeDMvc = "3D MVC";
    public const string ThreeD = "3D";

    /// <summary>Every format, in the order the manual picker offers them.</summary>
    public static readonly string[] All =
        [Vr180Sbs, Vr180Tb, Vr180, Vr360Sbs, Vr360Tb, Vr360, FisheyeSbs, FisheyeTb, Fisheye, Vr, ThreeDHsbs, ThreeDFsbs, ThreeDSbs, ThreeDHtab, ThreeDFtab, ThreeDTab, ThreeDMvc, ThreeD];

    // Jellyfin's 3D layout tokens (Emby.Naming NamingOptions.Format3DRules) plus the over/under
    // spellings, each with the format it names on its own (no VR projection token).
    private static readonly Dictionary<string, string> LayoutFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hsbs"] = ThreeDHsbs,
        ["fsbs"] = ThreeDFsbs,
        ["sbs"] = ThreeDSbs,
        ["sbs3d"] = ThreeDSbs,
        ["htab"] = ThreeDHtab,
        ["hou"] = ThreeDHtab,
        ["ftab"] = ThreeDFtab,
        ["tab"] = ThreeDTab,
        ["ou"] = ThreeDTab,
        ["mvc"] = ThreeDMvc,
    };

    // "lr" (left-right, DeoVR's naming) only sets a VR projection's layout; on its own it isn't 3D.
    private static readonly HashSet<string> SideBySideTokens = new(StringComparer.OrdinalIgnoreCase) { "sbs", "hsbs", "fsbs", "sbs3d", "lr" };
    private static readonly HashSet<string> TopBottomTokens = new(StringComparer.OrdinalIgnoreCase) { "tb", "tab", "htab", "ftab", "ou", "hou" };
    private static readonly HashSet<string> Projection180Tokens = new(StringComparer.OrdinalIgnoreCase) { "180", "vr180" };
    private static readonly HashSet<string> Projection360Tokens = new(StringComparer.OrdinalIgnoreCase) { "360", "vr360" };
    private static readonly HashSet<string> FisheyeTokens = new(StringComparer.OrdinalIgnoreCase) { "fisheye", "fisheye190" };

    /// <summary>Every token that names a format, for stripping them out of a version tag.</summary>
    private static readonly HashSet<string> FormatTokens = new(
        [.. LayoutFormats.Keys, .. SideBySideTokens, .. TopBottomTokens, .. Projection180Tokens, .. Projection360Tokens, .. FisheyeTokens, "3d", "vr"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The format a video file's name tags, or null when it tags none. Tokens are split on
    /// Jellyfin's flag delimiters ( ) - . _ [ ] and spaces, after the movie's code when the name starts
    /// with it, so a code's own number ("VRKM-180") isn't read as a flag. A VR projection (180/VR180,
    /// 360/VR360, fisheye) wins over a plain 3D layout; its layout is SBS or TB when one is tagged.</summary>
    public static string? FromFileName(string? fileName, string? movieCode = null)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var code = movieCode?.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            var unhyphenated = code.Replace("-", "").Replace("_", "");
            if (stem.StartsWith(code, StringComparison.OrdinalIgnoreCase)) stem = stem[code.Length..];
            else if (stem.StartsWith(unhyphenated, StringComparison.OrdinalIgnoreCase)) stem = stem[unhyphenated.Length..];
        }
        var tokens = TokenSeparator().Split(stem).Where(t => t.Length > 0).ToList();

        var sbs = tokens.Any(SideBySideTokens.Contains);
        var tb = tokens.Any(TopBottomTokens.Contains);
        string? Layout(string sbsFormat, string tbFormat, string bare) => sbs ? sbsFormat : tb ? tbFormat : bare;

        if (tokens.Any(Projection180Tokens.Contains)) return Layout(Vr180Sbs, Vr180Tb, Vr180);
        if (tokens.Any(Projection360Tokens.Contains)) return Layout(Vr360Sbs, Vr360Tb, Vr360);
        if (tokens.Any(FisheyeTokens.Contains)) return Layout(FisheyeSbs, FisheyeTb, Fisheye);

        var layout = tokens.Select(t => LayoutFormats.GetValueOrDefault(t)).FirstOrDefault(f => f is not null);
        if (layout is not null) return layout;
        if (tokens.Any(t => t.Equals("3d", StringComparison.OrdinalIgnoreCase))) return ThreeD;
        if (tokens.Any(t => t.Equals("vr", StringComparison.OrdinalIgnoreCase))) return Vr;
        return null;
    }

    /// <summary>A file's format: its name's tag; else VR180 SBS for a side-by-side (2:1) frame, the
    /// near-universal VR release format; else plain VR when the movie's genres include VR; else null.
    /// A bare VR180 or fisheye tag on a side-by-side frame is VR180 SBS or Fisheye SBS.</summary>
    public static string? Detect(string? fileName, string? movieCode, int? width, int? height, string? metaGenres)
    {
        var tagged = FromFileName(fileName, movieCode);
        var sideBySide = SideBySideVideo.IsSideBySide(width, height);
        if (tagged == Vr180 && sideBySide) return Vr180Sbs;
        if (tagged == Fisheye && sideBySide) return FisheyeSbs;
        if (tagged is not null) return tagged;
        if (sideBySide) return Vr180Sbs;
        return HasVrGenre(metaGenres) ? Vr : null;
    }

    /// <summary>Whether a comma-joined genre list (Movie.MetaGenres) has a genre naming VR as a word,
    /// e.g. "VR", "VR Exclusive" or "High-Quality VR".</summary>
    public static bool HasVrGenre(string? metaGenres) =>
        !string.IsNullOrWhiteSpace(metaGenres) && metaGenres.Split(',').Any(g => VrWord().IsMatch(g));

    /// <summary>The poster's corner badge: "3D" for a 3D layout, "VR" for every VR format.</summary>
    public static string? BadgeText(string? vrType) =>
        string.IsNullOrWhiteSpace(vrType) ? null : vrType.StartsWith("3D", StringComparison.Ordinal) ? ThreeD : Vr;

    /// <summary>A version tag without the format tokens the badge already shows: "RIFE-3.1.3d.hsbs" →
    /// "RIFE-3.1", "4K_180_sbs" → "4K". Null when only format tokens were left.</summary>
    public static string? StripFormatTokens(string versionTag)
    {
        var stripped = FormatTokenPattern().Replace(versionTag, match => FormatTokens.Contains(match.Groups["token"].Value) ? "" : match.Value);
        stripped = RepeatedDelimiters().Replace(stripped, match => match.Value[..1]).Trim(['-', '_', '.', ' ', '[', ']', '(', ')']);
        return stripped.Length > 0 ? stripped : null;
    }

    [GeneratedRegex(@"[\s()\-._\[\]]+")]
    private static partial Regex TokenSeparator();

    // A run of non-delimiter characters, i.e. one token, so whole tokens are matched only.
    [GeneratedRegex(@"(?<token>[^\s()\-._\[\]]+)")]
    private static partial Regex FormatTokenPattern();

    [GeneratedRegex(@"[\s()\-._\[\]]{2,}")]
    private static partial Regex RepeatedDelimiters();

    [GeneratedRegex(@"(?i)(^|[^a-z])vr([^a-z]|$)")]
    private static partial Regex VrWord();
}
