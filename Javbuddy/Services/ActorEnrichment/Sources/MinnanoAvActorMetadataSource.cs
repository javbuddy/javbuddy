using Javbuddy.Data;
using Javbuddy.Services.MinnanoAv;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.ActorEnrichment.Sources;

public class MinnanoAvActorMetadataSource(
    IMinnanoAvClient minnanoAvClient,
    IDbContextFactory<AppDbContext> dbFactory) : IActorMetadataSource
{
    public const string SourceNameConstant = "MinnanoAv";

    public string SourceName => SourceNameConstant;

    public int Priority => 30;

    public bool CanAutoEnrich => false;

    private readonly IMinnanoAvClient minnanoAvClient = minnanoAvClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.MinnanoAvSettings.ReadSingleRowAsync(ct);
        return settings?.Enabled == true;
    }

    public async Task<ActorMetadataResult?> LookupAsync(ActorEnrichmentContext context, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct)) return null;

        var nameMatcher = BuildNameMatcher(context);
        var queries = BuildSearchQueries(context);

        MinnanoAvSearchResult? matchedResult = null;

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            var results = await minnanoAvClient.SearchPerformersAsync(query, ct);
            if (results.Count == 0) continue;

            var exact = results.FirstOrDefault(r => r.IsExactMatch && MatchesPerformer(r, nameMatcher));
            if (exact != null)
            {
                matchedResult = exact;
                break;
            }

            var anyMatch = results.FirstOrDefault(r => MatchesPerformer(r, nameMatcher));
            if (anyMatch != null)
            {
                matchedResult = anyMatch;
                break;
            }
        }

        if (matchedResult == null) return null;

        var detail = await minnanoAvClient.GetPerformerDetailAsync(matchedResult.PathOrUrl, ct);
        if (detail == null || !detail.HasKnownAttributes) return null;

        return new ActorMetadataResult
        {
            SourceName = SourceName,
            JapaneseNameKanji = detail.Name,
            JapaneseNameKana = detail.Kana,
            HeightCm = detail.HeightCm,
            CupSize = detail.CupSize,
            Bust = detail.Bust,
            Waist = detail.Waist,
            Hips = detail.Hips,
            BirthDate = detail.BirthDate,
            IsRetired = detail.IsRetired,
            Aliases = detail.Aliases
        };
    }

    private static List<string> BuildSearchQueries(ActorEnrichmentContext context)
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

    private static HashSet<string> BuildCandidateNameSet(ActorEnrichmentContext context)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { context.DisplayName };
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            set.Add($"{context.FirstName} {context.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKanji)) set.Add(context.JapaneseNameKanji);
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKana)) set.Add(context.JapaneseNameKana);
        foreach (var a in context.ExistingAliases) set.Add(a);
        return set;
    }

    private static Func<string, bool> BuildNameMatcher(ActorEnrichmentContext context)
    {
        var tokens = BuildCandidateNameSet(context);
        var compactKanji = context.JapaneseNameKanji?.Replace(" ", "");
        var compactKana = context.JapaneseNameKana?.Replace(" ", "");

        return name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            if (tokens.Contains(trimmed)) return true;

            var compact = trimmed.Replace(" ", "");
            if (compactKanji != null && compact.Equals(compactKanji, StringComparison.OrdinalIgnoreCase)) return true;
            if (compactKana != null && compact.Equals(compactKana, StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var token in tokens)
            {
                var tokenParts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var nameParts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokenParts.Length == 2 && nameParts.Length == 2)
                {
                    if (string.Equals(tokenParts[0], nameParts[1], StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(tokenParts[1], nameParts[0], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        };
    }

    private static bool MatchesPerformer(MinnanoAvSearchResult performer, Func<string, bool> nameMatcher)
    {
        if (nameMatcher(performer.Name)) return true;
        if (!string.IsNullOrWhiteSpace(performer.Kana) && nameMatcher(performer.Kana)) return true;
        if (!string.IsNullOrWhiteSpace(performer.Romaji) && nameMatcher(performer.Romaji)) return true;
        foreach (var alias in performer.KnownAliases)
        {
            if (nameMatcher(alias)) return true;
        }
        return false;
    }
}
