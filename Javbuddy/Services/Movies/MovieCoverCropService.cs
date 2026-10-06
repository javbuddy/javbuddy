using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Movies;

public record MovieCropSource(string Key, string Label, string Url);

/// <param name="HasLocalFolder">Whether a save writes poster.jpg into the movie's library folder,
/// or (false) only replaces Javbuddy's cached poster.</param>
public record MovieCropSources(
    string Code,
    string? DefaultSourceKey,
    IReadOnlyList<MovieCropSource> Sources,
    bool HasLocalFolder = false);

public record MovieCoverCropDownloadResult(bool Success, byte[]? Bytes, string? ContentType, string? ErrorMessage);

/// <param name="Message">User-facing summary of where the crop ended up, set on success.</param>
/// <param name="IsWarning">The crop was saved, but a leftover poster file couldn't be removed.</param>
public record MovieCoverCropSaveResult(bool Success, string? ErrorMessage = null, string? Message = null, bool IsWarning = false)
{
    public static MovieCoverCropSaveResult Fail(string error) => new(false, error);
}

public interface IMovieCoverCropService
{
    Task<MovieCropSources> GetCropSourcesAsync(string code, CancellationToken ct = default);
    Task<byte[]?> GetSourceBytesAsync(string code, string sourceKey, CancellationToken ct = default);
    Task<MovieCoverCropDownloadResult> DownloadImageFromUrlAsync(string url, CancellationToken ct = default);
    Task<MovieCoverCropSaveResult> SaveCroppedCoverAsync(
        string code,
        byte[] sourceBytes,
        NormalizedCropRect cropRect,
        CancellationToken ct = default);
}

public class MovieCoverCropService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    IHttpClientFactory httpClientFactory,
    ImageUploadSettings? uploadSettings = null,
    MovieChangeNotifier? movieChangeNotifier = null,
    ILogger<MovieCoverCropService>? logger = null) : IMovieCoverCropService
{
    private static readonly string[] FanartCandidates = { "fanart.jpg", "fanart.jpeg", "fanart.png", "fanart.webp" };
    private const string PosterFileName = "poster.jpg";
    private static readonly string[] PosterCandidates =
    {
        "poster.jpg", "poster.jpeg", "poster.png", "poster.webp",
        "folder.jpg", "folder.jpeg", "folder.png", "folder.webp",
    };

    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly ILocalImageCache imageCache = imageCache;
    private readonly IImageCacheMaintenanceService cacheMaintenance = cacheMaintenance;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly ImageUploadSettings uploadSettings = uploadSettings ?? ImageUploadSettings.Default;
    private readonly MovieChangeNotifier? movieChangeNotifier = movieChangeNotifier;
    private readonly ILogger<MovieCoverCropService> logger = logger ?? NullLogger<MovieCoverCropService>.Instance;

    public async Task<MovieCropSources> GetCropSourcesAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new MovieCropSources(string.Empty, null, []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code, ct);
        var localLookup = await localLibraryClient.TryGetMetadataAsync(code, ct, includeMediaInfo: false);
        var folder = localLookup.Found ? localLookup.FolderPath : null;
        var sources = new List<MovieCropSource>();

        var hasFanart = false;
        if (folder != null)
        {
            var fanartPath = await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, FanartCandidates, ct);
            hasFanart = fanartPath != null;
        }
        else if (!string.IsNullOrWhiteSpace(movie?.MetaBackdropUrl))
        {
            hasFanart = true;
        }

        if (hasFanart)
        {
            sources.Add(new MovieCropSource("backdrop", "Backdrop / Jacket", $"/image-cache/{Uri.EscapeDataString(code)}/fanart/full"));
        }

        var hasPoster = false;
        if (folder != null)
        {
            var posterPath = await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, PosterCandidates, ct);
            hasPoster = posterPath != null;
        }
        else if (!string.IsNullOrWhiteSpace(movie?.MetaCoverUrl))
        {
            hasPoster = true;
        }

        if (hasPoster)
        {
            sources.Add(new MovieCropSource("poster", "Current Poster", $"/image-cache/{Uri.EscapeDataString(code)}/poster/full"));
        }

        var extraFiles = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
        for (var i = 0; i < extraFiles.Count; i++)
        {
            sources.Add(new MovieCropSource($"extrafanart-{i}", $"Gallery #{i + 1}", $"/image-cache/{Uri.EscapeDataString(code)}/extrafanart/{i}/full"));
        }

        var defaultSourceKey = sources.FirstOrDefault(s => s.Key == "backdrop")?.Key
            ?? sources.FirstOrDefault(s => s.Key == "poster")?.Key
            ?? sources.FirstOrDefault()?.Key;

        return new MovieCropSources(code, defaultSourceKey, sources, HasLocalFolder: folder != null);
    }

    public async Task<byte[]?> GetSourceBytesAsync(string code, string sourceKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(sourceKey)) return null;

        if (sourceKey == "backdrop")
        {
            var path = await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, FanartCandidates, ct);
            if (path != null && File.Exists(path))
            {
                return await File.ReadAllBytesAsync(path, ct);
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code, ct);
            var remoteUrl = movie?.MetaBackdropUrl ?? movie?.MetaCoverUrl;
            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                try
                {
                    var client = httpClientFactory.CreateClient();
                    return await client.GetByteArrayAsync(remoteUrl, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Failed to download remote backdrop for {Code} from {Url}", code, remoteUrl);
                }
            }
            return null;
        }

        if (sourceKey == "poster")
        {
            var path = await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(code, PosterCandidates, ct);
            if (path != null && File.Exists(path))
            {
                return await File.ReadAllBytesAsync(path, ct);
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code, ct);
            var remoteUrl = movie?.MetaCoverUrl;
            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                try
                {
                    var client = httpClientFactory.CreateClient();
                    return await client.GetByteArrayAsync(remoteUrl, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Failed to download remote poster for {Code} from {Url}", code, remoteUrl);
                }
            }
            return null;
        }

        if (sourceKey.StartsWith("extrafanart-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(sourceKey["extrafanart-".Length..], out var index))
        {
            var names = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
            if (index >= 0 && index < names.Count)
            {
                var path = await localLibraryClient.ResolveLiveExtraFanartFilePathAsync(code, names[index], ct);
                if (path != null && File.Exists(path))
                {
                    return await File.ReadAllBytesAsync(path, ct);
                }
            }
        }

        return null;
    }

    public async Task<MovieCoverCropDownloadResult> DownloadImageFromUrlAsync(string url, CancellationToken ct = default)
    {
        var urlFailure = RemoteImageDownloader.TryParseUrl(url, out var uri);
        var download = urlFailure != RemoteImageDownloadFailure.None
            ? new RemoteImageDownloadResult(urlFailure)
            : await RemoteImageDownloader.DownloadAsync(
                httpClientFactory.CreateClient(RemoteImageDownloader.ImportClientName),
                uri!,
                uploadSettings.MaxSizeBytes,
                ct,
                configureRequest: request =>
                {
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Javbuddy", "1.0"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
                });

        if (download.Success)
        {
            return new(true, download.Bytes, download.MediaType ?? "image/jpeg", null);
        }

        if (download.Error is HttpRequestException)
        {
            logger.LogWarning(download.Error, "HTTP error downloading image from {Url}", url.Trim());
        }
        else if (download.Error is not null)
        {
            logger.LogWarning(download.Error, "Unexpected error downloading image from {Url}", url.Trim());
        }

        return new(false, null, null, download.Failure switch
        {
            RemoteImageDownloadFailure.MissingUrl => "Please enter an image URL.",
            RemoteImageDownloadFailure.DefunctHost => "The provided image URL is on a defunct domain (r18.com) and cannot be downloaded.",
            RemoteImageDownloadFailure.InvalidUrl => "Please enter a valid HTTP or HTTPS image URL.",
            RemoteImageDownloadFailure.BlockedAddress => RemoteImageDownloadMessages.BlockedAddress,
            RemoteImageDownloadFailure.Timeout => "Image download timed out after 30 seconds.",
            RemoteImageDownloadFailure.HttpStatus => $"Failed to download image: Server returned {(int)download.StatusCode!.Value} {download.ReasonPhrase}.",
            RemoteImageDownloadFailure.DeclaredTooLarge => $"Image size ({download.DeclaredLength!.Value / (1024 * 1024):F1} MB) exceeds maximum allowed size ({uploadSettings.MaxSizeMb} MB).",
            RemoteImageDownloadFailure.TooLarge => $"Image size exceeds maximum allowed size ({uploadSettings.MaxSizeMb} MB).",
            RemoteImageDownloadFailure.Empty => "Downloaded image file is empty.",
            _ when download.Error is HttpRequestException => $"Network error downloading image: {download.Error.Message}",
            _ => $"Error downloading image: {download.Error?.Message}",
        });
    }

    public async Task<MovieCoverCropSaveResult> SaveCroppedCoverAsync(
        string code,
        byte[] sourceBytes,
        NormalizedCropRect cropRect,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return MovieCoverCropSaveResult.Fail("Movie code is required.");
        }

        if (sourceBytes is not { Length: > 0 })
        {
            return MovieCoverCropSaveResult.Fail("Source image is empty.");
        }

        var clamped = cropRect.Clamp();

        byte[] jpegBytes;
        try
        {
            jpegBytes = await ImageConversionGate.RunAsync(() => ImageConverter.CropImageToJpeg(sourceBytes, clamped, quality: 92), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to crop image to JPEG for {Code}", code);
            return MovieCoverCropSaveResult.Fail($"Failed to crop image: {ex.Message}");
        }

        byte[] thumbBytes;
        byte[] fullBytes;
        try
        {
            (thumbBytes, fullBytes) = await ImageConversionGate.RunAsync(() => ImageConverter.CropAndConvertToBothWebP(
                sourceBytes,
                clamped,
                qualityFull: imageCache.QualityFull,
                qualityThumb: imageCache.QualityThumb), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to crop image to WebP variants for {Code}", code);
            return MovieCoverCropSaveResult.Fail($"Failed to convert cropped image: {ex.Message}");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code, ct);
        var localLookup = await localLibraryClient.TryGetMetadataAsync(code, ct, includeMediaInfo: false);
        var folder = localLookup.Found ? localLookup.FolderPath : null;
        var isLocal = folder != null && Directory.Exists(folder);

        // The library file is written before the cache: if the disk write fails, the cache must
        // keep showing what's actually on disk rather than a crop that never landed there.
        FileInfo? posterInfo = null;
        string message;
        var isWarning = false;
        if (isLocal)
        {
            var targetPath = Path.Combine(folder!, PosterFileName);
            var tmpPath = targetPath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tmpPath, jpegBytes, ct);
                File.Move(tmpPath, targetPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to write cropped poster for {Code} to {Path}", code, targetPath);
                TryDelete(tmpPath);
                return MovieCoverCropSaveResult.Fail($"Could not write {PosterFileName} to the movie folder: {ex.Message}");
            }

            posterInfo = new FileInfo(targetPath);
            (message, isWarning) = RemoveOtherPosterFiles(code, folder!);
        }
        else
        {
            message = "Saved the cropped cover to Javbuddy's image cache only — this movie has no local folder, so nothing was written to disk.";
        }

        var existing = await db.CachedImages
            .Where(c => c.Code == code && c.Role == LocalImageCacheService.RolePoster && c.Index == 0)
            .ToListAsync(ct);

        var thumbExisting = existing.FirstOrDefault(c => c.Variant == LocalImageCacheService.VariantThumb);
        var fullExisting = existing.FirstOrDefault(c => c.Variant == LocalImageCacheService.VariantFull);

        var thumbStorageId = thumbExisting?.StorageId ?? Guid.NewGuid();
        var fullStorageId = fullExisting?.StorageId ?? Guid.NewGuid();

        var destThumbPath = imageCache.GetStoragePath(thumbStorageId);
        var destFullPath = imageCache.GetStoragePath(fullStorageId);

        Directory.CreateDirectory(Path.GetDirectoryName(destThumbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);

        await File.WriteAllBytesAsync(destThumbPath, thumbBytes, ct);
        await File.WriteAllBytesAsync(destFullPath, fullBytes, ct);

        if (posterInfo != null)
        {
            await db.UpsertCachedImagePairAsync(
                code: code,
                role: LocalImageCacheService.RolePoster,
                index: 0,
                thumbStorageId: thumbStorageId,
                fullStorageId: fullStorageId,
                sourceLength: posterInfo.Length,
                sourceLastWriteUtc: posterInfo.LastWriteTimeUtc,
                updatedAt: DateTime.UtcNow,
                ct: ct);

            if (movie != null)
            {
                await localLibraryClient.SyncImagesSignatureAsync(movie.Id, ct);
            }
        }
        else
        {
            await db.UpsertCachedImagePairAsync(
                code: code,
                role: LocalImageCacheService.RolePoster,
                index: 0,
                thumbStorageId: thumbStorageId,
                fullStorageId: fullStorageId,
                sourceLength: 0L,
                sourceLastWriteUtc: DateTime.MinValue,
                sourceUrl: "custom",
                updatedAt: DateTime.UtcNow,
                ct: ct);
        }

        await cacheMaintenance.EnforceSizeLimitAsync((long)thumbBytes.Length + fullBytes.Length, ct);
        movieChangeNotifier?.NotifyChanged();
        return new MovieCoverCropSaveResult(true, Message: message, IsWarning: isWarning);
    }

    // Standardizes the folder on poster.jpg: any other poster/folder image left beside it would be
    // stale artwork, and media servers that prefer folder.* (or a scan picking up poster.png) would
    // keep showing it instead of the new crop.
    private (string Message, bool IsWarning) RemoveOtherPosterFiles(string code, string folder)
    {
        var removed = new List<string>();
        var failed = new List<string>();
        foreach (var name in PosterCandidates.Where(n => n != PosterFileName))
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;

            try
            {
                File.Delete(path);
                removed.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to remove leftover poster {Path} for {Code}", path, code);
                failed.Add($"{name} ({ex.Message})");
            }
        }

        var message = $"Saved the cropped cover to {PosterFileName} in the movie folder";
        if (removed.Count > 0)
        {
            message += $" and removed {string.Join(", ", removed)}";
        }

        return failed.Count == 0
            ? (message + ".", false)
            : (message + $", but could not remove {string.Join(", ", failed)} — it may still show as the poster in other media servers.", true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
