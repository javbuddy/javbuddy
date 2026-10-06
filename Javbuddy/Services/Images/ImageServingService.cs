using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.R18Dev;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Images;

public enum ImageServingResultKind
{
    NotFound,
    LocalFile,
    Redirect,

    /// <summary>An object opened from a durable object store; the endpoint streams
    /// and disposes it.</summary>
    Stored,
}

/// <summary>What Program.cs's image-cache endpoint should do with a request — resolved here so
/// the endpoint itself stays a thin mapping from this to Results.File/Stream/Redirect/NotFound.</summary>
public readonly record struct ImageServingResult(ImageServingResultKind Kind, string? FilePath = null, string? RedirectUrl = null, StoredObject? Object = null)
{
    public static ImageServingResult NotFound() => new(ImageServingResultKind.NotFound);
    public static ImageServingResult LocalFile(string path) => new(ImageServingResultKind.LocalFile, FilePath: path);
    public static ImageServingResult Redirect(string url) => new(ImageServingResultKind.Redirect, RedirectUrl: url);
    public static ImageServingResult Stored(StoredObject stored) => new(ImageServingResultKind.Stored, Object: stored);

    /// <summary>A local file, or not found when path is null.</summary>
    public static ImageServingResult LocalFileOrNotFound(string? path) => path is null ? NotFound() : LocalFile(path);
}

public interface IImageServingService
{
    Task<ImageServingResult> GetPosterOrFanartAsync(string code, string role, string variant, CancellationToken ct = default);

    /// <summary>A short token that changes whenever the movie's poster does, for a "?v=" on its
    /// /image-cache URL: an unchanged poster keeps one URL the browser can paint from cache, while a
    /// cropped, replaced or refreshed one gets a new URL instead of the cached old image.</summary>
    Task<string> GetPosterVersionAsync(string code, CancellationToken ct = default);
}

/// <summary>Resolves a movie's poster/fanart request: local WebP cache hit, else (poster only) a
/// locally-cropped remote cover for a Missing movie or an untracked code previewed from the r18.dev
/// dump, else a redirect to the raw remote metadata image, else not found. See the
/// "/image-cache/{code}/{role}/{variant}" endpoint in Program.cs for why this shape exists.</summary>
public class ImageServingService(
    ILocalImageCacheService imageCacheService,
    ILocalImageCache imageCache,
    IRemotePosterCropService remotePosterCropService,
    IR18DevDumpStore r18DevDumpStore,
    IDbContextFactory<AppDbContext> dbFactory) : IImageServingService
{
    public async Task<ImageServingResult> GetPosterOrFanartAsync(string code, string role, string variant, CancellationToken ct = default)
    {
        if (role != LocalImageCacheService.RolePoster && role != LocalImageCacheService.RoleFanart)
        {
            return ImageServingResult.NotFound();
        }

        var path = await imageCacheService.GetOrCreateAsync(code, role, 0, variant, ct);
        if (path is not null)
        {
            return ImageServingResult.LocalFile(path);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsNoTracking()
            .Where(m => m.Code == code)
            .Select(m => new { m.Status, m.MetaCoverUrl, m.MetaBackdropUrl })
            .FirstOrDefaultAsync(ct);

        if (imageCache.Settings.CachesExternalImages
            && role == LocalImageCacheService.RolePoster
            && movie is { Status: MovieStatus.Missing, MetaCoverUrl: not null and not "" })
        {
            var croppedPath = await remotePosterCropService.GetOrCreateAsync(code, movie.MetaCoverUrl, variant, ct);
            if (croppedPath is not null)
            {
                return ImageServingResult.LocalFile(croppedPath);
            }
        }

        if (imageCache.Settings.CachesExternalImages && movie is null && role == LocalImageCacheService.RolePoster)
        {
            var preview = await r18DevDumpStore.GetMovieByCodeAsync(code, ct);
            if (!string.IsNullOrWhiteSpace(preview?.PosterUrl))
            {
                var croppedPath = await remotePosterCropService.GetOrCreateAsync(code, preview.PosterUrl, variant, ct);
                if (croppedPath is not null)
                {
                    return ImageServingResult.LocalFile(croppedPath);
                }
            }
        }

        var remoteUrl = role == LocalImageCacheService.RolePoster ? movie?.MetaCoverUrl : movie?.MetaBackdropUrl;
        return remoteUrl is not null && Uri.TryCreate(remoteUrl, UriKind.Absolute, out _)
            ? ImageServingResult.Redirect(remoteUrl)
            : ImageServingResult.NotFound();
    }

    // Covers every way the served poster changes: the poster file in the library folder (a crop
    // or an outside edit), the cached copy being rewritten (a crop of a movie with no local folder),
    // and the remote cover URL (a metadata refresh).
    public async Task<string> GetPosterVersionAsync(string code, CancellationToken ct = default)
    {
        var sourcePath = await imageCacheService.ResolveSourcePathAsync(code, LocalImageCacheService.RolePoster, 0, ct);
        var source = sourcePath is null ? null : new FileInfo(sourcePath);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cachedAt = await db.CachedImages
            .AsNoTracking()
            .Where(c => c.Code == code && c.Role == LocalImageCacheService.RolePoster && c.Index == 0)
            .Select(c => (DateTime?)c.UpdatedAt)
            .MaxAsync(ct);
        var coverUrl = await db.Movies
            .AsNoTracking()
            .Where(m => m.Code == code)
            .Select(m => m.MetaCoverUrl)
            .FirstOrDefaultAsync(ct);

        return PosterVersion.Compute(cachedAt, coverUrl, source);
    }
}
