using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Images;

/// <summary>Resolves an actor's image out of the local library: searches the ".actors" subfolder
/// of every movie already linked to the actor through the indexed MovieActor relation (the same
/// linkage ActorDetail uses — not a library-wide scan) for a file named after them, converting/caching the
/// first match found as WebP. Shared by the /actor-image serving endpoint (Program.cs) and
/// ImageCacheTask's background scan.</summary>
public interface IActorImageCacheService
{
    /// <summary>The cached (and up to date) WebP file for this actor/variant, converting/caching it
    /// first if needed; for the source variant, the stored original bytes when there are any. Not
    /// found if no local image was found in any linked movie's ".actors" subfolder.</summary>
    Task<ImageServingResult> GetOrCreateAsync(int actorId, string variant, CancellationToken ct = default);

    /// <summary>Generates/ensures both Thumb and Full cached WebP files in a single decode pass.</summary>
    Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(int actorId, CancellationToken ct = default);

    /// <summary>Attempts to download an external image URL and cache it locally for the actor.
    /// Defunct domains (e.g. r18.com) are rejected immediately.</summary>
    Task<(string? ThumbPath, string? FullPath)> DownloadAndCacheRemoteImageAsync(
        int actorId,
        string remoteImageUrl,
        CancellationToken ct = default);

    /// <summary>Checks whether a custom portrait image has been uploaded for this actor.</summary>
    Task<bool> HasCustomImageAsync(int actorId, CancellationToken ct = default);

    /// <summary>Saves and converts an uploaded custom image for this actor into WebP variants
    /// (thumb and full) in the cache, optionally applying a normalized crop rectangle.</summary>
    Task<(string? ThumbPath, string? FullPath)> SaveCustomImageAsync(
        int actorId,
        byte[] imageBytes,
        NormalizedCropRect? cropRect = null,
        CancellationToken ct = default);

    /// <summary>Deletes any custom uploaded portrait image for this actor, removing the on-disk
    /// WebP files and database records so standard movie .actors resolution or remote thumbnail fallback resumes.</summary>
    Task<bool> DeleteCustomImageAsync(int actorId, CancellationToken ct = default);

    Task DeleteAllForActorAsync(int actorId, CancellationToken ct = default);

    /// <summary>Transfers cached and custom images from a source actor to a target canonical actor during a merge,
    /// cleaning up conflicting or redundant on-disk files and database records.</summary>
    Task TransferImagesAsync(int sourceActorId, int targetActorId, CancellationToken ct = default);

    /// <summary>Downloads and validates an external image from a remote URL for preview and cropping.</summary>
    Task<ActorImageDownloadResult> DownloadImageFromUrlAsync(string url, CancellationToken ct = default);
}

public sealed record ActorImageDownloadResult(
    bool Success,
    byte[]? Bytes = null,
    string? ContentType = null,
    string? ErrorMessage = null);

public class ActorImageCacheService(
    ILocalLibraryClient localLibraryClient,
    ILocalImageCache imageCache,
    IDbContextFactory<AppDbContext> dbFactory,
    IImageCacheMaintenanceService cacheMaintenance,
    IHttpClientFactory? httpClientFactory = null,
    ILogger<ActorImageCacheService>? logger = null,
    IActorImageDataStore? dataStore = null,
    ImageUploadSettings? uploadSettings = null) : IActorImageCacheService
{
    private readonly ImageUploadSettings uploadSettings = uploadSettings ?? ImageUploadSettings.Default;
    public const string VariantThumb = "thumb";
    public const string VariantFull = "full";
    public const string VariantSource = "source";
    public const string SourceMovieCodeCustom = "custom";
    public const string SourceMovieCodeRemote = "remote";

    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IImageCacheMaintenanceService cacheMaintenance = cacheMaintenance;
    private readonly IHttpClientFactory? httpClientFactory = httpClientFactory;
    private readonly ILogger<ActorImageCacheService> logger = logger ?? NullLogger<ActorImageCacheService>.Instance;
    private readonly IActorImageDataStore? dataStore = dataStore;

    public async Task<ImageServingResult> GetOrCreateAsync(int actorId, string variant, CancellationToken ct = default)
    {
        var isSourceVariant = string.Equals(variant, VariantSource, StringComparison.OrdinalIgnoreCase)
            || string.Equals(variant, "original", StringComparison.OrdinalIgnoreCase);

        if (isSourceVariant && await OpenSourceAsync(actorId, ct) is { } source)
        {
            return ImageServingResult.Stored(source);
        }

        var (thumbPath, fullPath) = await GetOrCreateBothAsync(actorId, ct);
        if (variant == VariantThumb) return ImageServingResult.LocalFileOrNotFound(thumbPath);

        // Building the cache may just have stored the source bytes.
        if (isSourceVariant && await OpenSourceAsync(actorId, ct) is { } storedSource)
        {
            return ImageServingResult.Stored(storedSource);
        }

        return ImageServingResult.LocalFileOrNotFound(fullPath);
    }

    private async Task<StoredObject?> OpenSourceAsync(int actorId, CancellationToken ct)
    {
        if (dataStore is null) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var source = await db.ActorImages
            .AsNoTracking()
            .Where(a => a.ActorId == actorId && a.SourceStorageId != null)
            .Select(a => new { a.SourceStorageId, a.SourceExtension })
            .FirstOrDefaultAsync(ct);

        return source?.SourceStorageId is { } id ? await dataStore.OpenReadAsync(id, source.SourceExtension, ct) : null;
    }

    public async Task<(string? ThumbPath, string? FullPath)> GetOrCreateBothAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actor = await db.Actors.AsNoTracking().Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == actorId, ct);
        if (actor is null) return (null, null);

        var existingList = await db.ActorImages.AsNoTracking().Where(a => a.ActorId == actorId).ToListAsync(ct);

        // Custom uploaded image takes precedence and is preserved across scans
        var customThumb = existingList.FirstOrDefault(a => a.Variant == VariantThumb && a.SourceMovieCode == SourceMovieCodeCustom);
        var customFull = existingList.FirstOrDefault(a => a.Variant == VariantFull && a.SourceMovieCode == SourceMovieCodeCustom);
        if (customThumb is not null && customFull is not null)
        {
            var customThumbPath = imageCache.GetStoragePath(customThumb.StorageId);
            var customFullPath = imageCache.GetStoragePath(customFull.StorageId);
            if (File.Exists(customThumbPath) && File.Exists(customFullPath))
            {
                return (customThumbPath, customFullPath);
            }

            if (customFull.SourceStorageId is { } customSourceStorageId && dataStore is not null)
            {
                var sourceBytes = await dataStore.ReadAsync(customSourceStorageId, customFull.SourceExtension, ct);
                if (sourceBytes is not null)
                {
                    var cropRect = customFull.CropX is { } x && customFull.CropY is { } y
                        && customFull.CropWidth is { } width && customFull.CropHeight is { } height
                        ? new NormalizedCropRect(x, y, width, height)
                        : null;
                    var (restoredThumbBytes, restoredFullBytes) = await ImageConversionGate.RunAsync(() => cropRect is null
                        ? ImageConverter.ConvertBytesToBothWebP(sourceBytes, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb)
                        : ImageConverter.CropAndConvertToBothWebP(sourceBytes, cropRect, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);
                    await WriteCacheVariantsAsync(customThumb.StorageId, customFull.StorageId, restoredThumbBytes, restoredFullBytes, ct);
                    return (imageCache.GetStoragePath(customThumb.StorageId), imageCache.GetStoragePath(customFull.StorageId));
                }
            }
        }

        var candidateNames = new List<string> { actor.DisplayName };
        if (!string.IsNullOrWhiteSpace(actor.LastName))
        {
            candidateNames.Add($"{actor.FirstName} {actor.LastName}".Trim());
        }
        else if (!string.IsNullOrWhiteSpace(actor.FirstName))
        {
            var parts = actor.FirstName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                candidateNames.Add($"{parts[1]} {parts[0]}");
            }
        }

        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji))
        {
            var kanji = actor.JapaneseNameKanji.Trim();
            candidateNames.Add(kanji);
            var compactKanji = kanji.Replace(" ", "");
            if (!string.Equals(compactKanji, kanji, StringComparison.OrdinalIgnoreCase))
            {
                candidateNames.Add(compactKanji);
            }
        }
        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKana))
        {
            var kana = actor.JapaneseNameKana.Trim();
            candidateNames.Add(kana);
            var compactKana = kana.Replace(" ", "");
            if (!string.Equals(compactKana, kana, StringComparison.OrdinalIgnoreCase))
            {
                candidateNames.Add(compactKana);
            }
        }
        if (!string.IsNullOrWhiteSpace(actor.R18DevName))
        {
            var r18 = actor.R18DevName.Trim();
            candidateNames.Add(r18);
            var parts = r18.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                candidateNames.Add($"{parts[1]} {parts[0]}");
            }
        }
        foreach (var alias in actor.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias.Name))
            {
                var aliasName = alias.Name.Trim();
                candidateNames.Add(aliasName);
                var compact = aliasName.Replace(" ", "");
                if (!string.Equals(compact, aliasName, StringComparison.OrdinalIgnoreCase))
                {
                    candidateNames.Add(compact);
                }
            }
        }

        var candidateCodes = await db.MovieActors
            .Where(link => link.ActorId == actorId && link.Movie.Code != null)
            .OrderByDescending(link => link.Movie.CreatedAt)
            .Select(link => link.Movie.Code!)
            .ToListAsync(ct);

        string? srcPath = null;
        string? sourceCode = null;

        if (candidateCodes.Count > 0)
        {
            foreach (var code in candidateCodes)
            {
                ct.ThrowIfCancellationRequested();

                string? found;
                try
                {
                    found = await localLibraryClient.ResolveActorImagePathAsync(code, candidateNames, ct);
                }
                catch (IOException ex)
                {
                    logger.LogDebug(ex, "IO error resolving actor image for {ActorName} in movie {Code}", actor.DisplayName, code);
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogDebug(ex, "Access denied resolving actor image for {ActorName} in movie {Code}", actor.DisplayName, code);
                    continue;
                }

                if (found is not null)
                {
                    srcPath = found;
                    sourceCode = code;
                    logger.LogDebug("Resolved actor image for {ActorName} ({ActorId}) from movie {Code}: {ImagePath}", actor.DisplayName, actorId, code, found);
                    break;
                }
            }
        }
        else
        {
            logger.LogDebug("No linked movies found for actor {ActorId} ({ActorName}) to resolve images from", actorId, actor.DisplayName);
        }

        if (srcPath is null || sourceCode is null)
        {
            var remoteThumb = existingList.FirstOrDefault(a => a.Variant == VariantThumb && a.SourceMovieCode == SourceMovieCodeRemote);
            var remoteFull = existingList.FirstOrDefault(a => a.Variant == VariantFull && a.SourceMovieCode == SourceMovieCodeRemote);
            if (remoteThumb is not null)
            {
                var thumbPath = imageCache.GetStoragePath(remoteThumb.StorageId);
                var fullPath = remoteFull is not null ? imageCache.GetStoragePath(remoteFull.StorageId) : thumbPath;
                if (File.Exists(thumbPath) && (File.Exists(fullPath) || imageCache.Settings.Mode == ImageCacheMode.Thumbnails))
                {
                    return (thumbPath, fullPath);
                }

                if (remoteThumb.SourceStorageId is { } remoteSourceStorageId && dataStore is not null)
                {
                    var sourceBytes = await dataStore.ReadAsync(remoteSourceStorageId, remoteThumb.SourceExtension, ct);
                    if (sourceBytes is not null)
                    {
                        var restoredFullStorageId = remoteFull?.StorageId ?? remoteThumb.StorageId;
                        var (restoredThumbBytes, restoredFullBytes) = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertBytesToBothWebP(
                            sourceBytes, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);
                        await WriteCacheVariantsAsync(remoteThumb.StorageId, restoredFullStorageId, restoredThumbBytes, restoredFullBytes, ct);
                        return (imageCache.GetStoragePath(remoteThumb.StorageId), imageCache.GetStoragePath(restoredFullStorageId));
                    }
                }
            }
            return (null, null);
        }

        var srcInfo = new FileInfo(srcPath);
        if (!srcInfo.Exists) return (null, null);
        if (imageCache.Settings.Mode == ImageCacheMode.Disabled) return (srcPath, srcPath);

        var thumbExisting = existingList.FirstOrDefault(a => a.Variant == VariantThumb);
        var fullExisting = existingList.FirstOrDefault(a => a.Variant == VariantFull);

        var thumbFresh = thumbExisting is not null && IsFresh(thumbExisting, srcInfo, sourceCode) && !imageCache.Settings.IsExpired(thumbExisting.UpdatedAt);
        var fullFresh = fullExisting is not null && IsFresh(fullExisting, srcInfo, sourceCode) && !imageCache.Settings.IsExpired(fullExisting.UpdatedAt);

        string? existingThumbPath = thumbFresh ? imageCache.GetStoragePath(thumbExisting!.StorageId) : null;
        string? existingFullPath = fullFresh ? imageCache.GetStoragePath(fullExisting!.StorageId) : null;

        if (existingThumbPath is not null && !File.Exists(existingThumbPath)) existingThumbPath = null;
        if (existingFullPath is not null && !File.Exists(existingFullPath)) existingFullPath = null;

        if (existingThumbPath is not null && existingFullPath is not null)
        {
            return (existingThumbPath, existingFullPath);
        }

        // Single-pass conversion
        var (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(
            () => ImageConverter.ConvertToBothWebP(srcPath, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);

        var thumbStorageId = thumbExisting?.StorageId ?? Guid.NewGuid();
        var fullStorageId = fullExisting?.StorageId ?? Guid.NewGuid();
        var sourceStorageId = dataStore is null ? (Guid?)null : Guid.NewGuid();
        string? sourceExtension = null;
        if (sourceStorageId is { } sourceId)
        {
            sourceExtension = await dataStore!.WriteAsync(sourceId, await File.ReadAllBytesAsync(srcPath, ct), ct);
        }

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);

        var updatedAt = DateTime.UtcNow;
        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: sourceCode,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceStorageId: sourceStorageId,
            sourceExtension: sourceExtension,
            sourceLength: srcInfo.Length,
            sourceLastWriteUtc: srcInfo.LastWriteTimeUtc,
            updatedAt: updatedAt,
            ct: ct);

        await DeletePreviousSourcesAsync(existingList, sourceStorageId);

        var resultFullPath = destFullPath;
        var addedBytes = (long)thumbBytes.Length + fullBytes.Length;
        if (imageCache.Settings.Mode == ImageCacheMode.Thumbnails)
        {
            File.Delete(destFullPath);
            addedBytes = thumbBytes.Length;
            await db.ActorImages.Where(image => image.ActorId == actorId && image.Variant == VariantFull)
                .ExecuteDeleteAsync(ct);
            resultFullPath = srcPath;
        }

        await cacheMaintenance.EnforceSizeLimitAsync(addedBytes, ct);

        return (destThumbPath, resultFullPath);
    }

    public async Task<bool> HasCustomImageAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ActorImages.AnyAsync(a => a.ActorId == actorId && a.SourceMovieCode == SourceMovieCodeCustom, ct);
    }

    public async Task<(string? ThumbPath, string? FullPath)> SaveCustomImageAsync(
        int actorId,
        byte[] imageBytes,
        NormalizedCropRect? cropRect = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actor = await db.Actors.FindAsync([actorId], ct);
        if (actor is null) return (null, null);

        var (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => cropRect is not null
            ? ImageConverter.CropAndConvertToBothWebP(imageBytes, cropRect, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb)
            : ImageConverter.ConvertBytesToBothWebP(imageBytes, qualityFull: imageCache.QualityFull, qualityThumb: imageCache.QualityThumb), ct);

        var sourceStorageId = dataStore is null ? (Guid?)null : Guid.NewGuid();
        string? sourceExtension = null;
        if (sourceStorageId is { } sourceId)
        {
            sourceExtension = await dataStore!.WriteAsync(sourceId, imageBytes, ct);
        }

        var existingList = await db.ActorImages.AsNoTracking().Where(a => a.ActorId == actorId).ToListAsync(ct);
        var thumbExisting = existingList.FirstOrDefault(a => a.Variant == VariantThumb);
        var fullExisting = existingList.FirstOrDefault(a => a.Variant == VariantFull);

        var thumbStorageId = thumbExisting?.StorageId ?? Guid.NewGuid();
        var fullStorageId = fullExisting?.StorageId ?? Guid.NewGuid();

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);

        var updatedAt = DateTime.UtcNow;
        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: SourceMovieCodeCustom,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceStorageId: sourceStorageId,
            sourceExtension: sourceExtension,
            cropX: cropRect?.X,
            cropY: cropRect?.Y,
            cropWidth: cropRect?.Width,
            cropHeight: cropRect?.Height,
            sourceLength: imageBytes.Length,
            sourceLastWriteUtc: updatedAt,
            updatedAt: updatedAt,
            ct: ct);

        await DeletePreviousSourcesAsync(existingList, sourceStorageId);

        await cacheMaintenance.EnforceSizeLimitAsync((long)thumbBytes.Length + fullBytes.Length, ct);

        return (destThumbPath, destFullPath);
    }

    public async Task<bool> DeleteCustomImageAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var customImages = await db.ActorImages
            .Where(a => a.ActorId == actorId && a.SourceMovieCode == SourceMovieCodeCustom)
            .ToListAsync(ct);

        if (customImages.Count == 0) return false;

        foreach (var img in customImages)
        {
            TryDeleteStorageFile(img.StorageId);
            if (img.SourceStorageId is { } sourceStorageId) await DeleteSourceAsync(sourceStorageId, img.SourceExtension);
        }

        db.ActorImages.RemoveRange(customImages);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task DeleteAllForActorAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var images = await db.ActorImages.Where(image => image.ActorId == actorId).ToListAsync(ct);
        foreach (var image in images)
        {
            TryDeleteStorageFile(image.StorageId);
            if (image.SourceStorageId is { } sourceStorageId) await DeleteSourceAsync(sourceStorageId, image.SourceExtension);
        }
        db.ActorImages.RemoveRange(images);
        await db.SaveChangesAsync(ct);
    }

    public async Task TransferImagesAsync(int sourceActorId, int targetActorId, CancellationToken ct = default)
    {
        if (sourceActorId == targetActorId) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sourceImages = await db.ActorImages.Where(a => a.ActorId == sourceActorId).ToListAsync(ct);
        if (sourceImages.Count == 0) return;

        var targetImages = await db.ActorImages.Where(a => a.ActorId == targetActorId).ToListAsync(ct);
        var targetHasCustom = targetImages.Any(a => a.SourceMovieCode == SourceMovieCodeCustom);
        var sourceHasCustom = sourceImages.Any(a => a.SourceMovieCode == SourceMovieCodeCustom);

        if (sourceHasCustom)
        {
            if (targetHasCustom)
            {
                // Target's custom images take precedence; discard source's custom images
                foreach (var img in sourceImages.Where(a => a.SourceMovieCode == SourceMovieCodeCustom))
                {
                    TryDeleteStorageFile(img.StorageId);
                    db.ActorImages.Remove(img);
                }
            }
            else
            {
                // Source's custom images supersede target's existing non-custom images
                foreach (var targetImg in targetImages)
                {
                    TryDeleteStorageFile(targetImg.StorageId);
                    db.ActorImages.Remove(targetImg);
                }
                foreach (var img in sourceImages.Where(a => a.SourceMovieCode == SourceMovieCodeCustom))
                {
                    img.ActorId = targetActorId;
                }
            }
        }

        // Handle remaining non-custom source images
        var remainingSourceImages = sourceImages.Where(a => a.ActorId == sourceActorId).ToList();
        var targetNonCustomImages = await db.ActorImages
            .Where(a => a.ActorId == targetActorId && a.SourceMovieCode != SourceMovieCodeCustom)
            .ToListAsync(ct);

        foreach (var srcImg in remainingSourceImages)
        {
            var targetMatch = targetNonCustomImages.FirstOrDefault(t => string.Equals(t.Variant, srcImg.Variant, StringComparison.OrdinalIgnoreCase));
            if (targetMatch is null)
            {
                srcImg.ActorId = targetActorId;
                targetNonCustomImages.Add(srcImg);
            }
            else if (targetMatch.SourceMovieCode == SourceMovieCodeRemote && srcImg.SourceMovieCode != SourceMovieCodeRemote)
            {
                // Source's local movie image supersedes target's remote fallback image
                TryDeleteStorageFile(targetMatch.StorageId);
                db.ActorImages.Remove(targetMatch);
                targetNonCustomImages.Remove(targetMatch);
                srcImg.ActorId = targetActorId;
                targetNonCustomImages.Add(srcImg);
            }
            else
            {
                TryDeleteStorageFile(srcImg.StorageId);
                db.ActorImages.Remove(srcImg);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<(string? ThumbPath, string? FullPath)> DownloadAndCacheRemoteImageAsync(
        int actorId,
        string remoteImageUrl,
        CancellationToken ct = default)
    {
        if (!ActorImageUrlHelper.IsUsableRemoteActorImageUrl(remoteImageUrl))
        {
            return (null, null);
        }

        if (imageCache.Settings.Mode == ImageCacheMode.Disabled)
        {
            return (null, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actorExists = await db.Actors.AnyAsync(a => a.Id == actorId, ct);
        if (!actorExists)
        {
            return (null, null);
        }

        var existingList = await db.ActorImages.AsNoTracking().Where(a => a.ActorId == actorId).ToListAsync(ct);

        // If a custom image already exists, do not overwrite
        var customThumb = existingList.FirstOrDefault(a => a.Variant == VariantThumb && a.SourceMovieCode == SourceMovieCodeCustom);
        var customFull = existingList.FirstOrDefault(a => a.Variant == VariantFull && a.SourceMovieCode == SourceMovieCodeCustom);
        if (customThumb is not null && customFull is not null)
        {
            var thumbPath = imageCache.GetStoragePath(customThumb.StorageId);
            var fullPath = imageCache.GetStoragePath(customFull.StorageId);
            if (File.Exists(thumbPath) && File.Exists(fullPath))
            {
                return (thumbPath, fullPath);
            }
        }

        var remoteThumb = existingList.FirstOrDefault(a => a.Variant == VariantThumb && a.SourceMovieCode == SourceMovieCodeRemote);
        var remoteFull = existingList.FirstOrDefault(a => a.Variant == VariantFull && a.SourceMovieCode == SourceMovieCodeRemote);

        var thumbFresh = remoteThumb is not null
            && string.Equals(remoteThumb.SourceUrl, remoteImageUrl, StringComparison.OrdinalIgnoreCase)
            && !imageCache.Settings.IsExpired(remoteThumb.UpdatedAt);
        var fullFresh = remoteFull is not null
            && string.Equals(remoteFull.SourceUrl, remoteImageUrl, StringComparison.OrdinalIgnoreCase)
            && !imageCache.Settings.IsExpired(remoteFull.UpdatedAt);

        string? existingThumbPath = thumbFresh ? imageCache.GetStoragePath(remoteThumb!.StorageId) : null;
        string? existingFullPath = fullFresh ? imageCache.GetStoragePath(remoteFull!.StorageId) : null;

        if (existingThumbPath is not null && !File.Exists(existingThumbPath)) existingThumbPath = null;
        if (existingFullPath is not null && !File.Exists(existingFullPath)) existingFullPath = null;

        if (existingThumbPath is not null && (existingFullPath is not null || imageCache.Settings.Mode == ImageCacheMode.Thumbnails))
        {
            return (existingThumbPath, existingFullPath ?? existingThumbPath);
        }

        if (httpClientFactory is null)
        {
            logger?.LogWarning("Cannot download remote actor image for actor {ActorId}: IHttpClientFactory is not available", actorId);
            return (null, null);
        }

        byte[] sourceBytes;
        try
        {
            var client = httpClientFactory.CreateClient();
            sourceBytes = await client.GetByteArrayAsync(remoteImageUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "Failed to download remote actor image for actor {ActorId} from {Url}", actorId, remoteImageUrl);
            return (null, null);
        }

        byte[] thumbBytes;
        byte[] fullBytes;
        try
        {
            (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertBytesToBothWebP(
                sourceBytes,
                qualityFull: imageCache.QualityFull,
                qualityThumb: imageCache.QualityThumb), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Failed to decode/convert downloaded actor image for actor {ActorId} from {Url}", actorId, remoteImageUrl);
            return (null, null);
        }

        var thumbStorageId = remoteThumb?.StorageId ?? Guid.NewGuid();
        var fullStorageId = remoteFull?.StorageId ?? Guid.NewGuid();
        var sourceStorageId = dataStore is null ? (Guid?)null : Guid.NewGuid();
        string? sourceExtension = null;
        if (sourceStorageId is { } sourceId)
        {
            sourceExtension = await dataStore!.WriteAsync(sourceId, sourceBytes, ct);
        }

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);

        var updatedAt = DateTime.UtcNow;
        await db.UpsertActorImagePairAsync(
            actorId: actorId,
            sourceMovieCode: SourceMovieCodeRemote,
            thumbStorageId: thumbStorageId,
            fullStorageId: fullStorageId,
            sourceStorageId: sourceStorageId,
            sourceExtension: sourceExtension,
            sourceLength: sourceBytes.Length,
            sourceLastWriteUtc: updatedAt,
            sourceUrl: remoteImageUrl,
            updatedAt: updatedAt,
            ct: ct);

        await DeletePreviousSourcesAsync(existingList, sourceStorageId);

        var resultFullPath = destFullPath;
        var addedBytes = (long)thumbBytes.Length + fullBytes.Length;
        if (imageCache.Settings.Mode == ImageCacheMode.Thumbnails)
        {
            File.Delete(destFullPath);
            addedBytes = thumbBytes.Length;
            await db.ActorImages
                .Where(image => image.ActorId == actorId && image.Variant == VariantFull)
                .ExecuteDeleteAsync(ct);
            resultFullPath = destThumbPath;
        }

        await cacheMaintenance.EnforceSizeLimitAsync(addedBytes, ct);

        return (destThumbPath, resultFullPath);
    }

    public async Task<ActorImageDownloadResult> DownloadImageFromUrlAsync(string url, CancellationToken ct = default)
    {
        var urlFailure = RemoteImageDownloader.TryParseUrl(url, out var uri);
        if (urlFailure == RemoteImageDownloadFailure.None && httpClientFactory is null)
        {
            return new(false, ErrorMessage: "HTTP client service is not available.");
        }

        var download = urlFailure != RemoteImageDownloadFailure.None
            ? new RemoteImageDownloadResult(urlFailure)
            : await RemoteImageDownloader.DownloadAsync(
                httpClientFactory!.CreateClient(RemoteImageDownloader.ImportClientName),
                uri!,
                uploadSettings.MaxSizeBytes,
                ct,
                isAcceptableMediaType: mediaType => mediaType is null || mediaType.StartsWith("image/") || mediaType == "application/octet-stream");

        if (!download.Success)
        {
            return new(false, ErrorMessage: RemoteImageDownloadMessages.Describe(download, uploadSettings.MaxSizeMb));
        }

        var imageBytes = download.Bytes!;
        using var codecStream = new MemoryStream(imageBytes);
        using var codec = SkiaSharp.SKCodec.Create(codecStream);
        if (codec is null)
        {
            return new(false, ErrorMessage: "Unsupported or corrupted image file. Please provide a valid JPEG, PNG, or WebP image.");
        }

        string contentType;
        switch (codec.EncodedFormat)
        {
            case SkiaSharp.SKEncodedImageFormat.Jpeg:
                contentType = "image/jpeg";
                break;
            case SkiaSharp.SKEncodedImageFormat.Png:
                contentType = "image/png";
                break;
            case SkiaSharp.SKEncodedImageFormat.Webp:
                contentType = "image/webp";
                break;
            default:
                return new(false, ErrorMessage: $"Unsupported image format '{codec.EncodedFormat}'. Please provide a JPEG, PNG, or WebP image.");
        }

        return new(true, Bytes: imageBytes, ContentType: contentType);
    }

    /// <summary>
    /// Deletes every source the actor's rows referenced before an upsert replaced them, whatever its origin
    /// (library, remote, or custom), since the upsert leaves nothing else pointing at it.
    /// </summary>
    private async Task DeletePreviousSourcesAsync(IEnumerable<ActorImage> previousImages, Guid? currentSourceStorageId)
    {
        foreach (var previous in previousImages
            .Where(image => image.SourceStorageId is { } id && id != currentSourceStorageId)
            .DistinctBy(image => image.SourceStorageId))
        {
            await DeleteSourceAsync(previous.SourceStorageId!.Value, previous.SourceExtension);
        }
    }

    /// <summary>Runs after the rows no longer reference the source, so it isn't cancelled.</summary>
    private Task DeleteSourceAsync(Guid sourceStorageId, string? sourceExtension) =>
        dataStore?.DeleteAsync(sourceStorageId, sourceExtension, CancellationToken.None) ?? Task.CompletedTask;

    private void TryDeleteStorageFile(Guid storageId)
    {
        var path = imageCache.GetStoragePath(storageId);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task WriteCacheVariantsAsync(Guid thumbStorageId, Guid fullStorageId, byte[] thumbBytes, byte[] fullBytes, CancellationToken ct)
    {
        var thumbPath = imageCache.GetStoragePath(thumbStorageId);
        var fullPath = imageCache.GetStoragePath(fullStorageId);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(thumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(fullPath, fullBytes, ct);
    }

    private static bool IsFresh(ActorImage cached, FileInfo srcInfo, string sourceCode) =>
        string.Equals(cached.SourceMovieCode, sourceCode, StringComparison.OrdinalIgnoreCase)
        && cached.SourceLength == srcInfo.Length
        && cached.SourceLastWriteUtc >= srcInfo.LastWriteTimeUtc;
}
