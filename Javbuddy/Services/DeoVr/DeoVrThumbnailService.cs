using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.DeoVr;

/// <summary>A movie's DeoVR list thumbnail: a JPEG to stream, else a remote URL to redirect to.</summary>
public sealed record DeoVrThumbnail(StoredObject? Image, string? RedirectUrl);

/// <summary>Serves a movie's DeoVR list thumbnail. DeoVR doesn't show WebP, so this is the local
/// fanart (any format the image cache reads) downscaled and re-encoded as JPEG, kept in the
/// disposable image cache, one entry per movie stamped with the source file's size and write time:
/// built on first request, or ahead of time by the Image Cache task (see PregenerateAsync), a changed
/// fanart rebuilds it in place, and the cache's TTL, size eviction and purge apply as to any cached image. With ImageCache:Mode Disabled it's built on every request and not kept. A movie
/// with no local fanart redirects to its remote cover.</summary>
public interface IDeoVrThumbnailService
{
    /// <summary>Null when the movie doesn't exist or has no image. The caller disposes the result's Image.</summary>
    Task<DeoVrThumbnail?> GetAsync(int movieId, CancellationToken ct = default);

    /// <summary>Builds and caches the movie's thumbnail unless it's already cached and up to date.
    /// True when it's cached; false when its fanart couldn't be converted; null when there's nothing
    /// to cache (no local fanart, or ImageCache:Mode Disabled).</summary>
    Task<bool?> PregenerateAsync(string code, CancellationToken ct = default);
}

public sealed class DeoVrThumbnailService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalImageCacheService imageCacheService,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    InFlightImageConversions inFlightConversions,
    ILogger<DeoVrThumbnailService> logger) : IDeoVrThumbnailService
{
    public const string CacheRole = "deovr-thumb";
    public const string CacheVariant = "deovr-thumb";
    public const int MaxEdge = 800;
    public const int JpegQuality = 80;
    private const int CacheIndex = 0;

    private sealed record Thumbnail(byte[] Bytes, DateTimeOffset LastModified);

    public async Task<DeoVrThumbnail?> GetAsync(int movieId, CancellationToken ct = default)
    {
        string? code;
        string? coverUrl;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies.AsNoTracking()
                .Where(m => m.Id == movieId)
                .Select(m => new { m.Code, m.MetaCoverUrl })
                .FirstOrDefaultAsync(ct);
            (code, coverUrl) = (movie?.Code, movie?.MetaCoverUrl);
        }
        if (string.IsNullOrWhiteSpace(code)) return null;

        var sourcePath = await imageCacheService.ResolveSourcePathAsync(code, LocalImageCacheService.RoleFanart, 0, ct);
        var source = string.IsNullOrEmpty(sourcePath) ? null : new FileInfo(sourcePath);
        if (source is { Exists: true } && await OpenAsync(code, source, ct) is { } image) return new DeoVrThumbnail(image, null);

        return Uri.TryCreate(coverUrl, UriKind.Absolute, out var remote) && (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps)
            ? new DeoVrThumbnail(null, remote.AbsoluteUri)
            : null;
    }

    public async Task<bool?> PregenerateAsync(string code, CancellationToken ct = default)
    {
        if (imageCache.Settings.Mode == ImageCacheMode.Disabled) return null;

        var sourcePath = await imageCacheService.ResolveSourcePathAsync(code, LocalImageCacheService.RoleFanart, 0, ct);
        var source = string.IsNullOrEmpty(sourcePath) ? null : new FileInfo(sourcePath);
        if (source is not { Exists: true }) return null;

        var row = await FindRowAsync(code, ct);
        if (row is not null && MatchesSource(row, source) && !imageCache.Settings.IsExpired(row.UpdatedAt) && File.Exists(imageCache.GetStoragePath(row)))
        {
            return true;
        }

        var thumbnail = await inFlightConversions.RunOnceAsync(
            $"deovr-thumb:{code}", () => BuildAsync(code, source, caches: true, CancellationToken.None), ct);
        return thumbnail is not null;
    }

    private async Task<StoredObject?> OpenAsync(string code, FileInfo source, CancellationToken ct)
    {
        var caches = imageCache.Settings.Mode != ImageCacheMode.Disabled;
        if (caches && await OpenCachedAsync(code, source, ct) is { } cached) return cached;

        // Built detached from this request's cancellation (see RunOnceAsync), so the build shared with
        // concurrent requests for the same movie isn't lost when the first one goes away.
        var thumbnail = await inFlightConversions.RunOnceAsync(
            $"deovr-thumb:{code}", () => BuildAsync(code, source, caches, CancellationToken.None), ct);
        if (thumbnail is null) return null;

        return new StoredObject($"{CacheRole}/{code}", new MemoryStream(thumbnail.Bytes, writable: false), thumbnail.Bytes.Length,
            thumbnail.LastModified, FileSystemObjectStore.ETagFor(thumbnail.LastModified, thumbnail.Bytes.Length));
    }

    /// <summary>The cached thumbnail when it was built from this source file, hasn't expired and is still on disk.</summary>
    private async Task<StoredObject?> OpenCachedAsync(string code, FileInfo source, CancellationToken ct)
    {
        var row = await FindRowAsync(code, ct);
        if (row is null || !MatchesSource(row, source) || imageCache.Settings.IsExpired(row.UpdatedAt)) return null;

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

    private static bool MatchesSource(CachedImage row, FileInfo source) =>
        row.SourceLength == source.Length && row.SourceLastWriteUtc == source.LastWriteTimeUtc;

    private async Task<Thumbnail?> BuildAsync(string code, FileInfo source, bool caches, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertToJpeg(source.FullName, MaxEdge, JpegQuality), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't build the DeoVR thumbnail for {Code} from {Path}", code, source.FullName);
            return null;
        }

        var lastModified = DateTimeOffset.UtcNow;
        if (caches)
        {
            lastModified = await TryCacheAsync(code, source, bytes, ct) ?? lastModified;
        }
        return new Thumbnail(bytes, lastModified);
    }

    /// <summary>Writes the thumbnail over the movie's cache entry and stamps it with the source file;
    /// returns the file's write time, or null when it couldn't be cached (it's still served).</summary>
    private async Task<DateTimeOffset?> TryCacheAsync(string code, FileInfo source, byte[] bytes, CancellationToken ct)
    {
        string? temp = null;
        try
        {
            var storageId = (await FindRowAsync(code, ct))?.StorageId ?? Guid.NewGuid();
            var destination = imageCache.GetStoragePath(storageId, CacheVariant);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Not a *.jpg, so the cache's walks never see a half-written file.
            temp = $"{destination}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, destination, overwrite: true);
            temp = null;

            await UpsertRowAsync(code, source, storageId, ct);
            await cacheMaintenance.EnforceSizeLimitAsync(bytes.Length, ct);
            return new DateTimeOffset(File.GetLastWriteTimeUtc(destination), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(ex, "Couldn't cache the DeoVR thumbnail for {Code}", code);
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
            c => c.Code == code && c.Role == CacheRole && c.Index == CacheIndex && c.Variant == CacheVariant, ct);
    }

    private async Task UpsertRowAsync(string code, FileInfo source, Guid storageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CachedImages.FirstOrDefaultAsync(
            c => c.Code == code && c.Role == CacheRole && c.Index == CacheIndex && c.Variant == CacheVariant, ct);
        if (row is null)
        {
            row = new CachedImage { Code = code, Role = CacheRole, Index = CacheIndex, Variant = CacheVariant };
            db.CachedImages.Add(row);
        }
        row.StorageId = storageId;
        row.SourceLength = source.Length;
        row.SourceLastWriteUtc = source.LastWriteTimeUtc;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
