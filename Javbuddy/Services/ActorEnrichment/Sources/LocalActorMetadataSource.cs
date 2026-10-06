using Javbuddy.Data;
using Javbuddy.Services.Actors;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.ActorEnrichment.Sources;

public class LocalActorMetadataSource(
    ILocalLibraryClient localLibraryClient,
    IDbContextFactory<AppDbContext> dbFactory) : IActorMetadataSource
{
    public const string SourceNameConstant = "Local";

    public string SourceName => SourceNameConstant;

    public int Priority => 10;

    public bool CanAutoEnrich => true;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public async Task<ActorMetadataResult?> LookupAsync(ActorEnrichmentContext context, CancellationToken ct = default)
    {
        var movieCodes = new List<string>(context.LinkedMovieCodes);

        // If no linked movies are provided in context, attempt to find candidate movies in the library
        if (movieCodes.Count == 0)
        {
            var libraryCast = context.RunCache?.LibraryCast ?? await LibraryCastIndex.LoadAsync(dbFactory, ct);
            if (context.RunCache is not null) context.RunCache.LibraryCast = libraryCast;
            movieCodes.AddRange(libraryCast.CodesCasting(BuildCandidateNameSet(context)));
        }

        if (movieCodes.Count == 0) return null;

        var nameMatcher = BuildNameMatcher(context);
        var namesToCheck = new List<string> { context.DisplayName };
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            namesToCheck.Add($"{context.FirstName} {context.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKanji)) namesToCheck.Add(context.JapaneseNameKanji);
        if (!string.IsNullOrWhiteSpace(context.JapaneseNameKana)) namesToCheck.Add(context.JapaneseNameKana);
        foreach (var alias in context.ExistingAliases) namesToCheck.Add(alias);

        string? discoveredKanji = null;
        string? discoveredKana = null;
        var discoveredAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasLocalImage = false;
        var matchFound = false;

        foreach (var code in movieCodes)
        {
            ct.ThrowIfCancellationRequested();

            var movieActors = await localLibraryClient.GetMovieActorsAsync(code, ct);
            foreach (var meta in movieActors)
            {
                if (MatchesActor(meta, nameMatcher))
                {
                    matchFound = true;
                    ExtractJapaneseAndAliases(meta, ref discoveredKanji, ref discoveredKana, discoveredAliases);
                }
            }

            if (!hasLocalImage)
            {
                var imagePath = await localLibraryClient.ResolveActorImagePathAsync(code, namesToCheck, ct);
                if (imagePath is not null)
                {
                    hasLocalImage = true;
                    matchFound = true;
                }
            }
        }

        if (!matchFound) return null;

        // Filter out known display name or reversed name from discovered aliases
        discoveredAliases.Remove(context.DisplayName);
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            discoveredAliases.Remove($"{context.FirstName} {context.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(discoveredKanji)) discoveredAliases.Remove(discoveredKanji);
        if (!string.IsNullOrWhiteSpace(discoveredKana)) discoveredAliases.Remove(discoveredKana);

        return new ActorMetadataResult
        {
            SourceName = SourceName,
            JapaneseNameKanji = discoveredKanji,
            JapaneseNameKana = discoveredKana,
            Aliases = discoveredAliases.ToList()
        };
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
        if (!string.IsNullOrWhiteSpace(context.R18DevName)) set.Add(context.R18DevName);
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

            return false;
        };
    }

    private static bool MatchesActor(LocalActorMetadata meta, Func<string, bool> nameMatcher)
    {
        if (nameMatcher(meta.Name)) return true;
        if (!string.IsNullOrWhiteSpace(meta.AltName) && nameMatcher(meta.AltName)) return true;
        foreach (var alias in meta.Aliases)
        {
            if (nameMatcher(alias)) return true;
        }
        return false;
    }

    private static void ExtractJapaneseAndAliases(
        LocalActorMetadata meta,
        ref string? discoveredKanji,
        ref string? discoveredKana,
        HashSet<string> aliases)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(meta.AltName)) candidates.Add(meta.AltName);
        candidates.AddRange(meta.Aliases);

        foreach (var val in candidates)
        {
            if (string.IsNullOrWhiteSpace(val)) continue;
            var trimmed = val.Trim();
            if (JapaneseTextHelper.HasKanji(trimmed))
            {
                discoveredKanji ??= trimmed;
            }
            else if (JapaneseTextHelper.IsPureKana(trimmed))
            {
                discoveredKana ??= trimmed;
            }
            else
            {
                aliases.Add(trimmed);
            }
        }
    }
}
