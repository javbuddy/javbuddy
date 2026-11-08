using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Javbuddy.Startup;

/// <summary>An absolute-link wire version of <see cref="MovieSummary"/>: the relative <c>webPath</c> plus <c>webUrl</c>, built from the request.</summary>
public sealed record ApiMovieSummary : MovieSummary
{
    public ApiMovieSummary(MovieSummary summary, string webUrl) : base(summary) => WebUrl = webUrl;
    public string WebUrl { get; }
}

public sealed record ApiMovieDetail : MovieDetail
{
    public ApiMovieDetail(MovieDetail detail, string webUrl) : base(detail) => WebUrl = webUrl;
    public string WebUrl { get; }
}

public sealed record ApiMovieListPage(int Total, IReadOnlyList<ApiMovieSummary> Items);

public sealed record ApiMovieLookupResult(string Query, string? Code, bool InLibrary, ApiMovieSummary? Movie);

/// <summary>The public REST API under <c>/api/v1</c> (see docs/api.md). Unauthenticated like the rest of the app, guarded
/// against cross-site browser calls by <see cref="OriginGuardFilter"/>. A thin layer over <see cref="ILibraryApiService"/>.</summary>
public static class ApiEndpoints
{
    public const string Root = "/api/v1";

    public static void MapPublicApi(this WebApplication app)
    {
        var api = app.MapGroup(Root)
            .RequireAllowedOrigin()
            .DisableAntiforgery(); // JSON only; there are no form-bound routes to protect

        MapSystem(api);
        MapMovies(api);
        MapLookups(api);

        // An unknown /api/v1 path must answer with a problem, not fall through to the HTML not-found page.
        api.MapFallback("{**path}", () => Results.Problem(title: "Not found", detail: "No such API route.", statusCode: StatusCodes.Status404NotFound))
            .ExcludeFromDescription();
    }

    private static void MapSystem(RouteGroupBuilder api) =>
        api.MapGet("/system/info", async (ILibraryApiService library, CancellationToken ct) =>
                ApiResults.JsonOk(await library.GetSystemInfoAsync(ct)))
            .WithTags("System")
            .WithName("GetSystemInfo")
            .WithSummary("Javbuddy's version and which integrations (javinizer, prowlarr, qbittorrent, jellyfin) are configured.")
            .Produces<SystemInfo>();

    private static void MapMovies(RouteGroupBuilder api)
    {
        api.MapGet("/movies", async (
                [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? studio, [FromQuery] string? tag,
                [FromQuery] int? actorId, [FromQuery] string? nfoDrift, [FromQuery] int? skip, [FromQuery] int? take,
                HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                var pageSize = take ?? 50;
                if (pageSize is < 1 or > LibraryApiService.MaxPageSize)
                {
                    return ApiResults.BadRequest($"take must be between 1 and {LibraryApiService.MaxPageSize}.");
                }
                if (skip is < 0)
                {
                    return ApiResults.BadRequest("skip must not be negative.");
                }

                var result = await library.ListAsync(new MovieListQuery(q, status, studio, tag, actorId, nfoDrift, skip ?? 0, pageSize), ct);
                return result.ToHttp(page => new ApiMovieListPage(page.Total, page.Items.Select(m => WithUrl(request, m)).ToList()));
            })
            .WithTags("Movies")
            .WithName("ListMovies")
            .WithSummary("Lists or searches the library, newest added first. Returns a page plus the total number of matches.")
            .Produces<ApiMovieListPage>()
            .ProducesValidationProblem();

        // Registered before /movies/{code}; the literal segment wins either way.
        api.MapGet("/movies/lookup", async ([FromQuery] string[]? codes, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                // Both ?codes=A,B and repeated ?codes=A&codes=B.
                var list = (codes ?? [])
                    .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToList();
                var result = await library.LookupAsync(list, ct);
                return result.ToHttp(results => results
                    .Select(r => new ApiMovieLookupResult(r.Query, r.Code, r.InLibrary, r.Movie is null ? null : WithUrl(request, r.Movie)))
                    .ToList());
            })
            .WithTags("Movies")
            .WithName("LookupMovies")
            .WithSummary("Checks up to 100 codes (comma-separated or repeated) for presence in the library, tolerating zero-padding and case.")
            .Produces<List<ApiMovieLookupResult>>()
            .ProducesValidationProblem();

        api.MapGet("/movies/{code}", async (string code, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
                (await library.GetDetailAsync(code, ct)).ToHttp(d => new ApiMovieDetail(d, AbsoluteUrl(request, d.WebPath))))
            .WithTags("Movies")
            .WithName("GetMovie")
            .WithSummary("A movie's full detail by code (case and zero-padding tolerant).")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapLookups(RouteGroupBuilder api)
    {
        api.MapGet("/tags", async ([FromQuery] string? q, ILibraryApiService library, CancellationToken ct) =>
                ApiResults.JsonOk(await library.SearchTagsAsync(q, ct)))
            .WithTags("Tags")
            .WithName("SearchTags")
            .WithSummary("Searches the tag library by name; without q lists tags.")
            .Produces<List<TagSearchItem>>();

        api.MapGet("/actors", async ([FromQuery] string? q, ILibraryApiService library, CancellationToken ct) =>
                string.IsNullOrWhiteSpace(q)
                    ? ApiResults.BadRequest("q is required.")
                    : ApiResults.JsonOk(await library.SearchActorsAsync(q, ct)))
            .WithTags("Actors")
            .WithName("SearchActors")
            .WithSummary("Searches tracked actors by name or alternative name.")
            .Produces<List<ActorSearchHit>>()
            .ProducesValidationProblem();
    }

    /// <summary>The absolute URL for an app-relative path, from the request's scheme, host and PathBase.</summary>
    public static string AbsoluteUrl(HttpRequest request, string path) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{path}";

    private static ApiMovieSummary WithUrl(HttpRequest request, MovieSummary summary) =>
        new(summary, AbsoluteUrl(request, summary.WebPath));
}
