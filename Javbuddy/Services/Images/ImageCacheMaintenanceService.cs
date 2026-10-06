using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Images;

/// <summary>Stats and manual purge for the shared local image cache. Actor records retain their
/// durable-source metadata; only their regenerated WebP files are disposable.</summary>
public interface IImageCacheMaintenanceService
{
    Task<ImageCacheStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>Records addedBytes just written to the cache and, once the tracked total passes the
    /// configured size limit, evicts the oldest cache entries until the cache is back down to
    /// EvictionTargetRatio of the limit. The directory is only scanned when the tracked total is
    /// over the limit (or not yet known). A null limit leaves the cache untouched.</summary>
    Task EnforceSizeLimitAsync(long addedBytes, CancellationToken ct = default);

    /// <summary>Deletes movie-cache rows and every cached file under the cache directory. Actor rows
    /// are deliberately retained so newly stored actor-image sources can regenerate their variants.</summary>
    Task PurgeAsync(CancellationToken ct = default);
}

/// <summary>Cache footprint split into WebP still images (posters, fanart, actor photos,
/// screenshots), WebM scene/highlight hover previews and JPEG DeoVR timeline mosaics
///.</summary>
public record ImageCacheStats(int MovieImageRows, int ActorImageRows, int StillImageFileCount, long StillImageBytes, int PreviewFileCount, long PreviewBytes,
    int TimelineFileCount = 0, long TimelineBytes = 0)
{
    public int FileCount => StillImageFileCount + PreviewFileCount + TimelineFileCount;

    public long TotalBytes => StillImageBytes + PreviewBytes + TimelineBytes;
}

public class ImageCacheMaintenanceService(ILocalImageCache imageCache, IDbContextFactory<AppDbContext> dbFactory, ImageCacheSizeTracker sizeTracker) : IImageCacheMaintenanceService
{
    /// <summary>Eviction frees down to this share of the limit rather than to the limit itself, so
    /// a full cache isn't rescanned on the very next conversion.</summary>
    public const double EvictionTargetRatio = 0.9;

    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly ImageCacheSizeTracker sizeTracker = sizeTracker;

    public async Task<ImageCacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieImageRows = await db.CachedImages.CountAsync(ct);
        var actorImageRows = 0;

        // This walk runs every minute for the metrics gauges anyway, so it also re-baselines the
        // size tracker, which picks up cache writes that don't report into it. It skips that
        // rather than wait when an eviction pass holds the gate.
        var rebase = sizeTracker.ScanGate.Wait(0);
        var stillImageFileCount = 0;
        var stillImageBytes = 0L;
        var previewFileCount = 0;
        var previewBytes = 0L;
        var timelineFileCount = 0;
        var timelineBytes = 0L;
        try
        {
            var snapshot = sizeTracker.TrackedBytes;
            if (Directory.Exists(imageCache.RootPath))
            {
                foreach (var file in ILocalImageCache.EnumerateFiles(imageCache.RootPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var length = new FileInfo(file).Length;
                    if (file.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                    {
                        previewFileCount++;
                        previewBytes += length;
                    }
                    else if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        timelineFileCount++;
                        timelineBytes += length;
                    }
                    else
                    {
                        stillImageFileCount++;
                        stillImageBytes += length;
                    }
                }
            }

            if (rebase) sizeTracker.Rebase(snapshot, stillImageBytes + previewBytes + timelineBytes);
        }
        finally
        {
            if (rebase) sizeTracker.ScanGate.Release();
        }

        return new ImageCacheStats(movieImageRows, actorImageRows, stillImageFileCount, stillImageBytes, previewFileCount, previewBytes, timelineFileCount, timelineBytes);
    }

    public async Task EnforceSizeLimitAsync(long addedBytes, CancellationToken ct = default)
    {
        if (imageCache.Settings.MaxSizeBytes is not { } maxSizeBytes) return;

        var trackedBytes = sizeTracker.Add(addedBytes);
        if (sizeTracker.HasBaseline && trackedBytes <= maxSizeBytes) return;

        await sizeTracker.ScanGate.WaitAsync(ct);
        try
        {
            // Another caller may have evicted while this one waited for the gate.
            if (sizeTracker.HasBaseline && sizeTracker.TrackedBytes <= maxSizeBytes) return;
            await ScanAndEvictAsync(maxSizeBytes, ct);
        }
        finally
        {
            sizeTracker.ScanGate.Release();
        }
    }

    private async Task ScanAndEvictAsync(long maxSizeBytes, CancellationToken ct)
    {
        var snapshot = sizeTracker.TrackedBytes;
        var files = Directory.Exists(imageCache.RootPath)
            ? ILocalImageCache.EnumerateFiles(imageCache.RootPath)
                .Select(path => new FileInfo(path))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList()
            : [];
        var totalBytes = files.Sum(file => file.Length);
        if (totalBytes > maxSizeBytes)
        {
            var targetBytes = (long)(maxSizeBytes * EvictionTargetRatio);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (totalBytes <= targetBytes) break;

                var storageIdText = Path.GetFileNameWithoutExtension(file.Name);
                if (!Guid.TryParseExact(storageIdText, "N", out var storageId)) continue;

                File.Delete(file.FullName);
                totalBytes -= file.Length;
                await db.CachedImages.Where(image => image.StorageId == storageId).ExecuteDeleteAsync(ct);
            }
        }

        sizeTracker.Rebase(snapshot, totalBytes);
    }

    public async Task PurgeAsync(CancellationToken ct = default)
    {
        sizeTracker.Invalidate();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.CachedImages.ExecuteDeleteAsync(ct);
        if (!Directory.Exists(imageCache.RootPath)) return;

        foreach (var file in ILocalImageCache.EnumerateFiles(imageCache.RootPath))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
