using System.Runtime.CompilerServices;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Data;

/// <summary>Encapsulates compile-time-safe, atomic SQLite upserts for image cache metadata tables.
/// EF Core 9 lacks a native atomic upsert primitive; SQLite's native "INSERT ... ON CONFLICT DO UPDATE"
/// prevents concurrent cache misses from racing on the UNIQUE constraint and polluting application
/// logs with DbUpdateException errors. All table and column names are checked at compile-time via nameof().</summary>
public static class AppDbContextImageCacheExtensions
{
    public const string VariantThumb = "thumb";
    public const string VariantFull = "full";

    private static readonly string UpsertActorImagesFormat = $$"""
        INSERT INTO "{{nameof(AppDbContext.ActorImages)}}" (
            "{{nameof(ActorImage.ActorId)}}",
            "{{nameof(ActorImage.Variant)}}",
            "{{nameof(ActorImage.SourceMovieCode)}}",
            "{{nameof(ActorImage.StorageId)}}",
            "{{nameof(ActorImage.SourceStorageId)}}",
            "{{nameof(ActorImage.CropX)}}",
            "{{nameof(ActorImage.CropY)}}",
            "{{nameof(ActorImage.CropWidth)}}",
            "{{nameof(ActorImage.CropHeight)}}",
            "{{nameof(ActorImage.SourceLength)}}",
            "{{nameof(ActorImage.SourceLastWriteUtc)}}",
            "{{nameof(ActorImage.SourceUrl)}}",
            "{{nameof(ActorImage.UpdatedAt)}}",
            "{{nameof(ActorImage.SourceExtension)}}"
        )
        VALUES 
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {15}),
            ({0}, {13}, {2}, {14}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {15})
        ON CONFLICT ("{{nameof(ActorImage.ActorId)}}", "{{nameof(ActorImage.Variant)}}") DO UPDATE SET
            "{{nameof(ActorImage.SourceMovieCode)}}" = excluded."{{nameof(ActorImage.SourceMovieCode)}}",
            "{{nameof(ActorImage.StorageId)}}" = excluded."{{nameof(ActorImage.StorageId)}}",
            "{{nameof(ActorImage.SourceStorageId)}}" = excluded."{{nameof(ActorImage.SourceStorageId)}}",
            "{{nameof(ActorImage.CropX)}}" = excluded."{{nameof(ActorImage.CropX)}}",
            "{{nameof(ActorImage.CropY)}}" = excluded."{{nameof(ActorImage.CropY)}}",
            "{{nameof(ActorImage.CropWidth)}}" = excluded."{{nameof(ActorImage.CropWidth)}}",
            "{{nameof(ActorImage.CropHeight)}}" = excluded."{{nameof(ActorImage.CropHeight)}}",
            "{{nameof(ActorImage.SourceLength)}}" = excluded."{{nameof(ActorImage.SourceLength)}}",
            "{{nameof(ActorImage.SourceLastWriteUtc)}}" = excluded."{{nameof(ActorImage.SourceLastWriteUtc)}}",
            "{{nameof(ActorImage.SourceUrl)}}" = excluded."{{nameof(ActorImage.SourceUrl)}}",
            "{{nameof(ActorImage.UpdatedAt)}}" = excluded."{{nameof(ActorImage.UpdatedAt)}}",
            "{{nameof(ActorImage.SourceExtension)}}" = excluded."{{nameof(ActorImage.SourceExtension)}}"
        """;

    private static readonly string UpsertCachedImagesFormat = $$"""
        INSERT INTO "{{nameof(AppDbContext.CachedImages)}}" (
            "{{nameof(CachedImage.Code)}}",
            "{{nameof(CachedImage.Role)}}",
            "{{nameof(CachedImage.Index)}}",
            "{{nameof(CachedImage.Variant)}}",
            "{{nameof(CachedImage.StorageId)}}",
            "{{nameof(CachedImage.SourceLength)}}",
            "{{nameof(CachedImage.SourceLastWriteUtc)}}",
            "{{nameof(CachedImage.SourceUrl)}}",
            "{{nameof(CachedImage.UpdatedAt)}}"
        )
        VALUES 
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}),
            ({0}, {1}, {2}, {9}, {10}, {5}, {6}, {7}, {8})
        ON CONFLICT ("{{nameof(CachedImage.Code)}}", "{{nameof(CachedImage.Role)}}", "{{nameof(CachedImage.Index)}}", "{{nameof(CachedImage.Variant)}}") DO UPDATE SET
            "{{nameof(CachedImage.StorageId)}}" = excluded."{{nameof(CachedImage.StorageId)}}",
            "{{nameof(CachedImage.SourceLength)}}" = excluded."{{nameof(CachedImage.SourceLength)}}",
            "{{nameof(CachedImage.SourceLastWriteUtc)}}" = excluded."{{nameof(CachedImage.SourceLastWriteUtc)}}",
            "{{nameof(CachedImage.SourceUrl)}}" = excluded."{{nameof(CachedImage.SourceUrl)}}",
            "{{nameof(CachedImage.UpdatedAt)}}" = excluded."{{nameof(CachedImage.UpdatedAt)}}"
        """;

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

        var sql = FormattableStringFactory.Create(
            UpsertActorImagesFormat,
            actorId,
            VariantThumb,
            sourceMovieCode,
            thumbStorageId,
            sourceStorageId,
            cropX,
            cropY,
            cropWidth,
            cropHeight,
            sourceLength,
            lastWrite,
            sourceUrl,
            updated,
            VariantFull,
            fullStorageId,
            sourceExtension);

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

        var sql = FormattableStringFactory.Create(
            UpsertCachedImagesFormat,
            code,
            role,
            index,
            VariantThumb,
            thumbStorageId,
            sourceLength,
            lastWrite,
            sourceUrl,
            updated,
            VariantFull,
            fullStorageId);

        await db.Database.ExecuteSqlAsync(sql, ct);
    }
}
