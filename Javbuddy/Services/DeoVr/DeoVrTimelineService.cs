using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.DeoVr;

/// <summary>Serves a movie's DeoVR timeline mosaic (see DeoVrTimeline). It's built from
/// the movie's trickplay set on first request and kept in the disposable image cache,
/// one entry per movie stamped with the set's identity: a request for another set rebuilds it in
/// place, and the cache's TTL, size eviction and purge apply as to any cached image. With
/// ImageCache:Mode Disabled it's built on every request and not kept.</summary>
public interface IDeoVrTimelineService
{
    /// <summary>The mosaic for the movie's trickplay set with this identity, built if it isn't cached
    /// yet; null when the movie or set doesn't exist. The caller disposes the result.</summary>
    Task<StoredObject?> OpenAsync(int movieId, string identity, CancellationToken ct = default);
}

public sealed class DeoVrTimelineService(
    IDbContextFactory<AppDbContext> dbFactory,
    ITrickplayStore store,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    InFlightImageConversions inFlightConversions,
    ILogger<DeoVrTimelineService> logger) : IDeoVrTimelineService
{
    private const int CacheIndex = 0;

    private sealed record Mosaic(byte[] Bytes, DateTimeOffset LastModified);

    public async Task<StoredObject?> OpenAsync(int movieId, string identity, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(identity)) return null;

        string? code;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            code = await db.Movies.AsNoTracking().Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(code) || await store.GetSetAsync(code, identity, ct) is not { } set) return null;

        var caches = imageCache.Settings.Mode != ImageCacheMode.Disabled;
        if (caches && await OpenCachedAsync(code, identity, ct) is { } cached) return cached;

        // Built detached from this request's cancellation (see RunOnceAsync), so the build shared with
        // concurrent requests for the same set isn't lost when the first one goes away.
        var mosaic = await inFlightConversions.RunOnceAsync(
            $"deovr-timeline:{code}:{identity}", () => BuildAsync(code, set, caches, CancellationToken.None), ct);
        if (mosaic is null) return null;

        var key = $"{DeoVrTimeline.CacheRole}/{code}/{identity}";
        return new StoredObject(key, new MemoryStream(mosaic.Bytes, writable: false), mosaic.Bytes.Length, mosaic.LastModified,
            FileSystemObjectStore.ETagFor(mosaic.LastModified, mosaic.Bytes.Length));
    }

    /// <summary>The cached mosaic when it was built from this set, hasn't expired and is still on disk.</summary>
    private async Task<StoredObject?> OpenCachedAsync(string code, string identity, CancellationToken ct)
    {
        var row = await FindRowAsync(code, ct);
        if (row is null || row.SourceIdentity != identity || imageCache.Settings.IsExpired(row.UpdatedAt)) return null;

        var path = imageCache.GetStoragePath(row);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        return new StoredObject(path, stream, stream.Length, lastModified, FileSystemObjectStore.ETagFor(lastModified, stream.Length));
    }

    private async Task<Mosaic?> BuildAsync(string code, TrickplaySet set, bool caches, CancellationToken ct)
    {
        var sheets = new List<byte[]>();
        for (var index = 0; index < DeoVrTimeline.SheetCount(set); index++)
        {
            await using var sheet = await store.OpenSheetAsync(code, set.Identity, index, ct);
            if (sheet is null) break;
            using var buffer = new MemoryStream();
            await sheet.Content.CopyToAsync(buffer, ct);
            sheets.Add(buffer.ToArray());
        }
        if (sheets.Count == 0)
        {
            logger.LogWarning("Trickplay set {Identity} for {Code} has no tile sheets to build a DeoVR timeline from", set.Identity, code);
            return null;
        }

        var bytes = await ImageConversionGate.RunAsync(() => DeoVrTimeline.Compose(set, sheets), ct);
        var lastModified = DateTimeOffset.UtcNow;
        if (caches)
        {
            lastModified = await TryCacheAsync(code, set.Identity, bytes, ct) ?? lastModified;
        }
        return new Mosaic(bytes, lastModified);
    }

    /// <summary>Writes the mosaic over the movie's cache entry and stamps it with the set's identity;
    /// returns the file's write time, or null when it couldn't be cached (it's still served).</summary>
    private async Task<DateTimeOffset?> TryCacheAsync(string code, string identity, byte[] bytes, CancellationToken ct)
    {
        string? temp = null;
        try
        {
            var storageId = (await FindRowAsync(code, ct))?.StorageId ?? Guid.NewGuid();
            var destination = imageCache.GetStoragePath(storageId, DeoVrTimeline.CacheVariant);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Not a *.jpg, so the cache's walks never see a half-written file.
            temp = $"{destination}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, destination, overwrite: true);
            temp = null;

            await UpsertRowAsync(code, identity, storageId, ct);
            await cacheMaintenance.EnforceSizeLimitAsync(bytes.Length, ct);
            return new DateTimeOffset(File.GetLastWriteTimeUtc(destination), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(ex, "Couldn't cache the DeoVR timeline for {Code}", code);
            return null;
        }
        finally
        {
            if (temp is not null) File.Delete(temp);
        }
    }

    private async Task<CachedImage?> FindRowAsync(string code, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CachedImages.AsNoTracking().FirstOrDefaultAsync(
            c => c.Code == code && c.Role == DeoVrTimeline.CacheRole && c.Index == CacheIndex && c.Variant == DeoVrTimeline.CacheVariant, ct);
    }

    private async Task UpsertRowAsync(string code, string identity, Guid storageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CachedImages.FirstOrDefaultAsync(
            c => c.Code == code && c.Role == DeoVrTimeline.CacheRole && c.Index == CacheIndex && c.Variant == DeoVrTimeline.CacheVariant, ct);
        if (row is null)
        {
            row = new CachedImage { Code = code, Role = DeoVrTimeline.CacheRole, Index = CacheIndex, Variant = DeoVrTimeline.CacheVariant };
            db.CachedImages.Add(row);
        }
        row.StorageId = storageId;
        row.SourceIdentity = identity;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
