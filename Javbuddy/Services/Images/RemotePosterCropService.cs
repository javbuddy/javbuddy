using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Images;

/// <summary>For a Missing movie with no local file, MetaCoverUrl is often the raw full DVD-case
/// scan (landscape, "backdrop"-shaped) rather than a proper poster — javinizer-go only crops a
/// cover into a poster as part of its own local-file-writing pipeline (see
/// internal/imageutil/poster.go's GetOptimalPosterURL: shouldCrop=true just means "the caller must
/// crop this"), which a Missing/non-local movie never goes through. This downloads and crops it
/// ourselves instead (see PosterCropGeometry for the crop geometry, ported from the same source),
/// on demand, caching the result the same way ILocalImageCacheService caches local images — same
/// CachedImages table/Role "poster", just keyed by SourceUrl instead of a local file's size/
/// last-write time for freshness (see CachedImage.SourceUrl).</summary>
public interface IRemotePosterCropService
{
    /// <summary>Returns the absolute path to the cached (and up to date) cropped-poster WebP file
    /// for this movie/variant, downloading and cropping it first if needed. Null if the cover
    /// couldn't be downloaded or decoded.</summary>
    Task<string?> GetOrCreateAsync(string code, string coverUrl, string variant, CancellationToken ct = default);
}

public class RemotePosterCropService(IHttpClientFactory httpClientFactory, ILocalImageCache imageCache, IDbContextFactory<AppDbContext> dbFactory, IImageCacheMaintenanceService cacheMaintenance, InFlightImageConversions inFlightConversions) : IRemotePosterCropService
{
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IImageCacheMaintenanceService cacheMaintenance = cacheMaintenance;
    private readonly InFlightImageConversions inFlightConversions = inFlightConversions;

    public async Task<string?> GetOrCreateAsync(string code, string coverUrl, string variant, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var requested = await db.CachedImages.AsNoTracking().FirstOrDefaultAsync(c =>
                c.Code == code && c.Role == LocalImageCacheService.RolePoster && c.Index == 0 && c.Variant == variant, ct);
            if (requested is not null && (requested.SourceUrl == coverUrl || requested.SourceUrl == "custom"))
            {
                var cachedPath = imageCache.GetStoragePath(requested.StorageId);
                if (File.Exists(cachedPath)) return cachedPath;
            }
        }

        // Concurrent misses for one cover (thumb and full, or several circuits) share a single
        // download + crop and its result.
        var paths = await inFlightConversions.RunOnceAsync(
            $"remote-poster:{code}|{coverUrl}",
            () => DownloadAndCropBothAsync(code, coverUrl, CancellationToken.None),
            ct);
        if (paths is not { } both) return null;
        return variant == LocalImageCacheService.VariantThumb ? both.ThumbPath : both.FullPath;
    }

    private async Task<(string ThumbPath, string FullPath)?> DownloadAndCropBothAsync(string code, string coverUrl, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existingList = await db.CachedImages.AsNoTracking().Where(c =>
            c.Code == code && c.Role == LocalImageCacheService.RolePoster && c.Index == 0).ToListAsync(ct);

        byte[] sourceBytes;
        try
        {
            var client = httpClientFactory.CreateClient();
            sourceBytes = await client.GetByteArrayAsync(coverUrl, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }

        byte[] thumbBytes;
        byte[] fullBytes;
        try
        {
            (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(
                () => ImageConverter.CropCoverToBothWebP(sourceBytes, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var thumbExisting = existingList.FirstOrDefault(c => c.Variant == LocalImageCacheService.VariantThumb);
        var fullExisting = existingList.FirstOrDefault(c => c.Variant == LocalImageCacheService.VariantFull);

        var thumbStorageId = thumbExisting?.StorageId ?? Guid.NewGuid();
        var fullStorageId = fullExisting?.StorageId ?? Guid.NewGuid();

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);

        var updatedAt = DateTime.UtcNow;
        await db.UpsertCachedImagePairAsync(
            code: code,
            role: LocalImageCacheService.RolePoster,
            index: 0,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceLength: 0L,
            sourceLastWriteUtc: DateTime.MinValue,
            sourceUrl: coverUrl,
            updatedAt: updatedAt,
            ct: ct);

        await cacheMaintenance.EnforceSizeLimitAsync((long)thumbBytes.Length + fullBytes.Length, ct);

        return (destThumbPath, destFullPath);
    }
}
