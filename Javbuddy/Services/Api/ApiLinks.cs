using Javbuddy.Services.Api.Contracts;

namespace Javbuddy.Services.Api;

/// <summary>Adds the absolute <c>webUrl</c> to the movie DTOs for the wire. Shared by the REST endpoints and the MCP tools so both
/// answer the same shape.</summary>
public static class ApiLinks
{
    /// <summary>The absolute URL for an app-relative path, from the request's scheme, host and PathBase. Without a request
    /// (none in scope) the path itself is returned.</summary>
    public static string AbsoluteUrl(HttpRequest? request, string path) =>
        request is null ? path : $"{request.Scheme}://{request.Host}{request.PathBase}{path}";

    public static ApiMovieDetail WithUrl(HttpRequest? request, MovieDetail detail) =>
        new(detail, AbsoluteUrl(request, detail.WebPath));

    public static ApiMovieSummary WithUrl(HttpRequest? request, MovieSummary summary) =>
        new(summary, AbsoluteUrl(request, summary.WebPath));

    public static ApiMovieListPage WithUrl(HttpRequest? request, MovieListPage page) =>
        new(page.Total, page.Items.Select(m => WithUrl(request, m)).ToList());

    public static ApiMetadataPatchResponse WithUrl(HttpRequest? request, MetadataPatchResponse response) =>
        new(WithUrl(request, response.Movie), response.Warning);

    public static ApiMovieAddResponse WithUrl(HttpRequest? request, MovieAddResponse response) =>
        new(WithUrl(request, response.Movie), response.OwnedViaJellyfin, response.MetadataFound, response.MetadataError);

    public static ApiMovieLookupResult WithUrl(HttpRequest? request, MovieLookupResult result) =>
        new(result.Query, result.Code, result.InLibrary, result.Movie is null ? null : WithUrl(request, result.Movie));
}
