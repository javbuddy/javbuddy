using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Movies;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Trickplay;
using Microsoft.Net.Http.Headers;

namespace Javbuddy.Startup;

/// <summary>The image, video-stream, trickplay and scene/highlight media endpoints.</summary>
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        // Serves a movie's poster/fanart out of the local WebP image cache (converted, opaquely stored; see
        // ILocalImageCacheService), converting on demand on a cache miss. A poster with no local file on a
        // Missing movie is cropped locally from the remote cover instead of serving the raw (often
        // uncropped) remote image; see IRemotePosterCropService. The same crop applies to a code that isn't
        // tracked yet, using its r18.dev preview cover (see MovieDetail.razor's preview branch); that
        // "movie" is code-shaped, not a Movie row, so RemotePosterCropService's Code-keyed cache works for it
        // unchanged. Anything else with no local file falls back to a 302 to the movie's remote metadata
        // image, so every consumer can point at this one URL shape.
        app.MapGet("/image-cache/{code}/{role}/{variant}", async (
            HttpContext http, string code, string role, string variant,
            IImageServingService imageServingService, ILocalImageCache imageCache, CancellationToken ct) =>
        {
            var result = await imageServingService.GetPosterOrFanartAsync(code, role, variant, ct);
            return result.Kind switch
            {
                ImageServingResultKind.LocalFile => ImageFile(http, result.FilePath!, imageCache.IsCachedFile(result.FilePath!) ? "private, max-age=300" : null),
                ImageServingResultKind.Redirect => Results.Redirect(result.RedirectUrl!),
                _ => Results.NotFound(),
            };
        });

        // Streams a movie's main local video file for the player, so playback doesn't need
        // Jellyfin, or with ?fileId= one of its other versions. Range requests (206) let the
        // browser seek without downloading the whole file. The path is only ever the one
        // IMovieStreamService resolves inside a library root, never one from the request.
        app.MapGet("/api/movies/{movieId:int}/stream", async (int movieId, int? fileId, IMovieStreamService streamService, CancellationToken ct) =>
        {
            var path = fileId is { } id
                ? await streamService.GetFilePathAsync(movieId, id, ct)
                : await streamService.GetMainFilePathAsync(movieId, ct);
            return path is null
                ? Results.NotFound()
                : Results.File(path, MovieStreamService.ContentType(path), enableRangeProcessing: true);
        });

        // One sheet of a movie's locally generated trickplay. The identity names one exact
        // video file, so a sheet never changes under its URL.
        app.MapGet("/trickplay/{movieId:int}/{identity}/{index:int}.webp", async (
            HttpContext http, int movieId, string identity, int index, ITrickplayService trickplay, CancellationToken ct) =>
        {
            var tile = await trickplay.OpenTileAsync(movieId, identity, index, ct);
            return tile is null ? Results.NotFound() : StoredImage(http, tile, "private, max-age=86400");
        });

        // A scene's generated screenshot ("thumb") or hover preview video ("preview"),.
        // 404 while it's missing or stale; that request also queues its (re)generation.
        app.MapGet("/scene-image/{sceneId:int}/{variant}", async (
            HttpContext http, int sceneId, string variant, ISceneMediaService sceneMedia, CancellationToken ct) =>
        {
            var path = await sceneMedia.GetFilePathAsync(sceneId, variant, ct);
            return path is null ? Results.NotFound() : ImageFile(http, path, "private, max-age=86400");
        });

        // A highlight's generated screenshot or hover preview; same contract as /scene-image.
        app.MapGet("/highlight-image/{highlightId:int}/{variant}", async (
            HttpContext http, int highlightId, string variant, IHighlightMediaService highlightMedia, CancellationToken ct) =>
        {
            var path = await highlightMedia.GetFilePathAsync(highlightId, variant, ct);
            return path is null ? Results.NotFound() : ImageFile(http, path, "private, max-age=86400");
        });

        // An apex's generated hover preview; same contract as /highlight-image, preview only.
        app.MapGet("/apex-image/{apexId:int}/{variant}", async (
            HttpContext http, int apexId, string variant, IApexMediaService apexMedia, CancellationToken ct) =>
        {
            var path = await apexMedia.GetFilePathAsync(apexId, variant, ct);
            return path is null ? Results.NotFound() : ImageFile(http, path, "private, max-age=86400");
        });

        // Serves one image from a movie's local "extrafanart" subfolder out of the local WebP image
        // cache, by natural-sort index (not filename — see CachedImage.Index). No remote fallback here:
        // extrafanart has no equivalent remote-metadata field.
        app.MapGet("/image-cache/{code}/extrafanart/{index}/{variant}", async (
            HttpContext http, string code, int index, string variant, ILocalImageCacheService imageCacheService, ILocalImageCache imageCache, CancellationToken ct) =>
        {
            var path = await imageCacheService.GetOrCreateAsync(code, LocalImageCacheService.RoleExtraFanart, index, variant, ct);
            return path is null ? Results.NotFound() : ImageFile(http, path, imageCache.IsCachedFile(path) ? "private, max-age=300" : null);
        });

        // Serves an actor's image out of the local WebP actor image cache (see IActorImageCacheService),
        // converting/caching on demand on a cache miss (from local .actors/ or remote ThumbnailUrl fallback).
        // Returns 404 if no image is available, letting the UI fall back to the initials avatar — never
        // redirects to external domains or leaks client network traffic.
        // Uses "no-cache" so browsers revalidate with ETag / Last-Modified on every view — ensuring
        // newly uploaded or cropped custom portraits reflect immediately without hard reloads.
        app.MapGet("/actor-image/{actorId:int}/{variant}", async (
            HttpContext http, int actorId, string variant, IActorImageCacheService imageCacheService, CancellationToken ct) =>
        {
            var result = await imageCacheService.GetOrCreateAsync(actorId, variant, ct);
            return ServeImage(http, result, "no-cache");
        });

        app.MapGet("/actor-photo/{photoId:int}/{variant}", async (
            HttpContext http, int photoId, string variant, IActorPhotoService actorPhotoService, CancellationToken ct) =>
        {
            var result = await actorPhotoService.GetImageAsync(photoId, variant, ct);
            return ServeImage(http, result, "private, max-age=300");
        });
    }

    // Every image endpoint above serves bytes straight out of the on-disk WebP cache, and those bytes
    // only change when their source does. Without an explicit Cache-Control the browser revalidates
    // each one every time it re-appears — and the Movies grid re-mounts the same cards constantly as
    // its virtualized window slides, so that showed up as thumbnails visibly re-loading while
    // scrolling even though nothing about them had changed. A short max-age keeps a whole scrolling
    // session in the browser's own cache; the ETag/Last-Modified taken from the file make the eventual
    // revalidation a 304 rather than a re-download, so a refreshed poster still shows up promptly.
    private static IResult ImageFile(HttpContext http, string path, string? cacheControl = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(cacheControl)) http.Response.Headers.CacheControl = cacheControl;
        // Range requests for the hover preview video: Safari won't play one without.
        return Results.File(
            path,
            ImageContentType(path),
            lastModified: info.LastWriteTimeUtc,
            entityTag: new EntityTagHeaderValue($"\"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}\""),
            enableRangeProcessing: true);
    }

    // The same for an image in a durable object store: streamed, with the store's
    // Last-Modified/ETag. The result disposes the stream once it's sent.
    private static IResult StoredImage(HttpContext http, StoredObject stored, string? cacheControl = null)
    {
        if (!string.IsNullOrWhiteSpace(cacheControl)) http.Response.Headers.CacheControl = cacheControl;
        return Results.Stream(
            stored.Content,
            ImageContentType(stored.Key),
            lastModified: stored.LastModified,
            entityTag: new EntityTagHeaderValue(stored.ETag));
    }

    private static IResult ServeImage(HttpContext http, ImageServingResult result, string? cacheControl) => result.Kind switch
    {
        ImageServingResultKind.LocalFile => ImageFile(http, result.FilePath!, cacheControl),
        ImageServingResultKind.Stored => StoredImage(http, result.Object!, cacheControl),
        _ => Results.NotFound(),
    };

    private static string ImageContentType(string pathOrKey) => Path.GetExtension(pathOrKey).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };
}
