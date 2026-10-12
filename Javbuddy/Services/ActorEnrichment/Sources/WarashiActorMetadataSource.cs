using Javbuddy.Data;
using Javbuddy.Services.Warashi;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.ActorEnrichment.Sources;

public class WarashiActorMetadataSource(
    IWarashiClient warashiClient,
    IDbContextFactory<AppDbContext> dbFactory) : IActorMetadataSource
{
    public const string SourceNameConstant = "Warashi";

    public string SourceName => SourceNameConstant;

    public int Priority => 20;

    public bool CanAutoEnrich => false;

    private readonly IWarashiClient warashiClient = warashiClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.WarashiSettings.ReadSingleRowAsync(ct);
        return settings?.Enabled == true;
    }

    public async Task<ActorMetadataResult?> LookupAsync(ActorEnrichmentContext context, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct)) return null;

        var nameMatcher = ActorNameMatcher.BuildNameMatcher(context, allowSwappedTwoWordNames: true);
        var matchedResult = await ActorNameMatcher.FindMatchAsync(
            ActorNameMatcher.BuildSearchQueries(context),
            warashiClient.SearchPerformersAsync,
            r => r.IsExactMatch,
            r => MatchesPerformer(r, nameMatcher),
            ct);

        if (matchedResult == null) return null;

        var detail = await warashiClient.GetPerformerDetailAsync(matchedResult.PathOrUrl, ct);
        if (detail == null || !detail.HasKnownAttributes) return null;

        var metadataResult = new ActorMetadataResult
        {
            SourceName = SourceName,
            JapaneseNameKanji = detail.JapaneseName,
            HeightCm = detail.HeightCm,
            CupSize = detail.CupSize,
            Bust = detail.Bust,
            Waist = detail.Waist,
            Hips = detail.Hips,
            BirthDate = detail.BirthDate,
            BloodType = detail.BloodType,
            IsRetired = detail.IsRetired,
            DebutYear = detail.DebutYear,
            Aliases = detail.Aliases
        };

        if (!string.IsNullOrWhiteSpace(detail.BirthPlace))
        {
            metadataResult.ExtraAttributes["BirthPlace"] = detail.BirthPlace;
        }

        if (!string.IsNullOrWhiteSpace(detail.MainPhotoUrl))
        {
            metadataResult.ExtraAttributes["MainPhotoUrl"] = detail.MainPhotoUrl;
        }

        return metadataResult;
    }

    private static bool MatchesPerformer(WarashiSearchResult performer, Func<string, bool> nameMatcher)
    {
        if (nameMatcher(performer.Name)) return true;
        if (!string.IsNullOrWhiteSpace(performer.JapaneseName) && nameMatcher(performer.JapaneseName)) return true;
        foreach (var alias in performer.KnownAliases)
        {
            if (nameMatcher(alias)) return true;
        }
        return false;
    }
}
