using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Movies;
using Microsoft.Net.Http.Headers;

namespace Javbuddy.Startup;

/// <summary>DeoVR's JSON API routes. All of them answer 404 while the integration is
/// off. The JSON routes take GET and POST: DeoVR may post its login fields to the URLs it loads.</summary>
public static class DeoVrEndpoints
{
    public static void MapDeoVrEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup(DeoVrUrls.Root).DisableAntiforgery();

        // The list DeoVR opens when given just Javbuddy's address: one list per group, in order.
        routes.MapMethods("", ["GET", "POST"], async (HttpContext http, IDeoVrService deoVr, CancellationToken ct) =>
        {
            var settings = await deoVr.GetEnabledSettingsAsync(ct);
            return settings is null
                ? Results.NotFound()
                : Results.Json(await deoVr.GetScenesAsync(BaseUrl(http), ct));
        });

        routes.MapMethods("/movies/{movieId:int}", ["GET", "POST"], async (HttpContext http, int movieId, IDeoVrService deoVr, CancellationToken ct) =>
        {
            if (await deoVr.GetEnabledSettingsAsync(ct) is null) return Results.NotFound();
            var video = await deoVr.GetVideoAsync(movieId, BaseUrl(http), ct);
            return video is null ? Results.NotFound() : Results.Json(video);
        });

        // One version's file, with byte ranges for seeking. The file name segment is only there so
        // the URL ends in the file's extension; the path comes from IMovieStreamService alone.
        routes.MapGet("/stream/{movieId:int}/{fileId:int}/{fileName}", async (int movieId, int fileId, IDeoVrService deoVr, IMovieStreamService streamService, CancellationToken ct) =>
        {
            if (await deoVr.GetEnabledSettingsAsync(ct) is null) return Results.NotFound();
            var path = await streamService.GetFilePathAsync(movieId, fileId, ct);
            return path is null ? Results.NotFound() : Results.File(path, MovieStreamService.ContentType(path), enableRangeProcessing: true);
        });

        // The seek-bar preview mosaic, built on its first request. The file name segment
        // is DeoVR's own; the identity makes the URL change when the set does, so it can be cached.
        routes.MapGet("/timeline/{movieId:int}/{identity}/" + DeoVrTimeline.FileName, async (HttpContext http, int movieId, string identity, IDeoVrService deoVr, IDeoVrTimelineService timelines, CancellationToken ct) =>
        {
            if (await deoVr.GetEnabledSettingsAsync(ct) is null) return Results.NotFound();
            var mosaic = await timelines.OpenAsync(movieId, identity, ct);
            if (mosaic is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Stream(mosaic.Content, "image/jpeg", lastModified: mosaic.LastModified, entityTag: new EntityTagHeaderValue(mosaic.ETag));
        });

        routes.MapGet("/thumb/{movieId:int}", async (int movieId, IDeoVrService deoVr, CancellationToken ct) =>
        {
            if (await deoVr.GetEnabledSettingsAsync(ct) is null) return Results.NotFound();
            var thumbnail = await deoVr.GetThumbnailAsync(movieId, ct);
            return thumbnail switch
            {
                { FilePath: { } path } => Results.File(path, ImageContentType(path)),
                { RedirectUrl: { } url } => Results.Redirect(url),
                _ => Results.NotFound(),
            };
        });
    }

    // The links DeoVR follows go back to the address it reached Javbuddy at.
    private static string BaseUrl(HttpContext http) => DeoVrUrls.BaseUrl(
        http.Request.Scheme, http.Request.Host.ToString(), http.Request.PathBase.ToString(), http.Request.Headers["X-Forwarded-Proto"].FirstOrDefault());

    private static string ImageContentType(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
}
