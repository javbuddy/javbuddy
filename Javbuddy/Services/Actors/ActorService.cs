using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.Common;
using Javbuddy.Services.Images;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Warashi;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

public class ActorUpdateModel
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? JapaneseNameKanji { get; set; }
    public string? JapaneseNameKana { get; set; }
    public string? R18DevName { get; set; }
    public string? JellyfinPersonId { get; set; }
    public int? R18DevId { get; set; }
    public List<string> Aliases { get; set; } = new();
    public int? HeightCm { get; set; }
    public string? CupSize { get; set; }
    public int? Bust { get; set; }
    public int? Waist { get; set; }
    public int? Hips { get; set; }
    public DateTime? BirthDate { get; set; }
    public bool IsRetired { get; set; }
    public List<ActorCupSizePeriodModel> CupSizePeriods { get; set; } = new();
}

/// <summary>One row of Actor Edit's dated cup size list.</summary>
public class ActorCupSizePeriodModel
{
    public DateTime? EffectiveFrom { get; set; }
    public string? CupSize { get; set; }
}

public record ActorOperationResult(bool Success, string? ErrorMessage = null, Actor? Actor = null, string? Warning = null)
{
    public static ActorOperationResult Ok(Actor actor) => new(true, null, actor);
    public static ActorOperationResult OkWithWarning(Actor actor, string warning) => new(true, null, actor, warning);
    public static ActorOperationResult Fail(string error) => new(false, error, null);
}

public record ActorMergeCandidate(
    int Id,
    string DisplayName,
    string? JapaneseNameKanji,
    string? JapaneseNameKana,
    int MovieCount,
    bool HasImage,
    bool IsSuggested);

/// <summary>One candidate in the Movie Detail page's "add actor to cast" search — deliberately
/// minimal (no movie count/suggestion fields like ActorMergeCandidate) since the caller only needs
/// an id, a name, and enough to render the same avatar-or-initials treatment as the header search
/// box's actor results.</summary>
public record ActorSearchItem(int Id, string DisplayName, bool HasImage);

public record ActorCardModel(
    int Id,
    string DisplayName,
    int MovieCount,
    bool HasImage,
    bool IsFavorite = false,
    DateTime CreatedAt = default,
    IReadOnlyList<string>? Aliases = null,
    string? JapaneseNameKanji = null,
    string? JapaneseNameKana = null,
    long? ImageVersion = null,
    bool HasJellyfin = false,
    int ImageCount = 0,
    string? CupSize = null,
    DateTime? BirthDate = null,
    bool IsRetired = false,
    int? HeightCm = null,
    int OwnedCount = 0)
{
    public int MissingCount => Math.Max(0, MovieCount - OwnedCount);

    public int? Age => ActorPhysicalAttributesHelper.CalculateAge(BirthDate);

    // "No metadata" for the Actors overview's needs-attention chip: none of the physical
    // attributes the detail page shows are set.
    public bool HasMetadata => BirthDate.HasValue || HeightCm.HasValue || !string.IsNullOrWhiteSpace(CupSize);
}

public sealed record ActorPortraitInfo(
    bool HasImage,
    bool HasCroppedImage,
    bool HasSourceImage,
    long ImageVersion = 0);

public interface IActorService
{
    Task<Actor?> GetByRouteNameAsync(string routeName, CancellationToken ct = default);
    Task<Actor?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ActorOperationResult> UpdateAsync(ActorUpdateModel model, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> HasImageAsync(int actorId, CancellationToken ct = default);
    Task<ActorPortraitInfo> GetPortraitInfoAsync(int actorId, CancellationToken ct = default);
    Task<IReadOnlyList<ActorMergeCandidate>> GetMergeCandidatesAsync(int sourceActorId, string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<ActorSearchItem>> SearchActorsAsync(string search, CancellationToken ct = default);
    Task<ActorOperationResult> MergeAsync(int sourceActorId, int targetActorId, bool updateNfoOnDisk = false, CancellationToken ct = default);
    Task<IReadOnlyList<ActorCardModel>> GetActorCardsAsync(CancellationToken ct = default);
    Task<bool> ToggleFavoriteAsync(int actorId, CancellationToken ct = default);
    Task<bool> IsJellyfinEnabledAsync(CancellationToken ct = default);
    Task<ActorEnrichmentResult> RefreshMetadataAsync(int actorId, CancellationToken ct = default);
    Task<ActorOperationResult> CreateAsync(string firstName, string? lastName, CancellationToken ct = default);
    Task<ActorOperationResult> AddAliasAsync(int actorId, string aliasName, CancellationToken ct = default);
    Task<ActorOperationResult> RemoveAliasAsync(int actorId, int aliasId, CancellationToken ct = default);
    Task<Actor?> FindMatchingActorAsync(string name, string? japaneseName = null, IReadOnlyList<string>? aliases = null, CancellationToken ct = default);
    Task<ActorOperationResult> ImportOrEnrichFromWarashiAsync(WarashiPerformerDetail detail, int? targetActorId = null, WarashiImportOptions? options = null, CancellationToken ct = default);
    Task<ActorOperationResult> ImportOrEnrichFromMinnanoAvAsync(MinnanoAvPerformerDetail detail, int? targetActorId = null, MinnanoAvImportOptions? options = null, CancellationToken ct = default);
}

public class ActorService(
    IDbContextFactory<AppDbContext> dbFactory,
    IActorImageCacheService? imageCacheService = null,
    INfoSyncService? nfoSyncService = null,
    IActorEnrichmentService? actorEnrichmentService = null,
    IActorPhotoService? actorPhotoService = null,
    IActorImageDataStore? dataStore = null,
    IJellyfinClient? jellyfinClient = null,
    IWarashiClient? warashiClient = null) : IActorService
{
    public async Task<Actor?> GetByRouteNameAsync(string routeName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routeName)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var route = routeName.Trim();

        // NOCASE rather than ToUpper() on both sides: SQLite's UPPER() only folds ASCII, while C#'s
        // ToUpper() also folds "ū" to "Ū", so a name like "Ichiiyūka" could never match.
        // NOCASE still ignores ASCII case and compares every other character exactly.
        return await db.Actors
            .AsNoTracking()
            .Include(a => a.Aliases)
            .Include(a => a.CupSizePeriods)
            .FirstOrDefaultAsync(a => a.FirstName != null
                && ((a.LastName == null && EF.Functions.Collate(a.FirstName, "NOCASE") == route)
                    || (a.LastName != null && EF.Functions.Collate(a.LastName + " " + a.FirstName, "NOCASE") == route)
                    || a.Aliases.Any(al => EF.Functions.Collate(al.Name, "NOCASE") == route)), ct);
    }

    public async Task<Actor?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Actors
            .AsNoTracking()
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<ActorOperationResult> UpdateAsync(ActorUpdateModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.FirstName))
        {
            return ActorOperationResult.Fail("First name is required.");
        }

        if (!ActorPhysicalAttributesHelper.IsAllowedBirthDate(model.BirthDate))
        {
            return ActorOperationResult.Fail($"Birthdate must make the actor at least {ActorPhysicalAttributesHelper.MinimumActorAge} years old.");
        }

        var firstName = model.FirstName.Trim();
        var lastName = model.LastName.TrimToNull();
        var proposedDisplayName = ActorDisplayName.Format(firstName, lastName);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var duplicate = await db.Actors.AnyAsync(a =>
            a.Id != model.Id
            && a.FirstName != null
            && a.FirstName.ToLower() == firstName.ToLower()
            && (
                (a.LastName == null && lastName == null)
                || (a.LastName != null && lastName != null && a.LastName.ToLower() == lastName.ToLower())
            ), ct);

        if (duplicate)
        {
            return ActorOperationResult.Fail($"An actor named \"{proposedDisplayName}\" already exists.");
        }

        var actor = await db.Actors
            .Include(a => a.Aliases)
            .Include(a => a.CupSizePeriods)
            .FirstOrDefaultAsync(a => a.Id == model.Id, ct);
        if (actor is null)
        {
            return ActorOperationResult.Fail("Actor not found.");
        }

        // Only standard sizes can be picked, but a non-standard value already stored is kept as-is.
        var cupSize = ActorPhysicalAttributesHelper.NormalizeCupSize(model.CupSize);
        if (cupSize is not null
            && !ActorPhysicalAttributesHelper.IsStandardCupSize(cupSize)
            && cupSize != ActorPhysicalAttributesHelper.NormalizeCupSize(actor.CupSize))
        {
            return ActorOperationResult.Fail($"Cup size must be one of {ActorPhysicalAttributesHelper.StandardCupSizes[0]}-{ActorPhysicalAttributesHelper.StandardCupSizes[^1]}.");
        }

        if (ValidateCupSizePeriods(model.CupSizePeriods, model.BirthDate) is { } periodError)
        {
            return ActorOperationResult.Fail(periodError);
        }

        var oldDisplayName = actor.DisplayName;
        var oldFirstName = actor.FirstName;
        var oldLastName = actor.LastName;

        var kanji = model.JapaneseNameKanji.TrimToNull();
        var kana = model.JapaneseNameKana.TrimToNull();
        var r18DevName = string.IsNullOrWhiteSpace(model.R18DevName) ? null : model.R18DevName.Trim();
        var jellyfinPersonId = model.JellyfinPersonId.TrimToNull();

        var namesChanged = !string.Equals(actor.FirstName, firstName, StringComparison.Ordinal)
            || !string.Equals(actor.LastName, lastName, StringComparison.Ordinal)
            || !string.Equals(actor.JapaneseNameKanji, kanji, StringComparison.Ordinal)
            || !string.Equals(actor.JapaneseNameKana, kana, StringComparison.Ordinal)
            || !string.Equals(actor.R18DevName, r18DevName, StringComparison.OrdinalIgnoreCase);

        var displayNameChanged = !string.Equals(oldDisplayName, proposedDisplayName, StringComparison.Ordinal);

        if (displayNameChanged && !string.IsNullOrWhiteSpace(oldDisplayName))
        {
            // 1. Preserve former display name as alias so external references and movie matching continue working
            if (!actor.Aliases.Any(a => string.Equals(a.Name, oldDisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                var alias = new ActorAlias
                {
                    ActorId = actor.Id,
                    Name = oldDisplayName,
                    CreatedAt = DateTime.UtcNow
                };
                actor.Aliases.Add(alias);
                db.ActorAliases.Add(alias);
            }

            // Also preserve old reversed name (FirstName LastName) as alias if it existed and differs
            if (!string.IsNullOrWhiteSpace(oldLastName))
            {
                var oldReversed = $"{oldFirstName} {oldLastName}".Trim();
                if (!string.Equals(oldReversed, proposedDisplayName, StringComparison.OrdinalIgnoreCase)
                    && !actor.Aliases.Any(a => string.Equals(a.Name, oldReversed, StringComparison.OrdinalIgnoreCase)))
                {
                    var alias = new ActorAlias
                    {
                        ActorId = actor.Id,
                        Name = oldReversed,
                        CreatedAt = DateTime.UtcNow
                    };
                    actor.Aliases.Add(alias);
                    db.ActorAliases.Add(alias);
                }
            }

            // 2. Build name matcher for previous names before mutating the actor
            var oldNameMatcher = NfoActorMatching.CreateNameMatcher(actor, [oldDisplayName]);

            // 3. Update MetaActresses in all movies linked to this actor so cast display reflects the new name
            var linkedMovieIds = await db.MovieActors
                .Where(ma => ma.ActorId == actor.Id)
                .Select(ma => ma.MovieId)
                .Distinct()
                .ToListAsync(ct);

            var linkedMovies = await db.Movies
                .Where(m => linkedMovieIds.Contains(m.Id))
                .ToListAsync(ct);

            foreach (var movie in linkedMovies)
            {
                NfoActorMatching.UpdateMovieMetaActresses(movie, oldNameMatcher, proposedDisplayName, addIfMissing: true);
            }

            // Also update any movies whose MetaActresses contains the old name but were not in linkedMovieIds
            var otherMatchingMovies = await db.Movies
                .Where(m => !linkedMovieIds.Contains(m.Id) && m.MetaActresses != null && m.MetaActresses.Contains(oldDisplayName))
                .ToListAsync(ct);

            foreach (var movie in otherMatchingMovies)
            {
                NfoActorMatching.UpdateMovieMetaActresses(movie, oldNameMatcher, proposedDisplayName, addIfMissing: false);
            }
        }

        actor.FirstName = firstName;
        actor.LastName = lastName;
        actor.JapaneseNameKanji = kanji;
        actor.JapaneseNameKana = kana;
        actor.R18DevName = r18DevName;
        actor.JellyfinPersonId = jellyfinPersonId;
        actor.R18DevId = model.R18DevId;
        actor.HeightCm = model.HeightCm is > 0 ? model.HeightCm : null;
        actor.CupSize = cupSize;
        actor.Bust = model.Bust is > 0 ? model.Bust : null;
        actor.Waist = model.Waist is > 0 ? model.Waist : null;
        actor.Hips = model.Hips is > 0 ? model.Hips : null;
        actor.BirthDate = model.BirthDate;
        actor.IsRetired = model.IsRetired;
        ApplyCupSizePeriods(actor, model.CupSizePeriods);

        await db.SaveChangesAsync(ct);

        if (namesChanged)
        {
            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
            await db.SaveChangesAsync(ct);
        }

        return ActorOperationResult.Ok(actor);
    }

    /// <summary>Null when every row has a date between the actor's 18th birthday (when known) and today, a
    /// standard cup size, and no two share a date.</summary>
    public static string? ValidateCupSizePeriods(IReadOnlyList<ActorCupSizePeriodModel> periods, DateTime? birthDate, DateOnly? today = null)
    {
        if (periods.Any(p => p.EffectiveFrom is null || ActorPhysicalAttributesHelper.NormalizeCupSize(p.CupSize) is null))
        {
            return "Each dated cup size needs both a date and a cup size.";
        }
        if (periods.Any(p => !ActorPhysicalAttributesHelper.IsStandardCupSize(ActorPhysicalAttributesHelper.NormalizeCupSize(p.CupSize))))
        {
            return $"Cup size must be one of {ActorPhysicalAttributesHelper.StandardCupSizes[0]}-{ActorPhysicalAttributesHelper.StandardCupSizes[^1]}.";
        }
        var latest = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (periods.Any(p => DateOnly.FromDateTime(p.EffectiveFrom!.Value) > latest))
        {
            return "A dated cup size can't start in the future.";
        }
        if (ActorPhysicalAttributesHelper.EarliestCupSizePeriodDate(birthDate) is { } earliest
            && periods.Any(p => DateOnly.FromDateTime(p.EffectiveFrom!.Value) < earliest))
        {
            return $"A dated cup size can't start before the actor turned {ActorPhysicalAttributesHelper.MinimumActorAge} ({earliest:yyyy-MM-dd}).";
        }
        if (periods.GroupBy(p => p.EffectiveFrom!.Value.Date).Any(g => g.Count() > 1))
        {
            return "Two dated cup sizes can't start on the same date.";
        }
        return null;
    }

    /// <summary>Updates rows in place by date rather than replacing them all, so the unique (actor, date)
    /// index never sees a delete and an insert of the same date in one save.</summary>
    private static void ApplyCupSizePeriods(Actor actor, IReadOnlyList<ActorCupSizePeriodModel> periods)
    {
        var wanted = periods.ToDictionary(p => p.EffectiveFrom!.Value.Date, p => ActorPhysicalAttributesHelper.NormalizeCupSize(p.CupSize)!);
        foreach (var existing in actor.CupSizePeriods.ToList())
        {
            if (wanted.Remove(existing.EffectiveFrom.Date, out var cup))
            {
                existing.CupSize = cup;
            }
            else
            {
                actor.CupSizePeriods.Remove(existing);
            }
        }
        foreach (var (from, cup) in wanted)
        {
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = from, CupSize = cup });
        }
    }

    public async Task<ActorOperationResult> CreateAsync(string firstName, string? lastName, CancellationToken ct = default)
    {
        var first = firstName.TrimToNull();
        if (first is null)
        {
            return ActorOperationResult.Fail("First name is required.");
        }

        var last = lastName.TrimToNull();
        var actor = new Actor { FirstName = first, LastName = last };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exists = await db.Actors.AnyAsync(a => a.FirstName != null
            && a.FirstName.ToLower() == first.ToLower()
            && a.LastName == last, ct);
        if (exists)
        {
            return ActorOperationResult.Fail($"An actor named \"{actor.DisplayName}\" already exists.");
        }

        db.Actors.Add(actor);
        await db.SaveChangesAsync(ct);
        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);
        return ActorOperationResult.Ok(actor);
    }

    public async Task<ActorOperationResult> AddAliasAsync(int actorId, string aliasName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aliasName))
        {
            return ActorOperationResult.Fail("Alias name cannot be empty.");
        }

        var trimmed = aliasName.Trim();
        if (trimmed.Length > 200)
        {
            return ActorOperationResult.Fail("Alias name cannot exceed 200 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actor = await db.Actors
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == actorId, ct);
        if (actor is null)
        {
            return ActorOperationResult.Fail("Actor not found.");
        }

        // 1. Check if the actor already has this alias
        if (actor.Aliases.Any(a => string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return ActorOperationResult.Fail($"The actor already has alias \"{trimmed}\".");
        }

        // 2. Check if alias collides with the actor's own canonical names
        var selfNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { actor.DisplayName };
        if (!string.IsNullOrWhiteSpace(actor.LastName))
        {
            selfNames.Add($"{actor.FirstName} {actor.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji))
        {
            selfNames.Add(actor.JapaneseNameKanji.Trim());
        }
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKana))
        {
            selfNames.Add(actor.JapaneseNameKana.Trim());
        }
        if (!string.IsNullOrWhiteSpace(actor.R18DevName))
        {
            selfNames.Add(actor.R18DevName.Trim());
        }

        if (selfNames.Contains(trimmed))
        {
            return ActorOperationResult.Fail($"Alias \"{trimmed}\" matches the actor's current name or variation.");
        }

        var trimmedCompact = trimmed.Replace(" ", "");
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji) && actor.JapaneseNameKanji.Replace(" ", "").Equals(trimmedCompact, StringComparison.OrdinalIgnoreCase))
        {
            return ActorOperationResult.Fail($"Alias \"{trimmed}\" matches the actor's current Japanese kanji.");
        }
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKana) && actor.JapaneseNameKana.Replace(" ", "").Equals(trimmedCompact, StringComparison.OrdinalIgnoreCase))
        {
            return ActorOperationResult.Fail($"Alias \"{trimmed}\" matches the actor's current Japanese kana.");
        }

        // 3. Check if alias collides with another actor's primary/canonical names
        var trimmedUpper = trimmed.ToUpper();
        var otherActorPrimary = await db.Actors
            .AsNoTracking()
            .Where(a => a.Id != actorId && a.FirstName != null)
            .FirstOrDefaultAsync(a =>
                (a.LastName == null && a.FirstName != null && a.FirstName.ToUpper() == trimmedUpper)
                || (a.LastName != null && (a.LastName + " " + a.FirstName).ToUpper() == trimmedUpper)
                || (a.LastName != null && (a.FirstName + " " + a.LastName).ToUpper() == trimmedUpper)
                || (a.JapaneseNameKanji != null && a.JapaneseNameKanji.ToUpper() == trimmedUpper)
                || (a.JapaneseNameKana != null && a.JapaneseNameKana.ToUpper() == trimmedUpper)
                || (a.R18DevName != null && a.R18DevName.ToUpper() == trimmedUpper), ct);

        if (otherActorPrimary is not null)
        {
            return ActorOperationResult.Fail($"\"{trimmed}\" is already the primary name of actor \"{otherActorPrimary.DisplayName}\". If these are the same person, use the Merge tool instead.");
        }

        // 4. Check if alias is already assigned to another actor
        var otherAlias = await db.ActorAliases
            .Include(al => al.Actor)
            .AsNoTracking()
            .FirstOrDefaultAsync(al => al.ActorId != actorId && al.Name.ToUpper() == trimmedUpper, ct);

        if (otherAlias is not null)
        {
            var otherName = otherAlias.Actor?.DisplayName ?? "another actor";
            return ActorOperationResult.Fail($"Alias \"{trimmed}\" is already assigned to actor \"{otherName}\". If these are the same person, use the Merge tool instead.");
        }

        // 5. Add alias
        var newAlias = new ActorAlias
        {
            ActorId = actor.Id,
            Name = trimmed,
            CreatedAt = DateTime.UtcNow
        };
        actor.Aliases.Add(newAlias);
        db.ActorAliases.Add(newAlias);
        await db.SaveChangesAsync(ct);

        // 6. Synchronize movie associations so movie matching reflects the new alias
        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);

        return ActorOperationResult.Ok(actor);
    }

    public async Task<ActorOperationResult> RemoveAliasAsync(int actorId, int aliasId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actor = await db.Actors
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == actorId, ct);
        if (actor is null)
        {
            return ActorOperationResult.Fail("Actor not found.");
        }

        var alias = actor.Aliases.FirstOrDefault(a => a.Id == aliasId);
        if (alias is null)
        {
            return ActorOperationResult.Fail("Alias not found.");
        }

        actor.Aliases.Remove(alias);
        db.ActorAliases.Remove(alias);
        await db.SaveChangesAsync(ct);

        // Synchronize movie associations so unlinked movies reflect the removed alias
        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);

        return ActorOperationResult.Ok(actor);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Actors.FindAsync([id], ct);
        if (tracked is not null)
        {
            if (actorPhotoService is not null)
            {
                await actorPhotoService.DeleteAllForActorAsync(id, ct);
            }
            if (imageCacheService is not null)
            {
                await imageCacheService.DeleteAllForActorAsync(id, ct);
            }
            db.Actors.Remove(tracked);
            await db.SaveChangesAsync(ct);

            // The MovieActor links to this actor cascade-delete automatically (FK), but that
            // doesn't recompute HasUnmatchedActors/UnmatchedActorNames on the movies that
            // referenced it — those still list the deleted actor's name in MetaActresses, which
            // is now unmatched. Same re-sync MergeAsync/RenameAsync already do after their own
            // actor-affecting changes.
            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> HasImageAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actorExists = await db.Actors.AnyAsync(a => a.Id == actorId, ct);
        if (!actorExists) return false;

        return await db.ActorImages.AnyAsync(ai => ai.ActorId == actorId, ct);
    }

    public async Task<ActorPortraitInfo> GetPortraitInfoAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var images = await db.ActorImages
            .AsNoTracking()
            .Where(a => a.ActorId == actorId)
            .ToListAsync(ct);

        if (images.Count == 0)
        {
            return new ActorPortraitInfo(false, false, false);
        }

        var hasCropped = images.Any(a => a.CropWidth != null);
        var sourced = images.FirstOrDefault(a => a.SourceStorageId != null);
        var hasSource = false;
        if (sourced?.SourceStorageId is { } sourceStorageId && dataStore is not null)
        {
            hasSource = await dataStore.ExistsAsync(sourceStorageId, sourced.SourceExtension, ct);
        }

        return new ActorPortraitInfo(
            HasImage: true,
            HasCroppedImage: hasCropped,
            HasSourceImage: hasSource,
            ImageVersion: images.Max(a => a.UpdatedAt).Ticks);
    }

    public async Task<IReadOnlyList<ActorMergeCandidate>> GetMergeCandidatesAsync(int sourceActorId, string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var source = await db.Actors.AsNoTracking().Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == sourceActorId, ct);
        if (source is null) return [];

        var sourceTokens = GetNameTokens(source);

        var query = db.Actors
            .AsNoTracking()
            .Where(a => a.Id != sourceActorId && a.FirstName != null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchUpper = search.Trim().ToUpper();
            query = query.Where(a =>
                (a.FirstName != null && a.FirstName.ToUpper().Contains(searchUpper))
                || (a.LastName != null && a.LastName.ToUpper().Contains(searchUpper))
                || (a.JapaneseNameKanji != null && a.JapaneseNameKanji.ToUpper().Contains(searchUpper))
                || (a.JapaneseNameKana != null && a.JapaneseNameKana.ToUpper().Contains(searchUpper))
                || (a.R18DevName != null && a.R18DevName.ToUpper().Contains(searchUpper))
                || a.Aliases.Any(al => al.Name.ToUpper().Contains(searchUpper)));
        }

        var actors = await query
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.JapaneseNameKanji,
                a.JapaneseNameKana,
                MovieCount = a.MovieActors.Count,
                HasCustomOrCachedImage = db.ActorImages.Any(ai => ai.ActorId == a.Id)
            })
            .OrderBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Take(50)
            .ToListAsync(ct);

        return actors.Select(a =>
        {
            var displayName = ActorDisplayName.Format(a.FirstName!, a.LastName);
            var hasImage = a.HasCustomOrCachedImage;
            var isSuggested = IsSuggestedMatch(sourceTokens, a.FirstName!, a.LastName, a.JapaneseNameKanji, a.JapaneseNameKana);
            return new ActorMergeCandidate(
                a.Id,
                displayName,
                a.JapaneseNameKanji,
                a.JapaneseNameKana,
                a.MovieCount,
                hasImage,
                isSuggested);
        })
        .OrderByDescending(c => c.IsSuggested)
        .ThenByDescending(c => c.MovieCount)
        .ThenBy(c => c.DisplayName)
        .ToList();
    }

    public async Task<IReadOnlyList<ActorSearchItem>> SearchActorsAsync(string search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search)) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var searchUpper = search.Trim().ToUpper();
        var actors = await db.Actors
            .AsNoTracking()
            .Where(a => a.FirstName != null
                && ((a.FirstName != null && a.FirstName.ToUpper().Contains(searchUpper))
                    || (a.LastName != null && a.LastName.ToUpper().Contains(searchUpper))
                    || (a.JapaneseNameKanji != null && a.JapaneseNameKanji.ToUpper().Contains(searchUpper))
                    || (a.JapaneseNameKana != null && a.JapaneseNameKana.ToUpper().Contains(searchUpper))
                    || (a.R18DevName != null && a.R18DevName.ToUpper().Contains(searchUpper))
                    || a.Aliases.Any(al => al.Name.ToUpper().Contains(searchUpper))))
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                HasImage = db.ActorImages.Any(ai => ai.ActorId == a.Id && ai.Variant == "thumb")
            })
            .OrderBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Take(10)
            .ToListAsync(ct);

        return actors
            .Select(a => new ActorSearchItem(a.Id, ActorDisplayName.Format(a.FirstName!, a.LastName), a.HasImage))
            .ToList();
    }

    public async Task<ActorOperationResult> MergeAsync(
        int sourceActorId,
        int targetActorId,
        bool updateNfoOnDisk = false,
        CancellationToken ct = default)
    {
        if (sourceActorId == targetActorId)
        {
            return ActorOperationResult.Fail("Cannot merge an actor into themselves.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var source = await db.Actors
            .Include(a => a.Aliases)
            .Include(a => a.CupSizePeriods)
            .FirstOrDefaultAsync(a => a.Id == sourceActorId, ct);
        var target = await db.Actors
            .Include(a => a.Aliases)
            .Include(a => a.CupSizePeriods)
            .FirstOrDefaultAsync(a => a.Id == targetActorId, ct);

        if (source is null) return ActorOperationResult.Fail("Source actor not found.");
        if (target is null) return ActorOperationResult.Fail("Target actor not found.");

        // 1. Consolidate metadata into target where target fields are missing
        if (string.IsNullOrWhiteSpace(target.JapaneseNameKanji) && !string.IsNullOrWhiteSpace(source.JapaneseNameKanji))
        {
            target.JapaneseNameKanji = source.JapaneseNameKanji.Trim();
        }
        if (string.IsNullOrWhiteSpace(target.JapaneseNameKana) && !string.IsNullOrWhiteSpace(source.JapaneseNameKana))
        {
            target.JapaneseNameKana = source.JapaneseNameKana.Trim();
        }
        if (string.IsNullOrWhiteSpace(target.R18DevName) && !string.IsNullOrWhiteSpace(source.R18DevName))
        {
            target.R18DevName = source.R18DevName.Trim();
        }
        target.R18DevId ??= source.R18DevId;
        if (string.IsNullOrWhiteSpace(target.JellyfinPersonId) && !string.IsNullOrWhiteSpace(source.JellyfinPersonId))
        {
            target.JellyfinPersonId = source.JellyfinPersonId.Trim();
        }
        if (!target.IsFavorite && source.IsFavorite)
        {
            target.IsFavorite = true;
            target.FavoritedAt = source.FavoritedAt ?? DateTime.UtcNow;
        }
        target.HeightCm ??= source.HeightCm;
        if (string.IsNullOrWhiteSpace(target.CupSize) && ActorPhysicalAttributesHelper.StandardCupSizeOrNull(source.CupSize) is { } sourceCupSize)
        {
            target.CupSize = sourceCupSize;
        }
        if (target.CupSizePeriods.Count == 0)
        {
            foreach (var period in source.CupSizePeriods)
            {
                target.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = period.EffectiveFrom, CupSize = period.CupSize });
            }
        }
        target.Bust ??= source.Bust;
        target.Waist ??= source.Waist;
        target.Hips ??= source.Hips;
        if (ActorPhysicalAttributesHelper.IsAllowedBirthDate(source.BirthDate))
        {
            target.BirthDate ??= source.BirthDate;
        }
        target.IsRetired |= source.IsRetired;

        // 2. Consolidate and create aliases on target
        var targetKnownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void RegisterTargetName(string? n)
        {
            if (!string.IsNullOrWhiteSpace(n)) targetKnownNames.Add(n.Trim());
        }
        RegisterTargetName(target.DisplayName);
        if (!string.IsNullOrWhiteSpace(target.LastName))
        {
            RegisterTargetName($"{target.FirstName} {target.LastName}");
        }
        RegisterTargetName(target.JapaneseNameKanji);
        RegisterTargetName(target.JapaneseNameKana);
        RegisterTargetName(target.R18DevName);
        foreach (var a in target.Aliases) RegisterTargetName(a.Name);

        var sourceCandidateNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.DisplayName)) sourceCandidateNames.Add(source.DisplayName);
        if (!string.IsNullOrWhiteSpace(source.LastName))
        {
            sourceCandidateNames.Add($"{source.FirstName} {source.LastName}".Trim());
        }
        if (!string.IsNullOrWhiteSpace(source.JapaneseNameKanji)) sourceCandidateNames.Add(source.JapaneseNameKanji);
        if (!string.IsNullOrWhiteSpace(source.JapaneseNameKana)) sourceCandidateNames.Add(source.JapaneseNameKana);
        if (!string.IsNullOrWhiteSpace(source.R18DevName)) sourceCandidateNames.Add(source.R18DevName);
        foreach (var a in source.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(a.Name)) sourceCandidateNames.Add(a.Name);
        }

        foreach (var name in sourceCandidateNames)
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0 && !targetKnownNames.Contains(trimmed))
            {
                var newAlias = new ActorAlias
                {
                    ActorId = target.Id,
                    Name = trimmed,
                    CreatedAt = DateTime.UtcNow
                };
                target.Aliases.Add(newAlias);
                db.ActorAliases.Add(newAlias);
                targetKnownNames.Add(trimmed);
            }
        }

        // 3. Re-point MovieActor links
        var targetMovieIds = (await db.MovieActors
            .Where(m => m.ActorId == target.Id)
            .Select(m => m.MovieId)
            .ToListAsync(ct))
            .ToHashSet();

        var sourceMovieLinks = await db.MovieActors
            .Where(m => m.ActorId == source.Id)
            .ToListAsync(ct);

        foreach (var link in sourceMovieLinks)
        {
            db.MovieActors.Remove(link);
            if (!targetMovieIds.Contains(link.MovieId))
            {
                db.MovieActors.Add(new MovieActor { MovieId = link.MovieId, ActorId = target.Id });
                targetMovieIds.Add(link.MovieId);
            }
        }

        // Scene actors hang off the source's cast links and would cascade away
        // with them; move them onto the target's link for the same movie.
        var targetSceneIds = (await db.SceneActors
            .Where(sa => sa.ActorId == target.Id)
            .Select(sa => sa.SceneId)
            .ToListAsync(ct))
            .ToHashSet();
        var sourceSceneLinks = await db.SceneActors
            .Where(sa => sa.ActorId == source.Id)
            .ToListAsync(ct);
        foreach (var link in sourceSceneLinks)
        {
            db.SceneActors.Remove(link);
            if (targetSceneIds.Add(link.SceneId))
            {
                db.SceneActors.Add(new SceneActor { SceneId = link.SceneId, MovieId = link.MovieId, ActorId = target.Id });
            }
        }

        // Apex actors hang off the cast links the same way.
        var targetApexIds = (await db.ApexActors
            .Where(aa => aa.ActorId == target.Id)
            .Select(aa => aa.ApexId)
            .ToListAsync(ct))
            .ToHashSet();
        var sourceApexLinks = await db.ApexActors
            .Where(aa => aa.ActorId == source.Id)
            .ToListAsync(ct);
        foreach (var link in sourceApexLinks)
        {
            db.ApexActors.Remove(link);
            if (targetApexIds.Add(link.ApexId))
            {
                db.ApexActors.Add(new ApexActor { ApexId = link.ApexId, MovieId = link.MovieId, ActorId = target.Id });
            }
        }

        // And so do highlight actors.
        var targetHighlightIds = (await db.HighlightActors
            .Where(ha => ha.ActorId == target.Id)
            .Select(ha => ha.HighlightId)
            .ToListAsync(ct))
            .ToHashSet();
        var sourceHighlightLinks = await db.HighlightActors
            .Where(ha => ha.ActorId == source.Id)
            .ToListAsync(ct);
        foreach (var link in sourceHighlightLinks)
        {
            db.HighlightActors.Remove(link);
            if (targetHighlightIds.Add(link.HighlightId))
            {
                db.HighlightActors.Add(new HighlightActor { HighlightId = link.HighlightId, MovieId = link.MovieId, ActorId = target.Id });
            }
        }

        // Actor tags hang off the cast links too; the target keeps its own and gains the source's.
        await RepointActorTagsAsync(db, source.Id, target.Id, ct);

        // 4. Transfer cached and custom images
        if (imageCacheService is not null)
        {
            await imageCacheService.TransferImagesAsync(source.Id, target.Id, ct);
        }

        // 5. Remove source actor (ActorAliases cascades delete)
        db.Actors.Remove(source);

        await db.SaveChangesAsync(ct);

        // 6. Synchronize movie-actor associations
        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);

        // 7. Synchronize actor name in linked movie .nfo files if requested
        if (updateNfoOnDisk && nfoSyncService is not null)
        {
            await nfoSyncService.SyncActorAsync(target.Id, sourceCandidateNames, ct);
        }

        return ActorOperationResult.Ok(target);
    }

    public async Task<IReadOnlyList<ActorCardModel>> GetActorCardsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var raw = await db.Actors
            .AsNoTracking()
            .OrderBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Select(a => new
            {
                a.Id,
                FirstName = a.FirstName ?? string.Empty,
                a.LastName,
                a.IsFavorite,
                a.CreatedAt,
                a.JapaneseNameKanji,
                a.JapaneseNameKana,
                a.JellyfinPersonId,
                MovieCount = a.MovieActors.Count,
                OwnedCount = a.MovieActors.Count(ma => ma.Movie.Status == MovieStatus.Got),
                ImageCount = db.ActorPhotos.Count(photo => photo.ActorId == a.Id),
                ImageUpdatedAt = db.ActorImages
                    .Where(ai => ai.ActorId == a.Id && ai.Variant == "thumb")
                    .Select(ai => (DateTime?)ai.UpdatedAt)
                    .FirstOrDefault(),
                Aliases = a.Aliases.Select(al => al.Name).ToList(),
                a.CupSize,
                a.BirthDate,
                a.IsRetired,
                a.HeightCm
            })
            .ToListAsync(ct);

        return raw.Select(a => new ActorCardModel(
            a.Id,
            ActorDisplayName.Format(a.FirstName, a.LastName),
            a.MovieCount,
            a.ImageUpdatedAt.HasValue,
            a.IsFavorite,
            a.CreatedAt,
            a.Aliases,
            a.JapaneseNameKanji,
            a.JapaneseNameKana,
            a.ImageUpdatedAt?.Ticks,
            !string.IsNullOrWhiteSpace(a.JellyfinPersonId),
            a.ImageCount,
            a.CupSize,
            a.BirthDate,
            a.IsRetired,
            a.HeightCm,
            a.OwnedCount
        )).ToList();
    }

    public async Task<bool> IsJellyfinEnabledAsync(CancellationToken ct = default)
    {
        if (jellyfinClient is null) return false;
        return await jellyfinClient.IsEnabledAsync(ct);
    }

    public async Task<bool> ToggleFavoriteAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors.FindAsync([actorId], ct);
        if (actor is null) return false;

        actor.IsFavorite = !actor.IsFavorite;
        actor.FavoritedAt = actor.IsFavorite ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return actor.IsFavorite;
    }

    public async Task<ActorEnrichmentResult> RefreshMetadataAsync(int actorId, CancellationToken ct = default)
    {
        if (actorEnrichmentService is null)
        {
            return ActorEnrichmentResult.Fail("Actor enrichment service is not configured.");
        }

        return await actorEnrichmentService.EnrichActorAsync(
            actorId,
            options: new ActorEnrichmentOptions { Source = LocalActorMetadataSource.SourceNameConstant },
            ct);
    }

    private static HashSet<string> GetNameTokens(Actor actor)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddTokens(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return;
            tokens.Add(str.Trim());
            var parts = str.Split([' ', ',', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                if (p.Length > 1) tokens.Add(p);
            }
        }

        AddTokens(actor.FirstName);
        AddTokens(actor.LastName);
        AddTokens(actor.JapaneseNameKanji);
        AddTokens(actor.JapaneseNameKana);
        AddTokens(actor.R18DevName);
        foreach (var al in actor.Aliases) AddTokens(al.Name);

        return tokens;
    }

    private static bool IsSuggestedMatch(HashSet<string> sourceTokens, string firstName, string? lastName, string? kanji, string? kana)
    {
        if (sourceTokens.Contains(firstName)) return true;
        if (!string.IsNullOrWhiteSpace(lastName) && sourceTokens.Contains(lastName)) return true;
        if (!string.IsNullOrWhiteSpace(kanji))
        {
            if (sourceTokens.Contains(kanji)) return true;
            var compact = kanji.Replace(" ", "");
            if (sourceTokens.Any(t => t.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase))) return true;
        }
        if (!string.IsNullOrWhiteSpace(kana))
        {
            if (sourceTokens.Contains(kana)) return true;
            var compact = kana.Replace(" ", "");
            if (sourceTokens.Any(t => t.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    public async Task<Actor?> FindMatchingActorAsync(string name, string? japaneseName = null, IReadOnlyList<string>? aliases = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (!string.IsNullOrWhiteSpace(japaneseName))
        {
            var compact = japaneseName.Replace(" ", "");
            var kanjiMatch = await db.Actors
                .AsNoTracking()
                .Include(a => a.Aliases)
                .FirstOrDefaultAsync(a => a.JapaneseNameKanji != null && a.JapaneseNameKanji.Replace(" ", "").ToLower() == compact.ToLower(), ct);
            if (kanjiMatch is not null) return kanjiMatch;
        }

        var cleanName = name.Trim();
        // Only the names matching needs, not whole actor rows; the match is then loaded by ID.
        var identities = await db.Actors
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FirstName, a.LastName, Aliases = a.Aliases.Select(al => al.Name).ToList() })
            .ToListAsync(ct);

        var matched = identities.FirstOrDefault(actor =>
        {
            var displayName = ActorDisplayName.Format(actor.FirstName ?? string.Empty, actor.LastName);
            if (string.Equals(displayName, cleanName, StringComparison.OrdinalIgnoreCase)) return true;

            if (!string.IsNullOrWhiteSpace(actor.LastName))
            {
                var reversed = $"{actor.FirstName} {actor.LastName}".Trim();
                if (string.Equals(reversed, cleanName, StringComparison.OrdinalIgnoreCase)) return true;
            }

            if (actor.Aliases.Any(al => string.Equals(al, cleanName, StringComparison.OrdinalIgnoreCase))) return true;

            return aliases is not null && aliases.Any(alias =>
                string.Equals(displayName, alias, StringComparison.OrdinalIgnoreCase)
                || actor.Aliases.Any(al => string.Equals(al, alias, StringComparison.OrdinalIgnoreCase)));
        });

        return matched is null
            ? null
            : await db.Actors.AsNoTracking().Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == matched.Id, ct);
    }

    public async Task<ActorOperationResult> ImportOrEnrichFromWarashiAsync(
        WarashiPerformerDetail detail,
        int? targetActorId = null,
        WarashiImportOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new WarashiImportOptions();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        Actor? actor = null;
        if (targetActorId.HasValue)
        {
            actor = await db.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == targetActorId.Value, ct);
            if (actor is null) return ActorOperationResult.Fail("Target actor not found.");
        }
        else
        {
            var matched = await FindMatchingActorAsync(detail.Name, detail.JapaneseName, detail.Aliases, ct);
            if (matched is not null)
            {
                actor = await db.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == matched.Id, ct);
            }
        }

        if (actor is not null)
        {
            ActorImportApplier.ApplyToExisting(db, actor, ToWarashiData(detail), ToWarashiSelection(options));

            await db.SaveChangesAsync(ct);

            string? photoWarning = null;
            if (options.ImportPhotos)
            {
                photoWarning = await ImportWarashiPhotosAsync(db, actor.Id, detail, ct);
            }

            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
            await db.SaveChangesAsync(ct);
            return photoWarning is null ? ActorOperationResult.Ok(actor) : ActorOperationResult.OkWithWarning(actor, photoWarning);
        }

        string firstName;
        string? lastName = null;

        if (!string.IsNullOrWhiteSpace(detail.GivenName))
        {
            firstName = NormalizeNameCasing(detail.GivenName);
            lastName = !string.IsNullOrWhiteSpace(detail.FamilyName) ? NormalizeNameCasing(detail.FamilyName) : null;
        }
        else
        {
            var parts = detail.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                firstName = NormalizeNameCasing(parts[0]);
                lastName = NormalizeNameCasing(string.Join(" ", parts.Skip(1)));
            }
            else
            {
                firstName = NormalizeNameCasing(detail.Name);
            }
        }

        var newActor = ActorImportApplier.CreateNew(firstName, lastName, ToWarashiData(detail), ToWarashiSelection(options));

        db.Actors.Add(newActor);
        await db.SaveChangesAsync(ct);

        string? newActorPhotoWarning = null;
        if (options.ImportPhotos)
        {
            newActorPhotoWarning = await ImportWarashiPhotosAsync(db, newActor.Id, detail, ct);
        }

        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);

        return newActorPhotoWarning is null ? ActorOperationResult.Ok(newActor) : ActorOperationResult.OkWithWarning(newActor, newActorPhotoWarning);
    }

    public async Task<ActorOperationResult> ImportOrEnrichFromMinnanoAvAsync(
        MinnanoAvPerformerDetail detail,
        int? targetActorId = null,
        MinnanoAvImportOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MinnanoAvImportOptions();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        Actor? actor = null;
        if (targetActorId.HasValue)
        {
            actor = await db.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == targetActorId.Value, ct);
            if (actor is null) return ActorOperationResult.Fail("Target actor not found.");
        }
        else
        {
            var matched = await FindMatchingActorAsync(detail.Romaji ?? detail.Name, detail.Name, detail.Aliases, ct);
            if (matched is not null)
            {
                actor = await db.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == matched.Id, ct);
            }
        }

        if (actor is not null)
        {
            ActorImportApplier.ApplyToExisting(db, actor, ToMinnanoAvData(detail), ToMinnanoAvSelection(options));

            await db.SaveChangesAsync(ct);
            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
            await db.SaveChangesAsync(ct);
            return ActorOperationResult.Ok(actor);
        }

        // minnano-av.com's romaji reading is family-name-first (e.g. "Tohno Miho" for 通野未帆),
        // matching Javbuddy's own LastName-FirstName DisplayName convention directly — unlike
        // WAPdB, where the romanized name is given-name-first.
        string firstName;
        string? lastName = null;

        var romajiParts = (detail.Romaji ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (romajiParts.Length >= 2)
        {
            lastName = NormalizeNameCasing(romajiParts[0]);
            firstName = NormalizeNameCasing(string.Join(" ", romajiParts.Skip(1)));
        }
        else if (romajiParts.Length == 1)
        {
            firstName = NormalizeNameCasing(romajiParts[0]);
        }
        else
        {
            firstName = detail.Name;
        }

        var newActor = ActorImportApplier.CreateNew(firstName, lastName, ToMinnanoAvData(detail), ToMinnanoAvSelection(options));

        db.Actors.Add(newActor);
        await db.SaveChangesAsync(ct);
        await MovieActorAssociation.SynchronizeAllAsync(db, ct);
        await db.SaveChangesAsync(ct);

        return ActorOperationResult.Ok(newActor);
    }

    /// <summary>Downloads and saves the performer's WAPdB photos. Returns null on full success (or
    /// when there was nothing to import), or a warning message describing what failed — the caller
    /// still treats the overall enrichment as successful since text-field updates already saved.</summary>
    private async Task<string?> ImportWarashiPhotosAsync(
        AppDbContext db,
        int actorId,
        WarashiPerformerDetail detail,
        CancellationToken ct)
    {
        var photoUrls = new List<string>();
        if (!string.IsNullOrWhiteSpace(detail.MainPhotoUrl))
        {
            photoUrls.Add(detail.MainPhotoUrl);
        }
        foreach (var url in detail.AdditionalPhotoUrls)
        {
            if (!string.IsNullOrWhiteSpace(url) && !photoUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                photoUrls.Add(url);
            }
        }

        if (photoUrls.Count == 0) return null;

        var downloadedBytes = new List<byte[]>();
        foreach (var url in photoUrls)
        {
            byte[]? bytes = null;
            if (warashiClient != null)
            {
                bytes = await warashiClient.DownloadImageAsync(url, ct);
            }
            else if (imageCacheService != null)
            {
                var dl = await imageCacheService.DownloadImageFromUrlAsync(url, ct);
                if (dl.Success && dl.Bytes is { Length: > 0 })
                {
                    bytes = dl.Bytes;
                }
            }

            if (bytes is { Length: > 0 })
            {
                downloadedBytes.Add(bytes);
            }
        }

        if (downloadedBytes.Count == 0) return "No photos could be downloaded from WAPdB.";

        // If the actor does not currently have a portrait, set the first image as their portrait
        var hasPortrait = await db.ActorImages.AnyAsync(ai => ai.ActorId == actorId, ct);
        if (imageCacheService != null && !hasPortrait)
        {
            try
            {
                await imageCacheService.SaveCustomImageAsync(actorId, downloadedBytes[0], cropRect: null, ct);
            }
            catch
            {
                // Ignore portrait save failure
            }
        }

        // Add photos to the "WAPdB" album
        if (actorPhotoService != null)
        {
            var album = await db.ActorAlbums.FirstOrDefaultAsync(a => a.ActorId == actorId && a.Name == "WAPdB", ct);
            int albumId;
            if (album is not null)
            {
                albumId = album.Id;
            }
            else
            {
                var newAlbum = new ActorAlbum { ActorId = actorId, Name = "WAPdB" };
                db.ActorAlbums.Add(newAlbum);
                await db.SaveChangesAsync(ct);
                albumId = newAlbum.Id;
            }

            var uploadResult = await actorPhotoService.UploadAsync(actorId, downloadedBytes, albumId, ct);
            if (!uploadResult.Success)
            {
                return uploadResult.ErrorMessage ?? "Failed to save photos to the WAPdB album.";
            }
        }

        return null;
    }

    private static ImportedActorData ToWarashiData(WarashiPerformerDetail d) => new(
        d.HeightCm, d.CupSize, d.Bust, d.Waist, d.Hips, d.BirthDate, d.IsRetired, d.JapaneseName, null, d.Aliases);

    private static ImportSelection ToWarashiSelection(WarashiImportOptions o) => new(
        o.ImportJapaneseName, o.ImportHeight, o.ImportCupSize, o.ImportMeasurements, o.ImportBirthDate, o.ImportRetiredStatus, o.ImportAliases);

    private static ImportedActorData ToMinnanoAvData(MinnanoAvPerformerDetail d) => new(
        d.HeightCm, d.CupSize, d.Bust, d.Waist, d.Hips, d.BirthDate, d.IsRetired, d.Name, d.Kana, d.Aliases);

    private static ImportSelection ToMinnanoAvSelection(MinnanoAvImportOptions o) => new(
        o.ImportJapaneseName, o.ImportHeight, o.ImportCupSize, o.ImportMeasurements, o.ImportBirthDate, o.ImportRetiredStatus, o.ImportAliases);

    private static string NormalizeNameCasing(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length <= 1) return trimmed.ToUpperInvariant();

        if (trimmed.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        }

        return trimmed;
    }

    private static async Task RepointActorTagsAsync(AppDbContext db, int sourceId, int targetId, CancellationToken ct)
    {
        var targetMovie = (await db.MovieActorTags.Where(t => t.ActorId == targetId).Select(t => new { t.MovieId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.MovieId, t.TagId)).ToHashSet();
        foreach (var row in await db.MovieActorTags.Where(t => t.ActorId == sourceId).ToListAsync(ct))
        {
            db.MovieActorTags.Remove(row);
            if (targetMovie.Add((row.MovieId, row.TagId)))
            {
                db.MovieActorTags.Add(new MovieActorTag { MovieId = row.MovieId, ActorId = targetId, TagId = row.TagId });
            }
        }

        var targetScene = (await db.SceneActorTags.Where(t => t.ActorId == targetId).Select(t => new { t.SceneId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.SceneId, t.TagId)).ToHashSet();
        foreach (var row in await db.SceneActorTags.Where(t => t.ActorId == sourceId).ToListAsync(ct))
        {
            db.SceneActorTags.Remove(row);
            if (targetScene.Add((row.SceneId, row.TagId)))
            {
                db.SceneActorTags.Add(new SceneActorTag { SceneId = row.SceneId, MovieId = row.MovieId, ActorId = targetId, TagId = row.TagId });
            }
        }

        var targetHighlight = (await db.HighlightActorTags.Where(t => t.ActorId == targetId).Select(t => new { t.HighlightId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.HighlightId, t.TagId)).ToHashSet();
        foreach (var row in await db.HighlightActorTags.Where(t => t.ActorId == sourceId).ToListAsync(ct))
        {
            db.HighlightActorTags.Remove(row);
            if (targetHighlight.Add((row.HighlightId, row.TagId)))
            {
                db.HighlightActorTags.Add(new HighlightActorTag { HighlightId = row.HighlightId, MovieId = row.MovieId, ActorId = targetId, TagId = row.TagId });
            }
        }

        var targetApex = (await db.ApexActorTags.Where(t => t.ActorId == targetId).Select(t => new { t.ApexId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.ApexId, t.TagId)).ToHashSet();
        foreach (var row in await db.ApexActorTags.Where(t => t.ActorId == sourceId).ToListAsync(ct))
        {
            db.ApexActorTags.Remove(row);
            if (targetApex.Add((row.ApexId, row.TagId)))
            {
                db.ApexActorTags.Add(new ApexActorTag { ApexId = row.ApexId, MovieId = row.MovieId, ActorId = targetId, TagId = row.TagId });
            }
        }
    }
}
