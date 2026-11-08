using System.Runtime.CompilerServices;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Javbuddy.Data;

/// <summary>Encapsulates atomic SQLite upserts for image cache metadata tables.
/// EF Core lacks a native atomic upsert primitive; SQLite's "INSERT ... ON CONFLICT DO UPDATE"
/// prevents concurrent cache misses from racing on the UNIQUE constraint and polluting application
/// logs with DbUpdateException errors. The statement is built from the EF model's metadata (table and
/// column names, so HasColumnName/ToTable are honoured) and the per-entity <see cref="PairSpec"/> below,
/// and every value is a parameter. Each call writes the "thumb" and "full" rows of one image in one statement.</summary>
public static class AppDbContextImageCacheExtensions
{
    public const string VariantThumb = "thumb";
    public const string VariantFull = "full";

    /// <summary>What an upsert writes for one entity. A pair's two rows differ only in <c>Variant</c> and
    /// <c>StorageId</c>; <see cref="SharedProperties"/> are the other columns, written identically to both.
    /// <see cref="ConflictKey"/> must be the entity's unique index (inserted, but not changed on conflict).
    /// <see cref="UnwrittenProperties"/> are mapped columns this upsert deliberately leaves alone (set by
    /// other code, or database-generated), so a column added to the entity has to be classified here or in
    /// SharedProperties — the column-coverage test fails otherwise.</summary>
    private sealed record PairSpec(
        Type Entity,
        IReadOnlyList<string> ConflictKey,
        IReadOnlyList<string> SharedProperties,
        IReadOnlyList<string> UnwrittenProperties);

    private static readonly PairSpec ActorImageSpec = new(
        typeof(ActorImage),
        ConflictKey: [nameof(ActorImage.ActorId), nameof(ActorImage.Variant)],
        SharedProperties:
        [
            nameof(ActorImage.ActorId),
            nameof(ActorImage.SourceMovieCode),
            nameof(ActorImage.SourceStorageId),
            nameof(ActorImage.SourceExtension),
            nameof(ActorImage.CropX),
            nameof(ActorImage.CropY),
            nameof(ActorImage.CropWidth),
            nameof(ActorImage.CropHeight),
            nameof(ActorImage.SourceLength),
            nameof(ActorImage.SourceLastWriteUtc),
            nameof(ActorImage.SourceUrl),
            nameof(ActorImage.UpdatedAt),
        ],
        UnwrittenProperties: [nameof(ActorImage.Id)]);

    private static readonly PairSpec CachedImageSpec = new(
        typeof(CachedImage),
        ConflictKey: [nameof(CachedImage.Code), nameof(CachedImage.Role), nameof(CachedImage.Index), nameof(CachedImage.Variant)],
        SharedProperties:
        [
            nameof(CachedImage.Code),
            nameof(CachedImage.Role),
            nameof(CachedImage.Index),
            nameof(CachedImage.SourceLength),
            nameof(CachedImage.SourceLastWriteUtc),
            nameof(CachedImage.SourceUrl),
            nameof(CachedImage.UpdatedAt),
        ],
        // The scene/highlight/DeoVR freshness columns are maintained by their own services, not by this upsert.
        UnwrittenProperties:
        [
            nameof(CachedImage.Id),
            nameof(CachedImage.SceneStartMs),
            nameof(CachedImage.SceneEndMs),
            nameof(CachedImage.SourceIdentity),
        ]);

    /// <summary>The entity properties the upsert for <paramref name="entity"/> writes (inserts, and updates on conflict
    /// unless they are in the conflict key). Exposed so a test can check it against the EF model.</summary>
    public static IReadOnlyList<string> GetWrittenProperties(Type entity) =>
        [nameof(ActorImage.Variant), nameof(ActorImage.StorageId), .. SpecFor(entity).SharedProperties];

    public static IReadOnlyList<string> GetConflictKey(Type entity) => SpecFor(entity).ConflictKey;

    public static IReadOnlyList<string> GetUnwrittenProperties(Type entity) => SpecFor(entity).UnwrittenProperties;

    private static PairSpec SpecFor(Type entity) =>
        entity == typeof(ActorImage) ? ActorImageSpec
        : entity == typeof(CachedImage) ? CachedImageSpec
        : throw new ArgumentException($"No image cache upsert is defined for {entity.Name}.", nameof(entity));

    /// <summary>Builds the two-row upsert. <paramref name="shared"/> must hold exactly the spec's SharedProperties.</summary>
    private static FormattableString BuildPairUpsert(
        AppDbContext db,
        PairSpec spec,
        IReadOnlyDictionary<string, object?> shared,
        Guid thumbStorageId,
        Guid fullStorageId)
    {
        var entityType = db.Model.FindEntityType(spec.Entity)
            ?? throw new InvalidOperationException($"{spec.Entity.Name} is not part of the model.");
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"{spec.Entity.Name} is not mapped to a table.");
        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

        if (shared.Count != spec.SharedProperties.Count || spec.SharedProperties.Any(p => !shared.ContainsKey(p)))
        {
            throw new ArgumentException(
                $"Values for {spec.Entity.Name} must be exactly: {string.Join(", ", spec.SharedProperties)}.", nameof(shared));
        }

        string Column(string property) => "\"" + (entityType.FindProperty(property)?.GetColumnName(store)
            ?? throw new InvalidOperationException($"{spec.Entity.Name}.{property} is not a mapped column.")) + "\"";

        // Parameter layout: {0} thumb variant, {1} thumb storage id, {2..} shared values, then full variant and storage id.
        var sharedCount = spec.SharedProperties.Count;
        var fullVariantIndex = 2 + sharedCount;
        var args = new object?[sharedCount + 4];
        args[0] = VariantThumb;
        args[1] = thumbStorageId;
        for (var i = 0; i < sharedCount; i++)
        {
            args[2 + i] = shared[spec.SharedProperties[i]];
        }

        args[fullVariantIndex] = VariantFull;
        args[fullVariantIndex + 1] = fullStorageId;

        var sharedPlaceholders = string.Join(", ", Enumerable.Range(2, sharedCount).Select(i => $"{{{i}}}"));
        var columns = new[] { nameof(ActorImage.Variant), nameof(ActorImage.StorageId) }
            .Concat(spec.SharedProperties)
            .ToList();
        var updates = columns
            .Where(c => !spec.ConflictKey.Contains(c))
            .Select(c => $"{Column(c)} = excluded.{Column(c)}");

        var fullRowStart = $"{{{fullVariantIndex}}}, {{{fullVariantIndex + 1}}}";
        var format = $$"""
            INSERT INTO "{{table}}" ({{string.Join(", ", columns.Select(Column))}})
            VALUES
                ({0}, {1}, {{sharedPlaceholders}}),
                ({{fullRowStart}}, {{sharedPlaceholders}})
            ON CONFLICT ({{string.Join(", ", spec.ConflictKey.Select(Column))}}) DO UPDATE SET {{string.Join(", ", updates)}}
            """;

        return FormattableStringFactory.Create(format, args);
    }

    public static async Task UpsertActorImagePairAsync(
        this AppDbContext db,
        int actorId,
        string sourceMovieCode,
        Guid thumbStorageId,
        Guid fullStorageId,
        Guid? sourceStorageId = null,
        string? sourceExtension = null,
        double? cropX = null,
        double? cropY = null,
        double? cropWidth = null,
        double? cropHeight = null,
        long sourceLength = 0,
        DateTime? sourceLastWriteUtc = null,
        string? sourceUrl = null,
        DateTime? updatedAt = null,
        CancellationToken ct = default)
    {
        var updated = updatedAt ?? DateTime.UtcNow;
        var lastWrite = sourceLastWriteUtc ?? updated;

        var sql = BuildPairUpsert(
            db,
            ActorImageSpec,
            new Dictionary<string, object?>
            {
                [nameof(ActorImage.ActorId)] = actorId,
                [nameof(ActorImage.SourceMovieCode)] = sourceMovieCode,
                [nameof(ActorImage.SourceStorageId)] = sourceStorageId,
                [nameof(ActorImage.SourceExtension)] = sourceExtension,
                [nameof(ActorImage.CropX)] = cropX,
                [nameof(ActorImage.CropY)] = cropY,
                [nameof(ActorImage.CropWidth)] = cropWidth,
                [nameof(ActorImage.CropHeight)] = cropHeight,
                [nameof(ActorImage.SourceLength)] = sourceLength,
                [nameof(ActorImage.SourceLastWriteUtc)] = lastWrite,
                [nameof(ActorImage.SourceUrl)] = sourceUrl,
                [nameof(ActorImage.UpdatedAt)] = updated,
            },
            thumbStorageId,
            fullStorageId);

        await db.Database.ExecuteSqlAsync(sql, ct);
    }

    public static async Task UpsertCachedImagePairAsync(
        this AppDbContext db,
        string code,
        string role,
        int index,
        Guid thumbStorageId,
        Guid fullStorageId,
        long sourceLength = 0,
        DateTime? sourceLastWriteUtc = null,
        string? sourceUrl = null,
        DateTime? updatedAt = null,
        CancellationToken ct = default)
    {
        var updated = updatedAt ?? DateTime.UtcNow;
        var lastWrite = sourceLastWriteUtc ?? updated;

        var sql = BuildPairUpsert(
            db,
            CachedImageSpec,
            new Dictionary<string, object?>
            {
                [nameof(CachedImage.Code)] = code,
                [nameof(CachedImage.Role)] = role,
                [nameof(CachedImage.Index)] = index,
                [nameof(CachedImage.SourceLength)] = sourceLength,
                [nameof(CachedImage.SourceLastWriteUtc)] = lastWrite,
                [nameof(CachedImage.SourceUrl)] = sourceUrl,
                [nameof(CachedImage.UpdatedAt)] = updated,
            },
            thumbStorageId,
            fullStorageId);

        await db.Database.ExecuteSqlAsync(sql, ct);
    }
}
