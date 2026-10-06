using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Tasks;

/// <summary>Combines what used to be two separate tasks — LocalImageCacheTask for movie images,
/// ActorImageCacheTask for actor images — into one. Both always shared the same underlying WebP
/// cache directory (ILocalImageCache), and each task's own cleanup sweep had to separately query
/// the *other* task's table just to avoid deleting its live files out from under it (see the old
/// tasks' CleanupAsync comments). One task means one combined "live StorageIds" set and one
/// filesystem sweep, instead of two tasks cross-referencing each other's tables to stay out of
/// each other's way.
///
/// 1. Caches every tracked Movie's poster/fanart/extrafanart images (unchanged from
///    LocalImageCacheTask — see ILocalImageCacheService). Images already cached and up to date are
///    skipped, so repeat runs after the first full scan are fast.
/// 2. Caches every tracked Actor's image the same way (unchanged from ActorImageCacheTask — see
///    IActorImageCacheService).
/// 3. One combined cleanup pass: prunes CachedImages rows (movies no longer tracked, or an
///    extrafanart index beyond a still-live folder's current count) and ActorImages rows (actors
///    no longer tracked), then sweeps the whole cache directory for any file neither table
///    references any more.</summary>
public class ImageCacheTask(
    ILocalLibraryClient localLibraryClient,
    ILocalImageCacheService movieImageCacheService,
    IActorImageCacheService actorImageCacheService,
    ILocalImageCache imageCache,
    IDbContextFactory<AppDbContext> dbFactory,
    IActorImageDataStore? dataStore = null,
    ILogger<ImageCacheTask>? logger = null,
    IActorPhotoService? actorPhotoService = null) : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly ILocalImageCacheService movieImageCacheService = movieImageCacheService;
    private readonly IActorImageCacheService actorImageCacheService = actorImageCacheService;
    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IActorImageDataStore? dataStore = dataStore;
    private readonly ILogger<ImageCacheTask> logger = logger ?? NullLogger<ImageCacheTask>.Instance;
    private readonly IActorPhotoService? actorPhotoService = actorPhotoService;

    public string Name => "Image Cache";

    public string Description =>
        "Caches poster/fanart/extrafanart images for every tracked movie, actor portraits, and actor photos, " +
        "then prunes cached files for anything no longer tracked.";

    public TimeSpan GetInterval() => DefaultInterval;

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        var (movieCodes, moviesProcessed, moviesFailed, sourcelessRoles, allRootsOffline) = await CacheMoviesAsync(progress, ct);
        var (actorIds, actorsProcessed, actorsFailed) = await CacheActorsAsync(progress, ct);
        var (photoIds, photosProcessed, photosFailed) = await CacheActorPhotosAsync(progress, ct);
        var (rowsPruned, filesPruned) = await CleanupAsync(movieCodes, actorIds, sourcelessRoles, allRootsOffline, ct);

        var imagesProcessed = moviesProcessed + actorsProcessed + photosProcessed;
        var imagesFailed = moviesFailed + actorsFailed + photosFailed;

        var summary = $"scanned {movieCodes.Count} movies / {actorIds.Count} actors / {photoIds.Count} photos, {imagesProcessed} images cached/up to date";
        if (imagesFailed > 0) summary += $", {imagesFailed} failed";
        if (rowsPruned > 0 || filesPruned > 0) summary += $", pruned {rowsPruned} orphaned rows / {filesPruned} orphaned files";
        return summary;
    }

    /// <summary>Discovers *which* movies to process from the Movies table, not by enumerating the
    /// configured root paths directly: a library folder can sit on disk long before (or without)
    /// ever being added here, and this task has no business touching/caching those — that's exactly
    /// what "Discover from Local Library" is for (see LibraryImport.razor). A tracked movie with no
    /// matching local folder at all (Missing, nothing downloaded yet) is simply skipped by
    /// CacheMovieBothAsync below (GetOrCreateBothAsync returns null — no local source, not an
    /// error).</summary>
    private async Task<(List<string> Codes, int Processed, int Failed, HashSet<(string Code, string Role)> SourcelessRoles, bool AllRootsOffline)> CacheMoviesAsync(IProgress<TaskProgress> progress, CancellationToken ct)
    {
        const string stage = "Caching movie images";
        progress.Report(new TaskProgress(0, null, stage));

        List<string> codes;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            codes = await db.Movies.Where(m => m.Code != null).Select(m => m.Code!).ToListAsync(ct);
        }

        var scanned = 0;
        var processed = 0;
        var failed = 0;

        var roots = await localLibraryClient.GetRootPathsAsync(ct) ?? [];
        var allRootsOffline = roots.Count > 0 && roots.All(r => !Directory.Exists(r));
        if (allRootsOffline)
        {
            logger.LogWarning("Configured media library roots are currently inaccessible — skipping sourceless image cleanup");
        }

        // Poster/fanart (index 0) rows whose local source no longer exists — e.g. the movie's
        // poster.jpg was deleted while the movie is still tracked — so any cached copy still
        // referenced by CachedImages is now stale and should be pruned by CleanupAsync. This is
        // only tracked for the single-source poster/fanart roles; extrafanart's equivalent case
        // (an index beyond the folder's current file count) is already handled by CleanupAsync
        // via ListExtraFanartFileNamesAsync.
        var sourcelessRoles = new HashSet<(string Code, string Role)>();

        foreach (var code in codes)
        {
            if (ct.IsCancellationRequested) break;
            scanned++;
            progress.Report(new TaskProgress(scanned, codes.Count, stage));

            switch (await CacheMovieBothAsync(code, LocalImageCacheService.RolePoster, 0, ct))
            {
                case true: processed += 2; break;
                case false: failed += 2; break;
                case null when !allRootsOffline: sourcelessRoles.Add((code, LocalImageCacheService.RolePoster)); break;
            }
            switch (await CacheMovieBothAsync(code, LocalImageCacheService.RoleFanart, 0, ct))
            {
                case true: processed += 2; break;
                case false: failed += 2; break;
                case null when !allRootsOffline: sourcelessRoles.Add((code, LocalImageCacheService.RoleFanart)); break;
            }

            List<string> extraNames;
            try
            {
                extraNames = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct) ?? new List<string>();
            }
            catch (IOException)
            {
                extraNames = new List<string>();
            }

            for (var i = 0; i < extraNames.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                switch (await CacheMovieBothAsync(code, LocalImageCacheService.RoleExtraFanart, i, ct))
                {
                    case true: processed += 2; break;
                    case false: failed += 2; break;
                }
            }
        }

        return (codes, processed, failed, sourcelessRoles, allRootsOffline);
    }

    private async Task<(List<int> ActorIds, int Processed, int Failed)> CacheActorsAsync(IProgress<TaskProgress> progress, CancellationToken ct)
    {
        const string stage = "Caching actor images";
        progress.Report(new TaskProgress(0, null, stage));

        List<int> actorIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            actorIds = await db.Actors.Select(a => a.Id).ToListAsync(ct);
        }

        var scanned = 0;
        var processed = 0;
        var failed = 0;

        foreach (var actorId in actorIds)
        {
            if (ct.IsCancellationRequested) break;
            scanned++;
            progress.Report(new TaskProgress(scanned, actorIds.Count, stage));

            switch (await CacheActorBothAsync(actorId, ct))
            {
                case true: processed += 2; break;
                case false: failed += 2; break;
            }
        }

        return (actorIds, processed, failed);
    }

    /// <summary>Converts/caches both Thumb and Full images in a single decode pass if a local source exists
    /// (GetOrCreateBothAsync skips the work if both cached copies are already fresh).</summary>
    private async Task<bool?> CacheMovieBothAsync(string code, string role, int index, CancellationToken ct)
    {
        try
        {
            var (thumb, full) = await movieImageCacheService.GetOrCreateBothAsync(code, role, index, ct);
            return thumb is not null || full is not null ? true : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Broad by design: a single bad source image (undecodable, wrong format, corrupt
            // file) can fail at several different points in the decode/convert/write pipeline
            // with different exception types (InvalidOperationException from ImageConverter,
            // ArgumentException from ActorImageDataStore, IOException/UnauthorizedAccessException
            // from disk I/O). Skip this one item and keep the run going rather than enumerating
            // every failure mode one exception type at a time.
            logger.LogWarning(ex, "Failed to cache image for movie {Code} ({Role} index {Index}) — skipping", code, role, index);
            return false;
        }
    }

    private async Task<bool?> CacheActorBothAsync(int actorId, CancellationToken ct)
    {
        try
        {
            var (thumb, full) = await actorImageCacheService.GetOrCreateBothAsync(actorId, ct);
            return thumb is not null || full is not null ? true : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cache image for actor {ActorId} — skipping", actorId);
            return false;
        }
    }

    private async Task<(List<int> PhotoIds, int Processed, int Failed)> CacheActorPhotosAsync(IProgress<TaskProgress> progress, CancellationToken ct)
    {
        const string stage = "Caching actor photos";
        progress.Report(new TaskProgress(0, null, stage));

        List<int> photoIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            photoIds = await db.ActorPhotos.Select(p => p.Id).ToListAsync(ct);
        }

        var scanned = 0;
        var processed = 0;
        var failed = 0;

        foreach (var photoId in photoIds)
        {
            if (ct.IsCancellationRequested) break;
            scanned++;
            progress.Report(new TaskProgress(scanned, photoIds.Count, stage));

            switch (await CacheActorPhotoBothAsync(photoId, ct))
            {
                case true: processed += 2; break;
                case false: failed += 2; break;
            }
        }

        return (photoIds, processed, failed);
    }

    private async Task<bool?> CacheActorPhotoBothAsync(int photoId, CancellationToken ct)
    {
        if (actorPhotoService is null) return null;
        try
        {
            var (thumb, full) = await actorPhotoService.GetOrCreateBothAsync(photoId, ct);
            return thumb is not null || full is not null ? true : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cache photo {PhotoId} — skipping", photoId);
            return false;
        }
    }

    /// <summary>Removes CachedImages rows for movies no longer tracked (or an extrafanart index
    /// beyond a still-live folder's current image count, or a poster/fanart row whose local source
    /// file no longer exists even though the movie itself is still tracked — see sourcelessRoles)
    /// ActorImages rows, and ActorPhotos rows for actors no longer tracked, then sweeps the shared storage directory
    /// for files neither table references any more — computing one combined "kept" StorageId set
    /// up front so this single sweep can't mistake the other kind of image for garbage the way two
    /// separate sweeps had to guard against.</summary>
    private async Task<(int RowsPruned, int FilesPruned)> CleanupAsync(List<string> liveCodes, List<int> liveActorIds, HashSet<(string Code, string Role)> sourcelessRoles, bool allRootsOffline, CancellationToken ct)
    {
        var liveCodeSet = new HashSet<string>(liveCodes, StringComparer.OrdinalIgnoreCase);
        var liveActorIdSet = liveActorIds.ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var allCachedImages = await db.CachedImages.ToListAsync(ct);
        var allActorImages = await db.ActorImages.ToListAsync(ct);
        var allActorPhotos = await db.ActorPhotos.ToListAsync(ct);

        var imagesToDelete = new List<CachedImage>(allCachedImages.Where(c => !liveCodeSet.Contains(c.Code)));
        imagesToDelete.AddRange(allCachedImages.Except(imagesToDelete)
            .Where(c => c.SourceUrl == null && sourcelessRoles.Contains((c.Code, c.Role))));

        if (!allRootsOffline)
        {
            var extraCountsByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var remainingExtraFanart = allCachedImages.Except(imagesToDelete)
                .Where(c => c.Role == LocalImageCacheService.RoleExtraFanart)
                .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase);

            foreach (var group in remainingExtraFanart)
            {
                if (ct.IsCancellationRequested) break;

                if (!extraCountsByCode.TryGetValue(group.Key, out var liveCount))
                {
                    List<string> names;
                    try
                    {
                        names = await localLibraryClient.ListExtraFanartFileNamesAsync(group.Key, ct) ?? new List<string>();
                    }
                    catch (IOException)
                    {
                        names = new List<string>();
                    }
                    liveCount = names.Count;
                    extraCountsByCode[group.Key] = liveCount;
                }

                imagesToDelete.AddRange(group.Where(c => c.Index >= liveCount));
            }
        }

        var actorImagesToDelete = allActorImages.Where(a => !liveActorIdSet.Contains(a.ActorId)).ToList();
        var actorPhotosToDelete = allActorPhotos.Where(photo => !liveActorIdSet.Contains(photo.ActorId)).ToList();

        // File names, not just storage ids: a hover preview written before is a .webp
        // under its row's storage id, while the row now expects a .webm, so it's swept as stale.
        var keptFileNames = allCachedImages.Except(imagesToDelete).Select(c => imageCache.GetStoragePath(c))
            .Concat(allActorImages.Except(actorImagesToDelete).Select(a => imageCache.GetStoragePath(a.StorageId)))
            .Concat(allActorPhotos.Except(actorPhotosToDelete).SelectMany(photo => new[] { imageCache.GetStoragePath(photo.ThumbStorageId), imageCache.GetStoragePath(photo.FullStorageId) }))
            .Select(path => Path.GetFileName(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filesPruned = 0;
        foreach (var row in imagesToDelete)
        {
            if (TryDeleteFile(imageCache.GetStoragePath(row))) filesPruned++;
        }
        foreach (var row in actorImagesToDelete)
        {
            if (TryDeleteFile(imageCache.GetStoragePath(row.StorageId))) filesPruned++;
            if (row.SourceStorageId is { } sourceStorageId && dataStore is not null) await dataStore.DeleteAsync(sourceStorageId, row.SourceExtension, ct);
        }
        foreach (var photo in actorPhotosToDelete)
        {
            if (TryDeleteFile(imageCache.GetStoragePath(photo.ThumbStorageId))) filesPruned++;
            if (TryDeleteFile(imageCache.GetStoragePath(photo.FullStorageId))) filesPruned++;
            if (photo.SourceStorageId is { } sourceStorageId && dataStore is not null) await dataStore.DeleteAsync(sourceStorageId, photo.SourceExtension, ct);
        }

        if (imagesToDelete.Count > 0) db.CachedImages.RemoveRange(imagesToDelete);
        if (actorImagesToDelete.Count > 0) db.ActorImages.RemoveRange(actorImagesToDelete);
        if (actorPhotosToDelete.Count > 0) db.ActorPhotos.RemoveRange(actorPhotosToDelete);
        if (imagesToDelete.Count > 0 || actorImagesToDelete.Count > 0 || actorPhotosToDelete.Count > 0) await db.SaveChangesAsync(ct);

        filesPruned += SweepUnreferencedFiles(keptFileNames, ct);

        return (imagesToDelete.Count + actorImagesToDelete.Count + actorPhotosToDelete.Count, filesPruned);
    }

    /// <summary>Deletes any cached file under the cache root whose name (its StorageId and extension) isn't in
    /// keptFileNames — the harmless leftover from a losing side of a concurrent cache-miss race,
    /// since the winning side's row is what keptFileNames reflects.</summary>
    private int SweepUnreferencedFiles(HashSet<string> keptFileNames, CancellationToken ct)
    {
        if (!Directory.Exists(imageCache.RootPath)) return 0;

        IEnumerable<string> files;
        try
        {
            files = ILocalImageCache.EnumerateFiles(imageCache.RootPath);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            if (!keptFileNames.Contains(Path.GetFileName(file)))
            {
                if (TryDeleteFile(file)) deleted++;
            }
        }

        return deleted;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
