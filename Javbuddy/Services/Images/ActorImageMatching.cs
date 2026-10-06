using System.Text;

namespace Javbuddy.Services.Images;

/// <summary>
/// Pure static helper for resolving actor image file paths against candidate actor names.
/// Handles naming variations including swapped word order (First Last &lt;-&gt; Last First),
/// separator variations (spaces, underscores, hyphens, dots, compact), Japanese Kanji/Kana
/// whitespace differences, and subfolder image conventions.
/// </summary>
public static class ActorImageMatching
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".jfif"];
    private static readonly char[] Separators = [' ', '_', '-', '.'];

    public static bool IsSupportedImageExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && AllowedImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a name string for resilient matching by removing all separators, punctuation,
    /// and converting to lowercase invariant.
    /// E.g. "Hashimoto_Arina" -> "hashimotoarina", "橋本 ありな" -> "橋本ありな".
    /// </summary>
    public static string NormalizeForMatching(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var span = name.AsSpan().Trim();
        var sb = new StringBuilder(span.Length);
        foreach (var c in span)
        {
            if (char.IsWhiteSpace(c) || c is '_' or '-' or '.' or '\'' or '\"' or '`') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks if two actor names denote the same person, accounting for word order swaps
    /// (e.g. "Araki Noa" &lt;-&gt; "Noa Araki"), separator differences ("Mikami_Yua" &lt;-&gt; "Mikami Yua"),
    /// and whitespace in Japanese names ("新木 希空" &lt;-&gt; "新木希空").
    /// </summary>
    public static bool Matches(string? name1, string? name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;

        var t1 = name1.Trim();
        var t2 = name2.Trim();

        if (string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase)) return true;

        var keys1 = GenerateCandidateKeys([t1]);
        if (keys1.Contains(t2)) return true;

        var norm1 = NormalizeForMatching(t1);
        var norm2 = NormalizeForMatching(t2);
        if (norm1.Length > 0 && norm1 == norm2) return true;

        return false;
    }

    /// <summary>
    /// Checks if a given actor name matches any candidate name in the collection.
    /// </summary>
    public static bool MatchesAny(string? name, IEnumerable<string> candidateNames)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return candidateNames.Any(c => Matches(name, c));
    }

    /// <summary>
    /// Generates candidate lookup keys for actor names, covering:
    /// - Trimmed exact name
    /// - Word order reversal for 2-word names ("LastName FirstName" &lt;-&gt; "FirstName LastName")
    /// - Separator variations (spaces, underscores, hyphens, dots)
    /// - Compact form with spaces/separators removed
    /// - Japanese Kanji/Kana space-agnostic forms
    /// </summary>
    public static HashSet<string> GenerateCandidateKeys(IEnumerable<string> names)
    {
        var (original, swapped) = GenerateOrderedCandidateKeys(names);
        original.UnionWith(swapped);
        return original;
    }

    /// <summary>
    /// Separates original token order variants from swapped token order variants
    /// to support strict matching priority (original order preferred over swapped order).
    /// </summary>
    public static (HashSet<string> OriginalOrderKeys, HashSet<string> SwappedOrderKeys) GenerateOrderedCandidateKeys(IEnumerable<string> names)
    {
        var original = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var swapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var trimmed = name.Trim();
            if (trimmed.Length == 0) continue;

            original.Add(trimmed);

            var tokens = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0) continue;

            if (tokens.Length == 1)
            {
                original.Add(tokens[0]);
                continue;
            }

            AddSeparatorPermutations(original, tokens);

            if (tokens.Length == 2)
            {
                var reversed = new[] { tokens[1], tokens[0] };
                AddSeparatorPermutations(swapped, reversed);
            }
            else if (tokens.Length == 3)
            {
                var reversedFirstLast = new[] { tokens[2], tokens[0], tokens[1] };
                AddSeparatorPermutations(swapped, reversedFirstLast);
                var fullyReversed = new[] { tokens[2], tokens[1], tokens[0] };
                AddSeparatorPermutations(swapped, fullyReversed);
            }
        }

        return (original, swapped);
    }

    private static void AddSeparatorPermutations(HashSet<string> keys, string[] tokens)
    {
        keys.Add(string.Join(" ", tokens));
        keys.Add(string.Join("_", tokens));
        keys.Add(string.Join("-", tokens));
        keys.Add(string.Join(".", tokens));
        keys.Add(string.Concat(tokens));
    }

    /// <summary>
    /// Given a list of candidate file paths in an actors folder and candidate actor names,
    /// finds the best matching image file according to priority:
    /// 1. Exact candidate name match against file stem
    /// 2. Original word order candidate keys (underscores, hyphens, dots, compact)
    /// 3. Swapped word order candidate keys (swapped order, underscores, hyphens, dots, compact)
    /// 4. Normalized key match (all punctuation/separators stripped)
    /// 5. Subdirectory name match (e.g. .actors/Hashimoto Arina/photo.jpg)
    /// </summary>
    public static string? FindBestMatch(IReadOnlyList<string> filePaths, IEnumerable<string> candidateNames)
    {
        if (filePaths.Count == 0) return null;

        var validFiles = filePaths
            .Where(f => IsSupportedImageExtension(Path.GetExtension(f)))
            .ToList();

        if (validFiles.Count == 0) return null;

        var rawCandidates = candidateNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rawCandidates.Count == 0) return null;

        // Pass 1: Exact candidate name match against file stem
        var exactCandidates = new HashSet<string>(rawCandidates, StringComparer.OrdinalIgnoreCase);
        foreach (var file in validFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (exactCandidates.Contains(stem))
            {
                return file;
            }
        }

        var (originalOrderKeys, swappedOrderKeys) = GenerateOrderedCandidateKeys(rawCandidates);

        // Pass 2: Original word order separator variants
        foreach (var file in validFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (originalOrderKeys.Contains(stem))
            {
                return file;
            }
        }

        // Pass 3: Swapped word order variants
        foreach (var file in validFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (swappedOrderKeys.Contains(stem))
            {
                return file;
            }
        }

        // Pass 4: Normalized key match (all punctuation/separators stripped)
        var normalizedTargets = rawCandidates
            .Select(NormalizeForMatching)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in rawCandidates)
        {
            var tokens = name.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 2)
            {
                var reversedNorm = NormalizeForMatching(tokens[1] + tokens[0]);
                if (reversedNorm.Length > 0) normalizedTargets.Add(reversedNorm);
            }
        }

        foreach (var file in validFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var norm = NormalizeForMatching(stem);
            if (norm.Length > 0 && normalizedTargets.Contains(norm))
            {
                return file;
            }
        }

        // Pass 5: Subdirectory name match (e.g. .actors/Hashimoto Arina/photo.jpg)
        foreach (var file in validFiles)
        {
            var dirName = Path.GetFileName(Path.GetDirectoryName(file));
            if (!string.IsNullOrWhiteSpace(dirName))
            {
                if (exactCandidates.Contains(dirName) || originalOrderKeys.Contains(dirName) || swappedOrderKeys.Contains(dirName))
                {
                    return file;
                }
                var dirNorm = NormalizeForMatching(dirName);
                if (dirNorm.Length > 0 && normalizedTargets.Contains(dirNorm))
                {
                    return file;
                }
            }
        }

        return null;
    }
}
