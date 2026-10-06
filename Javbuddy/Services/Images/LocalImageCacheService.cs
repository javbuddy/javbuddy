using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Images;

/// <summary>Resolves a (code, role, index, variant) request to a cached WebP file on disk,
/// converting on demand the first time it's asked for. Shared by the /image-cache serving
/// endpoint (Program.cs) and ImageCacheTask's bulk background scan, so there's exactly one
/// place that decides "is this cache entry still fresh" and "how do we convert/store one".</summary>
public interface ILocalImageCacheService
{
    /// <summary>Returns the absolute path to the cached (and up to date) WebP file for this
    /// image, converting/caching it first if needed. Null if no local source exists for this
    /// role/index at all (the caller should fall back to a remote URL, if any, or 404).</summary>
    Task<string?> GetOrCreateAsync(string code, string role, int index, string variant, CancellationToken ct = default);

    /// <summary>Generates/ensures both Thumb and Full cached WebP files in a single decode pass,
    /// avoiding redundant network/disk reads and duplicate decodes.</summary>
    Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(string code, string role, int index, CancellationToken ct = default);

    /// <summary>The movie's local source image for this role/index (e.g. its poster.jpg), without
    /// touching the cache. Null if there's none.</summary>
    Task<string?> ResolveSourcePathAsync(string code, string role, int index, CancellationToken ct = default);
}

public class LocalImageCacheService(ILocalLibraryClient localLibraryClient, ILocalImageCache imageCache, IDbContextFactory<AppDbContext> dbFactory, JavbuddyMetrics metrics, IImageCacheMaintenanceService cacheMaintenance, InFlightImageConversions inFlightConversions) : ILocalImageCacheService
{
    public const string RolePoster = "poster";
    public const string RoleFanart = "fanart";
    public const string RoleExtraFanart = "extrafanart";

    public const string VariantThumb = "thumb";
    public const string VariantFull = "full";
    public const string VariantOriginal = "original";

    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly JavbuddyMetrics metrics = metrics;
    private readonly IImageCacheMaintenanceService cacheMaintenance = cacheMaintenance;
    private readonly InFlightImageConversions inFlightConversions = inFlightConversions;

    public async Task<string?> GetOrCreateAsync(string code, string role, int index, string variant, CancellationToken ct = default)
    {
        var srcPath = await ResolveSourcePathAsync(code, role, index, ct);

        if (variant == VariantOriginal)
        {
            // Bypasses the WebP cache entirely, serving the on-disk file as-is in its original
            // format/resolution — only meaningful for extrafanart, which has no
            // remote-metadata fallback to fall back to instead.
            return role == RoleExtraFanart ? srcPath : null;
        }

        var srcInfo = srcPath is not null ? new FileInfo(srcPath) : null;
        var srcExists = srcInfo is { Exists: true };

        if (srcExists && !imageCache.Settings.CachesLocalVariant(variant)) return srcPath!;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.CachedImages.AsNoTracking().FirstOrDefaultAsync(c =>
            c.Code == code && c.Role == role && c.Index == index && c.Variant == variant, ct);

        if (existing is not null)
        {
            var isFresh = srcExists ? IsFresh(existing, srcInfo!) : true;
            if (isFresh && !imageCache.Settings.IsExpired(existing.UpdatedAt))
            {
                var cachedPath = imageCache.GetStoragePath(existing.StorageId);
                if (File.Exists(cachedPath)) return cachedPath;
            }
        }

        if (!srcExists) return null;

        // Cache miss: generate both variants in one decode pass
        var (thumbPath, fullPath) = await GetOrCreateBothInternalAsync(code, role, index, srcPath!, srcInfo!, ct);
        return variant == VariantThumb ? thumbPath : fullPath;
    }

    public async Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(string code, string role, int index, CancellationToken ct = default)
    {
        var srcPath = await ResolveSourcePathAsync(code, role, index, ct);
        var srcInfo = srcPath is not null ? new FileInfo(srcPath) : null;
        var srcExists = srcInfo is { Exists: true };

        if (!srcExists)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.CachedImages.AsNoTracking().Where(c =>
                c.Code == code && c.Role == role && c.Index == index).ToListAsync(ct);

            var thumbCached = existing.FirstOrDefault(c => c.Variant == VariantThumb);
            var fullCached = existing.FirstOrDefault(c => c.Variant == VariantFull);

            var thumbPath = thumbCached is not null && !imageCache.Settings.IsExpired(thumbCached.UpdatedAt)
                ? imageCache.GetStoragePath(thumbCached.StorageId)
                : null;
            var fullPath = fullCached is not null && !imageCache.Settings.IsExpired(fullCached.UpdatedAt)
                ? imageCache.GetStoragePath(fullCached.StorageId)
                : null;

            if (thumbPath is not null && !File.Exists(thumbPath)) thumbPath = null;
            if (fullPath is not null && !File.Exists(fullPath)) fullPath = null;

            return (thumbPath, fullPath);
        }

        if (imageCache.Settings.Mode == ImageCacheMode.Disabled) return (srcPath, srcPath);

        return await GetOrCreateBothInternalAsync(code, role, index, srcPath!, srcInfo!, ct);
    }

    // Concurrent misses for one image (e.g. the thumb and full of the same poster, or two
    // circuits scrolling the same grid) share a single conversion and its result.
    private Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothInternalAsync(
        string code, string role, int index, string srcPath, FileInfo srcInfo, CancellationToken ct) =>
        inFlightConversions.RunOnceAsync(
            $"local:{code}|{role}|{index}",
            () => ConvertAndStoreBothAsync(code, role, index, srcPath, srcInfo, CancellationToken.None),
            ct);

    private async Task<(string? ThumbPath, string? FullPath)> ConvertAndStoreBothAsync(
        string code, string role, int index, string srcPath, FileInfo srcInfo, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.CachedImages.AsNoTracking().Where(c =>
            c.Code == code && c.Role == role && c.Index == index).ToListAsync(ct);

        var thumbCached = existing.FirstOrDefault(c => c.Variant == VariantThumb);
        var fullCached = existing.FirstOrDefault(c => c.Variant == VariantFull);

        var thumbFresh = thumbCached is not null && IsFresh(thumbCached, srcInfo) && !imageCache.Settings.IsExpired(thumbCached.UpdatedAt);
        var fullFresh = fullCached is not null && IsFresh(fullCached, srcInfo) && !imageCache.Settings.IsExpired(fullCached.UpdatedAt);

        string? existingThumbPath = thumbFresh ? imageCache.GetStoragePath(thumbCached!.StorageId) : null;
        string? existingFullPath = fullFresh ? imageCache.GetStoragePath(fullCached!.StorageId) : null;

        if (existingThumbPath is not null && !File.Exists(existingThumbPath)) existingThumbPath = null;
        if (existingFullPath is not null && !File.Exists(existingFullPath)) existingFullPath = null;

        // If both are fresh and exist on disk, return immediately
        if (existingThumbPath is not null && existingFullPath is not null)
        {
            return (existingThumbPath, existingFullPath);
        }

        // Generate both in a single decode pass
        var (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => role == RolePoster
            ? ImageConverter.ConvertPosterToBothWebP(srcPath, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb)
            : ImageConverter.ConvertToBothWebP(srcPath, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);

        var thumbStorageId = thumbCached?.StorageId ?? Guid.NewGuid();
        var fullStorageId = fullCached?.StorageId ?? Guid.NewGuid();

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);
        metrics.ImageCacheConversions.Add(1);

        var updatedAt = DateTime.UtcNow;
        await db.UpsertCachedImagePairAsync(
            code: code,
            role: role,
            index: index,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceLength: srcInfo.Length,
            sourceLastWriteUtc: srcInfo.LastWriteTimeUtc,
            updatedAt: updatedAt,
            ct: ct);


        var resultFullPath = destFullPath;
        var addedBytes = (long)thumbBytes.Length + fullBytes.Length;
        if (imageCache.Settings.Mode == ImageCacheMode.Thumbnails)
        {
            File.Delete(destFullPath);
            addedBytes = thumbBytes.Length;
            await db.CachedImages.Where(image =>
                image.Code == code && image.Role == role && image.Index == index && image.Variant == VariantFull)
                .ExecuteDeleteAsync(ct);
            resultFullPath = srcPath;
        }

        await cacheMaintenance.EnforceSizeLimitAsync(addedBytes, ct);

        return (destThumbPath, resultFullPath);
    }

    private static bool IsFresh(CachedImage cached, FileInfo srcInfo) =>
        cached.SourceLength == srcInfo.Length && cached.SourceLastWriteUtc >= srcInfo.LastWriteTimeUtc;

    // Preference order: poster.* before folder.*, and within each, .jpg before .jpeg/.png/.webp —
    // must match LocalLibraryClient's PosterFileNames/FanartFileNames ordering.
    private static readonly string[] PosterCandidates =
    {
        "poster.jpg", "poster.jpeg", "poster.png", "poster.webp",
        "folder.jpg", "folder.jpeg", "folder.png", "folder.webp",
    };
    private static readonly string[] FanartCandidates = { "fanart.jpg", "fanart.jpeg", "fanart.png", "fanart.webp" };

    // One folder lookup for the whole candidate list, not one per candidate — see
    // ILocalLibraryClient.ResolveFirstExistingLocalFilePathAsync for why that difference is worth
    // a dedicated method.
    public async Task<string?> ResolveSourcePathAsync(string code, string role, int index, CancellationToken ct = default) => role switch
    {
        RolePoster => await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, PosterCandidates, ct),
        RoleFanart => await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, FanartCandidates, ct),
        RoleExtraFanart => await ResolveExtraFanartSourceAsync(code, index, ct),
        _ => null,
    };

    private async Task<string?> ResolveExtraFanartSourceAsync(string code, int index, CancellationToken ct)
    {
        if (index < 0) return null;

        var names = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
        if (index >= names.Count) return null;

        return await localLibraryClient.ResolveLiveExtraFanartFilePathAsync(code, names[index], ct);
    }
}
