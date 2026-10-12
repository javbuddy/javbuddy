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

        var nameMatcher = ActorNameMatcher.BuildNameMatcher(context, allowSwappedTwoWordNames: true);
        var matchedResult = await ActorNameMatcher.FindMatchAsync(
            ActorNameMatcher.BuildSearchQueries(context),
            minnanoAvClient.SearchPerformersAsync,
            r => r.IsExactMatch,
            r => MatchesPerformer(r, nameMatcher),
            ct);

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
