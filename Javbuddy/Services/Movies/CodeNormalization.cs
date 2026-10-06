using System.Text.RegularExpressions;

namespace Javbuddy.Services.Movies;

/// <summary>Normalizes a release code (e.g. "SIVR-505") for cross-source matching between
/// Movie.Code and r18.dev's dvd_id. Uppercase, strip hyphens/whitespace — the same rule
/// javinizer-go's own r18.dev dump importer uses (normalizeDVDID), and the same rule the r18.dev
/// dump itself indexes on (derived_video_lower_idx: lower(replace(dvd_id, '-', ''))).</summary>
public static partial class CodeNormalization
{
    [GeneratedRegex(@"(?:[-_](?:BOD|DOD|RDOD|VOD|SO|DL|HD|4K|VR|BD|DVD|UC|SP|RE|EX|DX|SD|EC|T-EC|V-EC|TK\d?|BTK|[A-Z])|(?:\d+)(?:BOD|DOD|RDOD|VOD|SO|DL|HD|4K|VR|BD|DVD|UC|SP|RE|EX|DX|SD|EC|TK\d?|BTK|[A-Z]))$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingDistributorSuffixRegex();

    [GeneratedRegex(@"^(\d+)(.*)$")]
    private static partial Regex TrailingDigitsSplitRegex();

    [GeneratedRegex(@"^(?:[A-Z]_\d*|[A-Z]\d+|\d+|(?:DVD|VOD)(?:\d+|_)+|TK|PB|RS|KS)+[-_]?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingDistributorPrefixRegex();

    [GeneratedRegex(@"^([A-Z]\d{1,2})(\d{3,})$", RegexOptions.IgnoreCase)]
    private static partial Regex AlphanumericMakerCodeRegex();

    [GeneratedRegex(@"^([A-Z]{2,10})(\d+)[A-Z]?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnhyphenatedCodeRegex();

    [GeneratedRegex(@"^[A-Z]+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLettersRegex();

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        Span<char> buffer = stackalloc char[code.Length];
        var length = 0;
        foreach (var c in code)
        {
            if (char.IsWhiteSpace(c) || c == '-') continue;
            buffer[length++] = char.ToUpperInvariant(c);
        }
        return new string(buffer[..length]);
    }

    [GeneratedRegex(@"^([A-Z]+)0*(\d+)$")]
    private static partial Regex ZeroPaddingToleranceKeyRegex();

    /// <summary>
    /// Narrow counterpart to <see cref="GetCanonicalKey"/> for callers that only want to tolerate
    /// a zero-padding difference (e.g. "SAVR-1195" vs "SAVR-01195") between two codes that are
    /// otherwise known to denote the same release — not the full retailer/distributor-prefix
    /// canonicalization <see cref="GetCanonicalKey"/> does, which is too permissive for matching
    /// two arbitrary, otherwise-unrelated strings (e.g. it collapses "E2E-IMPORT-1" and "E2E-001"
    /// to the same key, since both reduce to a single trailing digit once distributor-style
    /// prefix/suffix stripping runs).
    /// Returns null when <paramref name="code"/> isn't a plain letters-then-digits code, so a
    /// caller comparing two null results never treats them as a match.
    /// </summary>
    public static string? GetZeroPaddingToleranceKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var match = ZeroPaddingToleranceKeyRegex().Match(Normalize(code));
        if (!match.Success) return null;

        var digits = match.Groups[2].Value.TrimStart('0');
        if (digits.Length == 0) digits = "0";
        return $"{match.Groups[1].Value}-{digits}";
    }

    /// <summary>
    /// Computes a canonical group key for deduplicating retailer/distributor variants of the same movie.
    /// Handles:
    /// - Leading distributor prefixes: "TKMIH-007", "TKWANZ-984", "9OFJE-600", "h_068OFJE-600", "h068OFJE600", "118ABW-001", "104FSMD-041", "4OFJE318", "4ATK318", "4ATI420", "PBAGMX-042"
    /// - Trailing retailer/delivery suffixes: "OFJE-600DOD", "OFJE-600BOD", "OFJE-600_4K", "IESP-049-F", "KA-2114DOD", "AMBI-172TK", "IBW-786Z"
    /// - DMM stream prefix variants that drop 'D' ("ATI" -> "ATID", "DAS" -> "DASD", "DAZ" -> "DAZD", "ATK" -> "ATKD")
    /// - Redundant zero-padding: "ARMD-000710" vs "ARMD-710", "MDS-000280" vs "MDS-280", "ATKD-00318" vs "ATKD-318"
    /// - Alphanumeric maker codes: "T28-585" vs "T28585"
    /// - Unhyphenated codes: "DSTH7014" vs "DSTH-7014", "REBD173" vs "REBD-173", "OFJE600" vs "OFJE-600"
    /// </summary>
    public static string GetCanonicalKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var clean = code.Trim().ToUpperInvariant();

        // 1. Strip trailing retailer / delivery / format suffixes (BOD, DOD, VOD, SO, DL, EC, TK, -F, etc.)
        var matchSuffix = TrailingDistributorSuffixRegex().Match(clean);
        if (matchSuffix.Success)
        {
            var matchDigits = TrailingDigitsSplitRegex().Match(matchSuffix.Value);
            if (matchDigits.Success)
            {
                clean = string.Concat(clean.AsSpan(0, matchSuffix.Index), matchDigits.Groups[1].Value);
            }
            else
            {
                clean = clean[..matchSuffix.Index];
            }
        }

        // 2. Strip leading distributor prefixes (e.g. "h_068", "h_237", "9", "4", "118", "104", "PB", "RS", "TK")
        var stripped = LeadingDistributorPrefixRegex().Replace(clean, "");
        if (stripped.Length >= 2 && stripped.Any(char.IsLetter))
        {
            clean = stripped;
        }

        // 3. Case A: Contains hyphen or underscore (e.g. "OFJE-600", "TKMIH-007", "ATKD-318", "SGLA-001", "AGMX-042")
        if (clean.Contains('-') || clean.Contains('_'))
        {
            var parts = clean.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var prefix = parts[0];
                var rest = string.Join('-', parts[1..]);

                // If prefix still starts with distributor prefix like TK/PB/RS (e.g. "TKMIH" -> "MIH", "TKWANZ" -> "WANZ")
                var strippedPrefix = LeadingDistributorPrefixRegex().Replace(prefix, "");
                if (strippedPrefix.Length >= 2 && strippedPrefix.Any(char.IsLetter))
                {
                    prefix = strippedPrefix;
                }

                // Normalize known DMM streaming prefixes that drop the trailing 'D'
                prefix = NormalizeDmmPrefix(prefix);

                // Clean the number part (strip redundant leading zeroes)
                var matchNum = Regex.Match(rest, @"\d+");
                if (matchNum.Success)
                {
                    var numClean = matchNum.Value.TrimStart('0');
                    if (numClean.Length == 0) numClean = "0";
                    return $"{prefix}-{numClean}";
                }

                return $"{prefix}-{rest}";
            }
        }

        // 4. Case B: Alphanumeric maker prefix like T28, T38, H46 (e.g. "T28585" -> "T28-585")
        var matchT = AlphanumericMakerCodeRegex().Match(clean);
        if (matchT.Success)
        {
            var prefix = matchT.Groups[1].Value;
            var num = matchT.Groups[2].Value.TrimStart('0');
            if (num.Length == 0) num = "0";
            return $"{prefix}-{num}";
        }

        // 5. Case C: Unhyphenated pattern (e.g. "OFJE600", "ATK318", "REBD173", "FSMD041", "DSTH7014")
        var match = UnhyphenatedCodeRegex().Match(clean);
        if (match.Success)
        {
            var prefix = NormalizeDmmPrefix(match.Groups[1].Value);
            var num = match.Groups[2].Value.TrimStart('0');
            if (num.Length == 0) num = "0";

            return $"{prefix}-{num}";
        }

        return Normalize(clean);
    }

    private static string NormalizeDmmPrefix(string prefix) => prefix switch
    {
        "ATI" => "ATID",
        "DAS" => "DASD",
        "DAZ" => "DAZD",
        "ATK" => "ATKD",
        "IPT" => "IPTD",
        "MB" => "MBD",
        "SHK" => "SHKD",
        "STA" => "STAR",
        "FSM" => "FSMD",
        _ => prefix
    };

    /// <summary>
    /// Evaluates how canonical/clean an r18.dev entry is (higher is better).
    /// Used to pick the cleanest DVD ID (e.g. "MIH-007" over "TKMIH-007", "OFJE-600" over "9OFJE-600", "OFJE-600DOD", or "4OFJE600") when deduplicating.
    /// </summary>
    public static int ScoreDvdIdForCanonical(string dvdId, bool hasPoster = false, bool hasTitle = false)
    {
        if (string.IsNullOrWhiteSpace(dvdId)) return 0;
        var clean = dvdId.Trim();
        var score = 0;

        // 1. Heavily penalize distributor delivery/retailer suffixes (DOD, BOD, VOD, SO, DL, EC, TK, etc.)
        if (TrailingDistributorSuffixRegex().IsMatch(clean))
        {
            score -= 2000;
        }

        // 2. Penalize entries starting with distributor prefixes or numbers
        var startsWithDistributor = char.IsDigit(clean[0])
            || clean.StartsWith("h_", StringComparison.OrdinalIgnoreCase)
            || (clean.StartsWith("h", StringComparison.OrdinalIgnoreCase) && clean.Length > 1 && char.IsDigit(clean[1]))
            || clean.StartsWith("t28", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("t38", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("tk", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("pb", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("rs", StringComparison.OrdinalIgnoreCase);

        if (!startsWithDistributor)
        {
            score += 1000;
        }
        else
        {
            score -= 500;
        }

        // 3. Strongly prefer standard hyphenated codes (e.g. OFJE-600 over OFJE600)
        if (clean.Contains('-')) score += 500;

        // 4. Penalize excessive zero-padding (e.g. prefer ARMD-710 over ARMD-000710)
        if (Regex.IsMatch(clean, @"-0{3,}\d+"))
        {
            score -= 100;
        }

        // 5. Prefer entries with poster image & title
        if (hasPoster) score += 100;
        if (hasTitle) score += 50;

        // 6. Prefer shorter codes
        score -= clean.Length;

        return score;
    }

    /// <summary>
    /// Checks whether a release code is a distributor/retailer re-release, On-Demand print (BOD/DOD),
    /// or digital web channel SKU (e.g. starts with "9", "4", "118", "104", "TK", "h_", "PB", "RS" or ends with "DOD", "BOD", etc.).
    /// </summary>
    public static bool IsDistributorRelease(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var clean = code.Trim().ToUpperInvariant();

        if (TrailingDistributorSuffixRegex().IsMatch(clean)) return true;

        if (char.IsDigit(clean[0])) return true;

        if (clean.StartsWith("H_", StringComparison.OrdinalIgnoreCase) ||
            (clean.StartsWith("H", StringComparison.OrdinalIgnoreCase) && clean.Length > 1 && char.IsDigit(clean[1])))
        {
            return true;
        }

        if (clean.StartsWith("TK", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("PB", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("RS", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("KS", StringComparison.OrdinalIgnoreCase))
        {
            var matchLetters = LeadingLettersRegex().Match(clean);
            if (matchLetters.Success && matchLetters.Value.Length >= 4)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\b(?:FC2[-_]?PPV[-_]?\d+|\d{6}[-_]\d{3}|[A-Za-z0-9]{2,10}[-_]\d{2,6}|[A-Za-z]{2,10}\d{2,6})\b", RegexOptions.IgnoreCase)]
    private static partial Regex MovieCodeExtractionRegex();

    /// <summary>
    /// Extracts a likely JAV release code embedded within a directory name or file name (e.g. "SSNI-081 [1080p]" -> "SSNI-081").
    /// </summary>
    public static string? ExtractMovieCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = MovieCodeExtractionRegex().Match(text);
        return match.Success ? match.Value : null;
    }
}
