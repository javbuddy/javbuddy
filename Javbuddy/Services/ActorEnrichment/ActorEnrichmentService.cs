using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.ActorEnrichment;

public class ActorEnrichmentService(
    IEnumerable<IActorMetadataSource> sources,
    IDbContextFactory<AppDbContext> dbFactory,
    IActorImageCacheService? imageCacheService = null,
    ILogger<ActorEnrichmentService>? logger = null) : IActorEnrichmentService
{
    private readonly List<IActorMetadataSource> sources = sources.OrderBy(s => s.Priority).ToList();
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IActorImageCacheService? imageCacheService = imageCacheService;
    private readonly ILogger<ActorEnrichmentService> logger = logger ?? NullLogger<ActorEnrichmentService>.Instance;

    public IReadOnlyList<string> GetAvailableSources() =>
        sources.Select(s => s.SourceName).ToList();

    public Task<ActorEnrichmentResult> EnrichActorAsync(
        int actorId,
        ActorEnrichmentOptions? options = null,
        CancellationToken ct = default) =>
        EnrichActorAsync(actorId, options, runCache: null, ct);

    private async Task<ActorEnrichmentResult> EnrichActorAsync(
        int actorId,
        ActorEnrichmentOptions? options,
        ActorEnrichmentRunCache? runCache,
        CancellationToken ct)
    {
        options ??= new ActorEnrichmentOptions();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actor = await db.Actors
            .Include(a => a.Aliases)
            .Include(a => a.MovieActors)
                .ThenInclude(ma => ma.Movie)
            .FirstOrDefaultAsync(a => a.Id == actorId, ct);

        if (actor is null)
        {
            return ActorEnrichmentResult.Fail("Actor not found.");
        }

        var candidateSources = sources;
        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            candidateSources = candidateSources
                .Where(s => string.Equals(s.SourceName, options.Source, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidateSources.Count == 0)
            {
                return ActorEnrichmentResult.Fail($"Metadata source '{options.Source}' is not available.");
            }
        }
        else
        {
            candidateSources = candidateSources.Where(s => s.CanAutoEnrich).ToList();
        }

        var linkedMovieCodes = actor.MovieActors
            .Select(ma => ma.Movie?.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct()
            .ToList();

        var context = new ActorEnrichmentContext(
            actor.Id,
            actor.DisplayName,
            actor.FirstName,
            actor.LastName,
            actor.JapaneseNameKanji,
            actor.JapaneseNameKana,
            actor.Aliases.Select(a => a.Name).ToList(),
            linkedMovieCodes,
            actor.JellyfinPersonId,
            actor.R18DevId,
            actor.R18DevName)
        {
            RunCache = runCache
        };

        ActorMetadataResult? result = null;
        IActorMetadataSource? activeSource = null;

        foreach (var source in candidateSources)
        {
            ct.ThrowIfCancellationRequested();
            if (!await source.IsAvailableAsync(ct)) continue;

            try
            {
                var lookup = await source.LookupAsync(context, ct);
                if (lookup is not null)
                {
                    result = lookup;
                    activeSource = source;
                    break;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Source {Source} failed lookup for actor {ActorId}", source.SourceName, actorId);
            }
        }

        if (result is null || activeSource is null)
        {
            return ActorEnrichmentResult.Fail("No metadata found from available sources.");
        }

        var updatedFields = new List<string>();
        var addedAliases = new List<string>();

        // JapaneseNameKanji
        if (!string.IsNullOrWhiteSpace(result.JapaneseNameKanji)
            && (options.OverwriteExisting || string.IsNullOrWhiteSpace(actor.JapaneseNameKanji))
            && !string.Equals(actor.JapaneseNameKanji, result.JapaneseNameKanji, StringComparison.Ordinal))
        {
            actor.JapaneseNameKanji = result.JapaneseNameKanji.Trim();
            updatedFields.Add(nameof(actor.JapaneseNameKanji));
        }

        // JapaneseNameKana
        if (!string.IsNullOrWhiteSpace(result.JapaneseNameKana)
            && (options.OverwriteExisting || string.IsNullOrWhiteSpace(actor.JapaneseNameKana))
            && !string.Equals(actor.JapaneseNameKana, result.JapaneseNameKana, StringComparison.Ordinal))
        {
            actor.JapaneseNameKana = result.JapaneseNameKana.Trim();
            updatedFields.Add(nameof(actor.JapaneseNameKana));
        }

        // JellyfinPersonId
        if (!string.IsNullOrWhiteSpace(result.JellyfinPersonId)
            && (options.OverwriteExisting || string.IsNullOrWhiteSpace(actor.JellyfinPersonId))
            && !string.Equals(actor.JellyfinPersonId, result.JellyfinPersonId, StringComparison.Ordinal))
        {
            actor.JellyfinPersonId = result.JellyfinPersonId.Trim();
            updatedFields.Add(nameof(actor.JellyfinPersonId));
        }

        // R18DevId
        if (result.R18DevId.HasValue
            && (options.OverwriteExisting || !actor.R18DevId.HasValue)
            && actor.R18DevId != result.R18DevId)
        {
            actor.R18DevId = result.R18DevId;
            updatedFields.Add(nameof(actor.R18DevId));
        }

        // R18DevName
        if (!string.IsNullOrWhiteSpace(result.R18DevName)
            && (options.OverwriteExisting || string.IsNullOrWhiteSpace(actor.R18DevName))
            && !string.Equals(actor.R18DevName, result.R18DevName, StringComparison.OrdinalIgnoreCase))
        {
            actor.R18DevName = result.R18DevName.Trim();
            updatedFields.Add(nameof(actor.R18DevName));
        }

        // HeightCm
        if (result.HeightCm.HasValue
            && (options.OverwriteExisting || !actor.HeightCm.HasValue)
            && actor.HeightCm != result.HeightCm)
        {
            actor.HeightCm = result.HeightCm;
            updatedFields.Add(nameof(actor.HeightCm));
        }

        // CupSize
        var normalizedCup = ActorPhysicalAttributesHelper.StandardCupSizeOrNull(result.CupSize);
        if (!string.IsNullOrWhiteSpace(normalizedCup)
            && (options.OverwriteExisting || string.IsNullOrWhiteSpace(actor.CupSize))
            && !string.Equals(actor.CupSize, normalizedCup, StringComparison.OrdinalIgnoreCase))
        {
            actor.CupSize = normalizedCup;
            updatedFields.Add(nameof(actor.CupSize));
        }

        // Measurements fallback
        var resBust = result.Bust;
        var resWaist = result.Waist;
        var resHips = result.Hips;
        if (!resBust.HasValue && !resWaist.HasValue && !resHips.HasValue && !string.IsNullOrWhiteSpace(result.Measurements))
        {
            ActorPhysicalAttributesHelper.TryParseMeasurements(result.Measurements, out resBust, out resWaist, out resHips);
        }

        // Bust
        if (resBust.HasValue
            && (options.OverwriteExisting || !actor.Bust.HasValue)
            && actor.Bust != resBust)
        {
            actor.Bust = resBust;
            updatedFields.Add(nameof(actor.Bust));
        }

        // Waist
        if (resWaist.HasValue
            && (options.OverwriteExisting || !actor.Waist.HasValue)
            && actor.Waist != resWaist)
        {
            actor.Waist = resWaist;
            updatedFields.Add(nameof(actor.Waist));
        }

        // Hips
        if (resHips.HasValue
            && (options.OverwriteExisting || !actor.Hips.HasValue)
            && actor.Hips != resHips)
        {
            actor.Hips = resHips;
            updatedFields.Add(nameof(actor.Hips));
        }

        // BirthDate
        if (result.BirthDate.HasValue
            && ActorPhysicalAttributesHelper.IsAllowedBirthDate(result.BirthDate)
            && (options.OverwriteExisting || !actor.BirthDate.HasValue)
            && actor.BirthDate != result.BirthDate)
        {
            actor.BirthDate = result.BirthDate;
            updatedFields.Add(nameof(actor.BirthDate));
        }

        // IsRetired
        if (result.IsRetired.HasValue
            && (options.OverwriteExisting || !actor.IsRetired)
            && actor.IsRetired != result.IsRetired.Value)
        {
            actor.IsRetired = result.IsRetired.Value;
            updatedFields.Add(nameof(actor.IsRetired));
        }

        // Aliases
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { actor.DisplayName };
        if (!string.IsNullOrWhiteSpace(actor.LastName)) knownNames.Add($"{actor.FirstName} {actor.LastName}".Trim());
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji)) knownNames.Add(actor.JapaneseNameKanji);
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKana)) knownNames.Add(actor.JapaneseNameKana);
        foreach (var a in actor.Aliases) knownNames.Add(a.Name);

        foreach (var alias in result.Aliases)
        {
            var trimmed = alias.Trim();
            if (trimmed.Length > 0 && !knownNames.Contains(trimmed))
            {
                var newAlias = new ActorAlias
                {
                    ActorId = actor.Id,
                    Name = trimmed,
                    CreatedAt = DateTime.UtcNow
                };
                actor.Aliases.Add(newAlias);
                db.ActorAliases.Add(newAlias);
                knownNames.Add(trimmed);
                addedAliases.Add(trimmed);
            }
        }

        if (updatedFields.Count > 0 || addedAliases.Count > 0)
        {
            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        }

        await db.SaveChangesAsync(ct);

        var imageRefreshed = false;
        if (options.RefreshImageCache && imageCacheService is not null)
        {
            try
            {
                var (thumb, full) = await imageCacheService.GetOrCreateBothAsync(actor.Id, ct);
                imageRefreshed = thumb is not null || full is not null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Image cache refresh failed for actor {ActorId}", actor.Id);
            }
        }

        return ActorEnrichmentResult.Ok(
            activeSource.SourceName,
            updatedFields.Count + addedAliases.Count,
            updatedFields,
            addedAliases,
            imageRefreshed);
    }

    public async Task<ActorBatchEnrichmentResult> EnrichAllAsync(
        ActorEnrichmentOptions? options = null,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actors = await db.Actors.Select(a => new { a.Id, a.DisplayName }).ToListAsync(ct);
        var total = actors.Count;

        var succeeded = 0;
        var failed = 0;
        var totalFieldsUpdated = 0;
        var runCache = new ActorEnrichmentRunCache();

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var actor = actors[i];
            progress?.Report(new TaskProgress(i + 1, total, actor.DisplayName));

            try
            {
                var res = await EnrichActorAsync(actor.Id, options, runCache, ct);
                if (res.Success)
                {
                    succeeded++;
                    totalFieldsUpdated += res.FieldsUpdated;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Enrichment failed unexpectedly for actor {ActorId}", actor.Id);
                failed++;
            }
        }

        return new ActorBatchEnrichmentResult(total, succeeded, failed, totalFieldsUpdated);
    }
}
