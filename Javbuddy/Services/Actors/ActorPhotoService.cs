using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Javbuddy.Services.Actors;

public sealed record ActorPhotoAlbumSummary(int Id, string Name, int PhotoCount);

public sealed record ActorPhotoSummary(int Id, int? AlbumId, DateTime UploadedAt, double AspectRatio = 1.0, bool HasOriginal = false);

public sealed record ActorPhotoGalleryData(
    IReadOnlyList<ActorPhotoAlbumSummary> Albums,
    IReadOnlyList<ActorPhotoSummary> Photos);

public sealed record GlobalActorPhotoItem(
    int Id,
    int ActorId,
    string ActorDisplayName,
    int? AlbumId,
    string? AlbumName,
    double AspectRatio,
    DateTime UploadedAt,
    bool IsFavorite = false,
    bool HasOriginal = false);

public sealed record ActorWithPhotoCountSummary(
    int Id,
    string DisplayName,
    int PhotoCount);

public sealed record ActorPhotoOperationResult(bool Success, string? ErrorMessage = null)
{
    public static ActorPhotoOperationResult Ok() => new(true);
    public static ActorPhotoOperationResult Fail(string message) => new(false, message);
}

public sealed record ActorPhotoUploadItem(Func<Stream> OpenStream, long Length)
{
    public static implicit operator ActorPhotoUploadItem(byte[] bytes) =>
        new(() => new MemoryStream(bytes), bytes.LongLength);
}

public interface IActorPhotoService
{
    Task<ActorPhotoGalleryData> GetGalleryAsync(int actorId, CancellationToken ct = default);
    Task<IReadOnlyList<GlobalActorPhotoItem>> GetGlobalPhotosAsync(
        int? actorId = null,
        int? albumId = null,
        bool uncategorizedOnly = false,
        bool favoritesOnly = false,
        string? search = null,
        string? sort = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<ActorWithPhotoCountSummary>> GetActorsWithPhotosAsync(CancellationToken ct = default);
    Task<int> GetTotalPhotoCountAsync(CancellationToken ct = default);
    Task<ActorPhotoOperationResult> UploadAsync(int actorId, IReadOnlyList<byte[]> images, int? albumId, CancellationToken ct = default);
    Task<ActorPhotoOperationResult> UploadAsync(int actorId, IReadOnlyList<ActorPhotoUploadItem> items, int? albumId, IProgress<int>? progress = null, CancellationToken ct = default);
    Task<(ActorPhotoOperationResult Result, ActorPhotoAlbumSummary? Album)> CreateAlbumAsync(int actorId, string name, CancellationToken ct = default);
    Task<ActorPhotoOperationResult> RenameAlbumAsync(int actorId, int albumId, string name, CancellationToken ct = default);
    Task<ActorPhotoOperationResult> DeleteAlbumAsync(int actorId, int albumId, CancellationToken ct = default);
    Task<ActorPhotoOperationResult> MovePhotoAsync(int actorId, int photoId, int? albumId, CancellationToken ct = default);
    Task<ActorPhotoOperationResult> DeletePhotoAsync(int actorId, int photoId, CancellationToken ct = default);
    Task<byte[]?> GetPhotoBytesAsync(int actorId, int photoId, CancellationToken ct = default);
    Task<ImageServingResult> GetImageAsync(int photoId, string variant, CancellationToken ct = default);
    Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(int photoId, CancellationToken ct = default);
    Task DeleteAllForActorAsync(int actorId, CancellationToken ct = default);
}

public sealed class ActorPhotoService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalImageCache imageCache,
    IActorImageDataStore? dataStore = null,
    ImageUploadSettings? uploadSettings = null) : IActorPhotoService
{
    public const string VariantOriginal = "original";

    private readonly ImageUploadSettings uploadSettings = uploadSettings ?? ImageUploadSettings.Default;

    public async Task<ActorPhotoGalleryData> GetGalleryAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var albums = await db.ActorAlbums
            .AsNoTracking()
            .Where(album => album.ActorId == actorId)
            .OrderBy(album => album.Name)
            .Select(album => new ActorPhotoAlbumSummary(album.Id, album.Name, album.Photos.Count))
            .ToListAsync(ct);
        var photos = await db.ActorPhotos
            .AsNoTracking()
            .Where(photo => photo.ActorId == actorId)
            .OrderByDescending(photo => photo.UploadedAt)
            .ThenByDescending(photo => photo.Id)
            .Select(photo => new ActorPhotoSummary(photo.Id, photo.AlbumId, photo.UploadedAt, photo.AspectRatio ?? 1.0, photo.SourceStorageId != null))
            .ToListAsync(ct);
        return new ActorPhotoGalleryData(albums, photos);
    }

    public async Task<IReadOnlyList<GlobalActorPhotoItem>> GetGlobalPhotosAsync(
        int? actorId = null,
        int? albumId = null,
        bool uncategorizedOnly = false,
        bool favoritesOnly = false,
        string? search = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.ActorPhotos.AsNoTracking().Include(p => p.Actor).Include(p => p.Album).AsQueryable();

        if (actorId.HasValue)
        {
            query = query.Where(p => p.ActorId == actorId.Value);
        }

        if (albumId.HasValue)
        {
            query = query.Where(p => p.AlbumId == albumId.Value);
        }
        else if (uncategorizedOnly)
        {
            query = query.Where(p => p.AlbumId == null);
        }

        if (favoritesOnly)
        {
            query = query.Where(p => p.Actor != null && p.Actor.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            query = query.Where(p =>
                (p.Actor != null && (
                    (p.Actor.FirstName != null && p.Actor.FirstName.Contains(normalized)) ||
                    (p.Actor.LastName != null && p.Actor.LastName.Contains(normalized)) ||
                    (p.Actor.LastName != null && p.Actor.FirstName != null && (p.Actor.LastName + " " + p.Actor.FirstName).Contains(normalized)) ||
                    (p.Actor.FirstName != null && p.Actor.LastName != null && (p.Actor.FirstName + " " + p.Actor.LastName).Contains(normalized)) ||
                    (p.Actor.JapaneseNameKanji != null && p.Actor.JapaneseNameKanji.Contains(normalized)) ||
                    (p.Actor.JapaneseNameKana != null && p.Actor.JapaneseNameKana.Contains(normalized)))) ||
                (p.Album != null && p.Album.Name.Contains(normalized)));
        }

        query = sort switch
        {
            "oldest" => query.OrderBy(p => p.UploadedAt),
            "actor" => query.OrderBy(p => p.Actor != null ? p.Actor.LastName : "").ThenBy(p => p.Actor != null ? p.Actor.FirstName : "").ThenByDescending(p => p.UploadedAt),
            _ => query.OrderByDescending(p => p.UploadedAt)
        };

        var raw = await query
            .Select(p => new
            {
                p.Id,
                p.ActorId,
                FirstName = p.Actor != null ? p.Actor.FirstName : null,
                LastName = p.Actor != null ? p.Actor.LastName : null,
                p.AlbumId,
                AlbumName = p.Album != null ? p.Album.Name : null,
                p.AspectRatio,
                p.UploadedAt,
                IsFavorite = p.Actor != null && p.Actor.IsFavorite,
                HasOriginal = p.SourceStorageId != null
            })
            .ToListAsync(ct);

        return raw
            .Select(p => new GlobalActorPhotoItem(
                p.Id,
                p.ActorId,
                ActorDisplayName.Format(p.FirstName ?? string.Empty, p.LastName),
                p.AlbumId,
                p.AlbumName,
                p.AspectRatio ?? 1.0,
                p.UploadedAt,
                p.IsFavorite,
                p.HasOriginal))
            .ToList();
    }

    public async Task<IReadOnlyList<ActorWithPhotoCountSummary>> GetActorsWithPhotosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.ActorPhotos
            .AsNoTracking()
            .Where(p => p.Actor != null)
            .GroupBy(p => new { p.ActorId, p.Actor!.FirstName, p.Actor!.LastName })
            .Select(g => new
            {
                g.Key.ActorId,
                g.Key.FirstName,
                g.Key.LastName,
                Count = g.Count()
            })
            .ToListAsync(ct);

        return groups
            .Select(g => new ActorWithPhotoCountSummary(
                g.ActorId,
                ActorDisplayName.Format(g.FirstName ?? string.Empty, g.LastName),
                g.Count))
            .OrderBy(a => a.DisplayName)
            .ToList();
    }

    public async Task<int> GetTotalPhotoCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ActorPhotos.CountAsync(ct);
    }

    public Task<ActorPhotoOperationResult> UploadAsync(int actorId, IReadOnlyList<byte[]> images, int? albumId, CancellationToken ct = default)
    {
        var items = images.Select(image => new ActorPhotoUploadItem(() => new MemoryStream(image), image.LongLength)).ToList();
        return UploadAsync(actorId, items, albumId, null, ct);
    }

    public async Task<ActorPhotoOperationResult> UploadAsync(
        int actorId,
        IReadOnlyList<ActorPhotoUploadItem> items,
        int? albumId,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return ActorPhotoOperationResult.Fail("Choose at least one image.");
        if (items.Count > uploadSettings.MaxBatchFiles)
            return ActorPhotoOperationResult.Fail($"You can upload at most {uploadSettings.MaxBatchFiles} photos at a time.");
        if (items.Any(item => item.Length > uploadSettings.MaxSizeBytes))
            return ActorPhotoOperationResult.Fail($"Each image must be no larger than {uploadSettings.MaxSizeMb} MB.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Actors.AnyAsync(actor => actor.Id == actorId, ct)) return ActorPhotoOperationResult.Fail("Actor not found.");
        if (albumId.HasValue && !await db.ActorAlbums.AnyAsync(album => album.Id == albumId && album.ActorId == actorId, ct))
        {
            return ActorPhotoOperationResult.Fail("Album not found.");
        }

        var photos = new List<ActorPhoto>(items.Count);
        try
        {
            int processed = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = item.OpenStream();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, ct);
                var image = memory.ToArray();

                if (image.LongLength == 0 || image.LongLength > uploadSettings.MaxSizeBytes)
                {
                    foreach (var p in photos)
                    {
                        DeleteFile(p.ThumbStorageId);
                        DeleteFile(p.FullStorageId);
                        await DeleteSourceAsync(p.SourceStorageId, p.SourceExtension);
                    }
                    return ActorPhotoOperationResult.Fail($"Each image must be no larger than {uploadSettings.MaxSizeMb} MB.");
                }

                var aspectRatio = TryExtractAspectRatioFromBytes(image, out var ar) ? ar : (double?)null;

                var photo = new ActorPhoto
                {
                    ActorId = actorId,
                    AlbumId = albumId,
                    ThumbStorageId = Guid.NewGuid(),
                    FullStorageId = Guid.NewGuid(),
                    SourceStorageId = dataStore is null ? null : Guid.NewGuid(),
                    AspectRatio = aspectRatio,
                    UploadedAt = DateTime.UtcNow,
                };
                photos.Add(photo);
                if (photo.SourceStorageId is { } sourceStorageId)
                {
                    // Source bytes are kept in .tmp staging during upload, then committed to permanent
                    // storage after the database transaction succeeds.
                    photo.SourceExtension = await dataStore!.StageAsync(sourceStorageId, image, ct);
                }
                else
                {
                    // No durable source store configured: the original bytes would otherwise be
                    // lost once this request completes, so the cache variants must be generated now.
                    var (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertBytesToBothWebP(
                        image, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);
                    await WriteAsync(photo.ThumbStorageId, thumbBytes, ct);
                    await WriteAsync(photo.FullStorageId, fullBytes, ct);
                }

                processed++;
                progress?.Report(processed);
            }

            db.ActorPhotos.AddRange(photos);
            await db.SaveChangesAsync(ct);

            if (dataStore is not null)
            {
                foreach (var photo in photos)
                {
                    if (photo.SourceStorageId is { } sourceStorageId)
                    {
                        // The rows are saved, so the commit must finish even if the upload is cancelled now.
                        await dataStore.CommitAsync(sourceStorageId, CancellationToken.None);
                    }
                }
            }

            return ActorPhotoOperationResult.Ok();
        }
        catch (Exception ex)
        {
            foreach (var photo in photos)
            {
                DeleteFile(photo.ThumbStorageId);
                DeleteFile(photo.FullStorageId);
                await DeleteSourceAsync(photo.SourceStorageId, photo.SourceExtension);
            }

            if (ct.IsCancellationRequested || ex is OperationCanceledException)
            {
                throw;
            }

            return ActorPhotoOperationResult.Fail("One or more images could not be processed.");
        }
    }

    public async Task<(ActorPhotoOperationResult Result, ActorPhotoAlbumSummary? Album)> CreateAlbumAsync(int actorId, string name, CancellationToken ct = default)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return (ActorPhotoOperationResult.Fail("Album name is required."), null);
        if (normalized.Length > 100) return (ActorPhotoOperationResult.Fail("Album names can be at most 100 characters."), null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Actors.AnyAsync(actor => actor.Id == actorId, ct)) return (ActorPhotoOperationResult.Fail("Actor not found."), null);
        if (await db.ActorAlbums.AnyAsync(album => album.ActorId == actorId && album.Name == normalized, ct))
        {
            return (ActorPhotoOperationResult.Fail("An album with this name already exists."), null);
        }

        var album = new ActorAlbum { ActorId = actorId, Name = normalized };
        db.ActorAlbums.Add(album);
        await db.SaveChangesAsync(ct);
        return (ActorPhotoOperationResult.Ok(), new ActorPhotoAlbumSummary(album.Id, album.Name, 0));
    }

    public async Task<ActorPhotoOperationResult> RenameAlbumAsync(int actorId, int albumId, string name, CancellationToken ct = default)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return ActorPhotoOperationResult.Fail("Album name is required.");
        if (normalized.Length > 100) return ActorPhotoOperationResult.Fail("Album names can be at most 100 characters.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var album = await db.ActorAlbums.FirstOrDefaultAsync(candidate => candidate.Id == albumId && candidate.ActorId == actorId, ct);
        if (album is null) return ActorPhotoOperationResult.Fail("Album not found.");
        if (await db.ActorAlbums.AnyAsync(candidate => candidate.ActorId == actorId && candidate.Id != albumId && candidate.Name == normalized, ct))
        {
            return ActorPhotoOperationResult.Fail("An album with this name already exists.");
        }

        album.Name = normalized;
        await db.SaveChangesAsync(ct);
        return ActorPhotoOperationResult.Ok();
    }

    public async Task<ActorPhotoOperationResult> DeleteAlbumAsync(int actorId, int albumId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var album = await db.ActorAlbums.FirstOrDefaultAsync(candidate => candidate.Id == albumId && candidate.ActorId == actorId, ct);
        if (album is null) return ActorPhotoOperationResult.Fail("Album not found.");

        var photos = await db.ActorPhotos.Where(photo => photo.AlbumId == albumId).ToListAsync(ct);
        db.ActorPhotos.RemoveRange(photos);
        db.ActorAlbums.Remove(album);
        await db.SaveChangesAsync(ct);
        await DeleteFilesAsync(photos);
        return ActorPhotoOperationResult.Ok();
    }

    public async Task<ActorPhotoOperationResult> MovePhotoAsync(int actorId, int photoId, int? albumId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photo = await db.ActorPhotos.FirstOrDefaultAsync(candidate => candidate.Id == photoId && candidate.ActorId == actorId, ct);
        if (photo is null) return ActorPhotoOperationResult.Fail("Photo not found.");
        if (albumId.HasValue && !await db.ActorAlbums.AnyAsync(album => album.Id == albumId && album.ActorId == actorId, ct))
        {
            return ActorPhotoOperationResult.Fail("Album not found.");
        }

        photo.AlbumId = albumId;
        await db.SaveChangesAsync(ct);
        return ActorPhotoOperationResult.Ok();
    }

    public async Task<ActorPhotoOperationResult> DeletePhotoAsync(int actorId, int photoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photo = await db.ActorPhotos.FirstOrDefaultAsync(candidate => candidate.Id == photoId && candidate.ActorId == actorId, ct);
        if (photo is null) return ActorPhotoOperationResult.Fail("Photo not found.");

        db.ActorPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
        await DeleteFilesAsync([photo]);
        return ActorPhotoOperationResult.Ok();
    }

    public async Task<byte[]?> GetPhotoBytesAsync(int actorId, int photoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photo = await db.ActorPhotos.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == photoId && candidate.ActorId == actorId, ct);
        if (photo is null) return null;

        if (photo.SourceStorageId is { } sourceStorageId && dataStore is not null)
        {
            return await dataStore.ReadAsync(sourceStorageId, photo.SourceExtension, ct);
        }

        var path = imageCache.GetStoragePath(photo.FullStorageId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public async Task<ImageServingResult> GetImageAsync(int photoId, string variant, CancellationToken ct = default)
    {
        if (variant == VariantOriginal)
        {
            // Serves the canonical uploaded bytes as-is, bypassing the WebP cache
            // entirely. Only available when a durable source store is configured and this photo
            // still has its original bytes (see ActorPhotoSummary.HasOriginal).
            if (dataStore is null) return ImageServingResult.NotFound();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var original = await db.ActorPhotos.AsNoTracking()
                .Where(photo => photo.Id == photoId)
                .Select(photo => new { photo.SourceStorageId, photo.SourceExtension })
                .FirstOrDefaultAsync(ct);
            return original?.SourceStorageId is { } storageId && await dataStore.OpenReadAsync(storageId, original.SourceExtension, ct) is { } source
                ? ImageServingResult.Stored(source)
                : ImageServingResult.NotFound();
        }

        if (variant is not (ActorImageCacheService.VariantThumb or ActorImageCacheService.VariantFull)) return ImageServingResult.NotFound();
        var (thumb, full) = await GetOrCreateBothAsync(photoId, ct);
        return ImageServingResult.LocalFileOrNotFound(variant == ActorImageCacheService.VariantThumb ? thumb : full);
    }

    public async Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(int photoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photo = await db.ActorPhotos.FirstOrDefaultAsync(photo => photo.Id == photoId, ct);
        if (photo is null) return (null, null);

        var thumbPath = imageCache.GetStoragePath(photo.ThumbStorageId);
        var fullPath = imageCache.GetStoragePath(photo.FullStorageId);
        var thumbExists = File.Exists(thumbPath);
        var fullExists = File.Exists(fullPath);

        var needsDbUpdate = false;

        if (thumbExists && fullExists)
        {
            if (photo.AspectRatio == null)
            {
                if (TryExtractAspectRatioFromFile(fullPath, out var ar) || TryExtractAspectRatioFromFile(thumbPath, out ar))
                {
                    photo.AspectRatio = ar;
                    needsDbUpdate = true;
                }
            }

            if (needsDbUpdate)
            {
                await db.SaveChangesAsync(ct);
            }

            return (thumbPath, fullPath);
        }

        if (photo.SourceStorageId is { } sourceStorageId && dataStore is not null)
        {
            var sourceBytes = await dataStore.ReadAsync(sourceStorageId, photo.SourceExtension, ct);
            if (sourceBytes is not null)
            {
                var (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertBytesToBothWebP(
                    sourceBytes, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);
                await WriteAsync(photo.ThumbStorageId, thumbBytes, ct);
                await WriteAsync(photo.FullStorageId, fullBytes, ct);

                if (photo.AspectRatio == null)
                {
                    if (TryExtractAspectRatioFromBytes(sourceBytes, out var ar))
                    {
                        photo.AspectRatio = ar;
                        needsDbUpdate = true;
                    }
                }

                if (needsDbUpdate)
                {
                    await db.SaveChangesAsync(ct);
                }

                return (thumbPath, fullPath);
            }
        }

        if (photo.AspectRatio == null)
        {
            if ((fullExists && TryExtractAspectRatioFromFile(fullPath, out var ar)) ||
                (thumbExists && TryExtractAspectRatioFromFile(thumbPath, out ar)))
            {
                photo.AspectRatio = ar;
                await db.SaveChangesAsync(ct);
            }
        }

        return (thumbExists ? thumbPath : null, fullExists ? fullPath : null);
    }

    public async Task DeleteAllForActorAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photos = await db.ActorPhotos.Where(photo => photo.ActorId == actorId).ToListAsync(ct);
        db.ActorPhotos.RemoveRange(photos);
        await db.SaveChangesAsync(ct);
        await DeleteFilesAsync(photos);
    }

    private async Task WriteAsync(Guid storageId, byte[] bytes, CancellationToken ct)
    {
        var path = imageCache.GetStoragePath(storageId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    private async Task DeleteFilesAsync(IEnumerable<ActorPhoto> photos)
    {
        foreach (var photo in photos)
        {
            DeleteFile(photo.ThumbStorageId);
            DeleteFile(photo.FullStorageId);
            await DeleteSourceAsync(photo.SourceStorageId, photo.SourceExtension);
        }
    }

    private void DeleteFile(Guid storageId)
    {
        try
        {
            File.Delete(imageCache.GetStoragePath(storageId));
        }
        catch (IOException)
        {
            // The row is already gone; a later cache cleanup can remove a transiently locked file.
        }
    }

    /// <summary>Cleanup after the rows are gone or were never saved, so it isn't cancelled.</summary>
    private async Task DeleteSourceAsync(Guid? storageId, string? extension)
    {
        if (storageId is { } sourceStorageId && dataStore is not null)
        {
            dataStore.DeleteStaged(sourceStorageId);
            await dataStore.DeleteAsync(sourceStorageId, extension, CancellationToken.None);
        }
    }

    private static bool TryExtractAspectRatioFromFile(string path, out double aspectRatio)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is not null && codec.Info.Height > 0)
            {
                aspectRatio = Math.Round((double)codec.Info.Width / codec.Info.Height, 4);
                return true;
            }
        }
        catch
        {
            // Ignore corrupted or unreadable files
        }

        aspectRatio = default;
        return false;
    }

    private static bool TryExtractAspectRatioFromBytes(byte[] bytes, out double aspectRatio)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes));
            if (codec is not null && codec.Info.Height > 0)
            {
                aspectRatio = Math.Round((double)codec.Info.Width / codec.Info.Height, 4);
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        aspectRatio = default;
        return false;
    }
}
