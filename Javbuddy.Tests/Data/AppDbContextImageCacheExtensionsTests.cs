using Javbuddy.Data;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Data;

public class AppDbContextImageCacheExtensionsTests
{
    [Fact]
    public async Task UpsertActorImagePairAsync_InsertsThumbAndFullVariants()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        var actorId = 1;
        var thumbStorageId = Guid.NewGuid();
        var fullStorageId = Guid.NewGuid();
        var sourceStorageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: "TEST-001",
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceStorageId: sourceStorageId,
            sourceExtension: ".jpg",
            cropX: 0.1,
            cropY: 0.2,
            cropWidth: 0.5,
            cropHeight: 0.6,
            sourceLength: 12345,
            sourceLastWriteUtc: now,
            sourceUrl: null,
            updatedAt: now);

        var rows = await db.ActorImages.Where(a => a.ActorId == actorId).OrderBy(a => a.Variant).ToListAsync();
        Assert.Equal(2, rows.Count);

        var fullRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantFull);
        Assert.Equal(fullStorageId, fullRow.StorageId);
        Assert.Equal("TEST-001", fullRow.SourceMovieCode);
        Assert.Equal(sourceStorageId, fullRow.SourceStorageId);
        Assert.Equal(".jpg", fullRow.SourceExtension);
        Assert.Equal(0.1, fullRow.CropX);
        Assert.Equal(12345, fullRow.SourceLength);

        var thumbRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantThumb);
        Assert.Equal(thumbStorageId, thumbRow.StorageId);
        Assert.Equal("TEST-001", thumbRow.SourceMovieCode);
        Assert.Equal(sourceStorageId, thumbRow.SourceStorageId);
        Assert.Equal(".jpg", thumbRow.SourceExtension);
        Assert.Equal(0.1, thumbRow.CropX);
        Assert.Equal(12345, thumbRow.SourceLength);
    }

    [Fact]
    public async Task UpsertActorImagePairAsync_UpdatesExistingRowsOnConflict()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        var actorId = 2;
        var thumb1 = Guid.NewGuid();
        var full1 = Guid.NewGuid();
        var t1 = DateTime.UtcNow.AddHours(-1);

        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: "TEST-001",
            thumbStorageId: thumb1,
            fullStorageId: full1,
            sourceLength: 1000,
            updatedAt: t1);

        var thumb2 = Guid.NewGuid();
        var full2 = Guid.NewGuid();
        var source2 = Guid.NewGuid();
        var t2 = DateTime.UtcNow;

        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: "TEST-002",
            thumbStorageId: thumb2,
            fullStorageId: full2,
            sourceStorageId: source2,
            sourceExtension: ".webp",
            cropX: 0.25,
            cropY: 0.25,
            cropWidth: 0.5,
            cropHeight: 0.5,
            sourceLength: 2000,
            sourceUrl: "https://example.com/actor.jpg",
            updatedAt: t2);

        var rows = await db.ActorImages.Where(a => a.ActorId == actorId).ToListAsync();
        Assert.Equal(2, rows.Count);

        var thumbRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantThumb);
        Assert.Equal(thumb2, thumbRow.StorageId);
        Assert.Equal("TEST-002", thumbRow.SourceMovieCode);
        Assert.Equal(source2, thumbRow.SourceStorageId);
        Assert.Equal(".webp", thumbRow.SourceExtension);
        Assert.Equal(0.25, thumbRow.CropX);
        Assert.Equal(2000, thumbRow.SourceLength);
        Assert.Equal("https://example.com/actor.jpg", thumbRow.SourceUrl);

        var fullRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantFull);
        Assert.Equal(full2, fullRow.StorageId);
        Assert.Equal("TEST-002", fullRow.SourceMovieCode);
        Assert.Equal(source2, fullRow.SourceStorageId);
        Assert.Equal(".webp", fullRow.SourceExtension);
    }

    [Fact]
    public async Task UpsertCachedImagePairAsync_InsertsThumbAndFullVariants()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        var code = "ABC-123";
        var thumbStorageId = Guid.NewGuid();
        var fullStorageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await db.UpsertCachedImagePairAsync(
            code: code,
            role: "poster",
            index: 0,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceLength: 54321,
            sourceLastWriteUtc: now,
            sourceUrl: null,
            updatedAt: now);

        var rows = await db.CachedImages.Where(c => c.Code == code).ToListAsync();
        Assert.Equal(2, rows.Count);

        var thumbRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantThumb);
        Assert.Equal(thumbStorageId, thumbRow.StorageId);
        Assert.Equal("poster", thumbRow.Role);
        Assert.Equal(0, thumbRow.Index);
        Assert.Equal(54321, thumbRow.SourceLength);

        var fullRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantFull);
        Assert.Equal(fullStorageId, fullRow.StorageId);
        Assert.Equal("poster", fullRow.Role);
        Assert.Equal(0, fullRow.Index);
        Assert.Equal(54321, fullRow.SourceLength);
    }

    [Fact]
    public async Task UpsertCachedImagePairAsync_UpdatesExistingRowsOnConflict()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        var code = "XYZ-789";
        var thumb1 = Guid.NewGuid();
        var full1 = Guid.NewGuid();

        await db.UpsertCachedImagePairAsync(
            code: code,
            role: "fanart",
            index: 1,
            thumbStorageId: thumb1,
            fullStorageId: full1,
            sourceLength: 100);

        var thumb2 = Guid.NewGuid();
        var full2 = Guid.NewGuid();

        await db.UpsertCachedImagePairAsync(
            code: code,
            role: "fanart",
            index: 1,
            thumbStorageId: thumb2,
            fullStorageId: full2,
            sourceLength: 200,
            sourceUrl: "https://example.com/fanart.jpg");

        var rows = await db.CachedImages.Where(c => c.Code == code && c.Role == "fanart" && c.Index == 1).ToListAsync();
        Assert.Equal(2, rows.Count);

        var thumbRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantThumb);
        Assert.Equal(thumb2, thumbRow.StorageId);
        Assert.Equal(200, thumbRow.SourceLength);
        Assert.Equal("https://example.com/fanart.jpg", thumbRow.SourceUrl);

        var fullRow = rows.First(r => r.Variant == AppDbContextImageCacheExtensions.VariantFull);
        Assert.Equal(full2, fullRow.StorageId);
        Assert.Equal(200, fullRow.SourceLength);
        Assert.Equal("https://example.com/fanart.jpg", fullRow.SourceUrl);
    }

    [Fact]
    public async Task UpsertActorImagePairAsync_ConcurrentUpserts_DoNotThrowOrCorruptData()
    {
        using var factory = TestDbContextFactory.WithConnectionPerContext();

        var actorId = 3;
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.UpsertActorImagePairAsync(
                actorId: actorId,
                sourceMovieCode: $"TEST-00{i}",
                thumbStorageId: Guid.NewGuid(),
                fullStorageId: Guid.NewGuid(),
                sourceLength: 1000 + i);
        });

        await Task.WhenAll(tasks);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var rows = await verifyDb.ActorImages.Where(a => a.ActorId == actorId).ToListAsync();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task UpsertCachedImagePairAsync_ConcurrentUpserts_DoNotThrowOrCorruptData()
    {
        using var factory = TestDbContextFactory.WithConnectionPerContext();

        var code = "CONCUR-1";
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.UpsertCachedImagePairAsync(
                code: code,
                role: "poster",
                index: 0,
                thumbStorageId: Guid.NewGuid(),
                fullStorageId: Guid.NewGuid(),
                sourceLength: 500 + i);
        });

        await Task.WhenAll(tasks);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var rows = await verifyDb.CachedImages.Where(c => c.Code == code && c.Role == "poster" && c.Index == 0).ToListAsync();
        Assert.Equal(2, rows.Count);
    }
}
