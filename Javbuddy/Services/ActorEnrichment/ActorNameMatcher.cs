using Javbuddy.Services.Common;

namespace Javbuddy.Services.ActorEnrichment;

/// <summary>Name matching shared by the actor-metadata sources: which queries to search a site
/// with, which names count as "this actor", and the search-then-pick-a-match loop.</summary>
public static class ActorNameMatcher
{
    /// <summary>Queries to try against a source's search, most distinctive first: Japanese kanji,
    /// "First Last", the display name, then each alias.</summary>
    public static List<string> BuildSearchQueries(ActorEnrichmentContext context)
    {
        var queries = new List<string>();

        // 1. Japanese kanji is the most distinct and reliable
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKanji))
        {
            queries.Add(context.JapaneseNameKanji.Trim());
        }

        // 2. Full reversed name (First Last) if last name exists
        if (!string.IsNullOrWhiteSpace(context.LastName) && !string.IsNullOrWhiteSpace(context.FirstName))
        {
            queries.Add($"{context.FirstName} {context.LastName}".Trim());
        }

        // 3. DisplayName (Last First)
        if (!string.IsNullOrWhiteSpace(context.DisplayName) && !queries.Contains(context.DisplayName))
        {
            queries.Add(context.DisplayName.Trim());
        }

        // 4. Aliases
        foreach (var alias in context.ExistingAliases)
        {
            var trimmed = alias.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !queries.Contains(trimmed))
            {
                queries.Add(trimmed);
            }
        }

        return queries;
    }

    /// <summary>A predicate answering "is this name one of the actor's known names?" — exact
    /// (trimmed, case-insensitive) match against the actor's names and aliases, or a match ignoring
    /// spaces for the Japanese kanji/kana. <paramref name="allowSwappedTwoWordNames"/> also accepts
    /// "Last First" for a known "First Last" (and vice versa); <paramref name="includeR18DevName"/>
    /// adds the actor's r18.dev name to the known names.</summary>
    public static Func<string, bool> BuildNameMatcher(
        ActorEnrichmentContext context,
        bool allowSwappedTwoWordNames = false,
        bool includeR18DevName = false)
    {
        var tokens = BuildCandidateNames(context, includeR18DevName);
        var compactKanji = context.JapaneseNameKanji?.CompactName();
        var compactKana = context.JapaneseNameKana?.CompactName();

        return name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            if (tokens.Contains(trimmed)) return true;

            var compact = trimmed.CompactName();
            if (compactKanji != null && compact.Equals(compactKanji, StringComparison.OrdinalIgnoreCase)) return true;
            if (compactKana != null && compact.Equals(compactKana, StringComparison.OrdinalIgnoreCase)) return true;

            if (allowSwappedTwoWordNames)
            {
                var nameParts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length == 2)
                {
                    foreach (var token in tokens)
                    {
                        var tokenParts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tokenParts.Length == 2 &&
                            string.Equals(tokenParts[0], nameParts[1], StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(tokenParts[1], nameParts[0], StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        };
    }

    /// <summary>Runs <paramref name="queries"/> through <paramref name="search"/> in order and
    /// returns the first result that is an exact match satisfying <paramref name="matches"/>, else
    /// the first result that satisfies it, from the first query that yields one; null if none does.</summary>
    public static async Task<TResult?> FindMatchAsync<TResult>(
        IEnumerable<string> queries,
        Func<string, CancellationToken, Task<IReadOnlyList<TResult>>> search,
        Func<TResult, bool> isExactMatch,
        Func<TResult, bool> matches,
        CancellationToken ct) where TResult : class
    {
        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            var results = await search(query, ct);
            if (results.Count == 0) continue;

            // Prefer exact match if found
            var exact = results.FirstOrDefault(r => isExactMatch(r) && matches(r));
            if (exact != null) return exact;

            // Fallback: first result satisfying the name matcher
            var anyMatch = results.FirstOrDefault(matches);
            if (anyMatch != null) return anyMatch;
        }

        return null;
    }

    /// <summary>Every name the actor is known by, for matching and cast lookups.</summary>
    public static HashSet<string> BuildCandidateNames(ActorEnrichmentContext context, bool includeR18DevName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { context.DisplayName };
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            set.Add($"{context.FirstName} {context.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKanji)) set.Add(context.JapaneseNameKanji);
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKana)) set.Add(context.JapaneseNameKana);
        if (includeR18DevName && !string.IsNullOrWhiteSpace(context.R18DevName)) set.Add(context.R18DevName);
        foreach (var a in context.ExistingAliases) set.Add(a);
        return set;
    }
}
