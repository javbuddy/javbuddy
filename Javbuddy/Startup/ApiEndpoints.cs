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

public sealed record ApiMovieAddResponse(ApiMovieSummary Movie, bool OwnedViaJellyfin, bool MetadataFound, string? MetadataError);

public sealed record ApiMetadataPatchResponse(ApiMovieDetail Movie, string? Warning);

/// <summary>Body of <c>POST /movies</c>.</summary>
public sealed record AddMovieRequest(string? Code = null);

/// <summary>Body of <c>POST /movies/{code}/cast</c>.</summary>
public sealed record AddCastRequest(int ActorId = 0);

/// <summary>Optional body of <c>POST /movies/{code}/nfo/push</c>; without one, <c>includeExternal</c> is false.</summary>
public sealed record PushNfoRequest(bool IncludeExternal = false);

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
            .AddEndpointFilter<RouteGroupBuilder, RequireJsonBodyFilter>()
            .DisableAntiforgery(); // JSON only; there are no form-bound routes to protect

        MapSystem(api);
        MapMovies(api);
        MapLookups(api);
        MapMovieWrites(api);
        MapNfo(api);

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

    private static void MapMovieWrites(RouteGroupBuilder api)
    {
        api.MapPost("/movies", async (HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                var (body, error) = await ApiBody.ReadAsync<AddMovieRequest>(request, ct);
                if (error is not null) return error;
                if (string.IsNullOrWhiteSpace(body?.Code)) return ApiResults.BadRequest("Provide a JSON body like {\"code\": \"ABP-123\"}.");

                var result = await library.AddAsync(body.Code, ct);
                if (result.Status == ApiStatus.Conflict && result.Value is { } existing)
                {
                    return Results.Problem(
                        title: "Conflict",
                        detail: result.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = existing.Movie.Code,
                            ["webUrl"] = AbsoluteUrl(request, existing.Movie.WebPath),
                        });
                }
                if (!result.IsOk) return ApiResults.Problem(result);

                var added = result.Value!;
                request.HttpContext.Response.Headers.Location = AbsoluteUrl(request, $"{Root}/movies/{Uri.EscapeDataString(added.Movie.Code)}");
                return ApiResults.JsonOk(
                    new ApiMovieAddResponse(WithUrl(request, added.Movie), added.OwnedViaJellyfin, added.MetadataFound, added.MetadataError),
                    StatusCodes.Status201Created);
            })
            .WithTags("Movies")
            .WithName("AddMovie")
            .WithSummary("Adds a code to the library as a Missing movie (and fetches its metadata). 409 when it is already there.")
            .Accepts<AddMovieRequest>(isOptional: true, "application/json", "*/*")
            .Produces<ApiMovieAddResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        api.MapPatch("/movies/{code}/metadata", async (string code, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                var (body, error) = await ApiBody.ReadAsync<MetadataPatchRequest>(request, ct);
                if (error is not null) return error;
                if (body is null) return ApiResults.BadRequest("Provide a JSON body like {\"set\": {...}, \"clear\": [...]}.");

                return (await library.PatchMetadataAsync(code, body, ct))
                    .ToHttp(r => new ApiMetadataPatchResponse(WithUrl(request, r.Movie), r.Warning));
            })
            .WithTags("Movies")
            .WithName("PatchMovieMetadata")
            .WithSummary("Edits some metadata fields: values in \"set\" replace, names in \"clear\" are emptied, the rest is kept. Database only; push the .nfo separately.")
            .Accepts<MetadataPatchRequest>(isOptional: true, "application/json", "*/*")
            .Produces<ApiMetadataPatchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPut("/movies/{code}/tags/{tagId:int}", async (string code, int tagId, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
                (await library.AddTagAsync(code, tagId, ct)).ToHttp(d => WithUrl(request, d)))
            .WithTags("Movies")
            .WithName("AddMovieTag")
            .WithSummary("Adds an existing tag to the movie (a no-op when it already has it).")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete("/movies/{code}/tags/{tagId:int}", async (string code, int tagId, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
                (await library.RemoveTagAsync(code, tagId, ct)).ToHttp(d => WithUrl(request, d)))
            .WithTags("Movies")
            .WithName("RemoveMovieTag")
            .WithSummary("Removes the movie's explicit tag (a no-op when it hasn't it).")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/movies/{code}/cast", async (string code, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                var (body, error) = await ApiBody.ReadAsync<AddCastRequest>(request, ct);
                if (error is not null) return error;
                if (body is null || body.ActorId <= 0) return ApiResults.BadRequest("Provide a JSON body like {\"actorId\": 12}.");

                return (await library.AddCastAsync(code, body.ActorId, ct)).ToHttp(d => WithUrl(request, d));
            })
            .WithTags("Movies")
            .WithName("AddMovieCast")
            .WithSummary("Links a tracked actor to the movie's cast.")
            .Accepts<AddCastRequest>(isOptional: true, "application/json", "*/*")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapDelete("/movies/{code}/cast/{actorId:int}", async (string code, int actorId, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
                (await library.RemoveCastByActorAsync(code, actorId, ct)).ToHttp(d => WithUrl(request, d)))
            .WithTags("Movies")
            .WithName("RemoveMovieCastByActor")
            .WithSummary("Unlinks a tracked actor from the movie's cast.")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete("/movies/{code}/cast", async (string code, [FromQuery] string? name, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
                (await library.RemoveCastByNameAsync(code, name ?? string.Empty, ct)).ToHttp(d => WithUrl(request, d)))
            .WithTags("Movies")
            .WithName("RemoveMovieCastByName")
            .WithSummary("Drops a cast name that matches no tracked actor.")
            .Produces<ApiMovieDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static void MapNfo(RouteGroupBuilder api)
    {
        api.MapGet("/movies/{code}/nfo", async (string code, ILibraryApiService library, CancellationToken ct) =>
                (await library.GetNfoAsync(code, ct)).ToHttp(n => n))
            .WithTags("NFO")
            .WithName("GetMovieNfo")
            .WithSummary("The movie's .nfo drift state and the .nfo Javbuddy would write.")
            .Produces<NfoPreview>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/movies/{code}/nfo/push", async (string code, HttpRequest request, ILibraryApiService library, CancellationToken ct) =>
            {
                var (body, error) = await ApiBody.ReadAsync<PushNfoRequest>(request, ct);
                if (error is not null) return error;

                return (await library.PushNfoAsync(code, body?.IncludeExternal ?? false, ct)).ToHttp(r => r);
            })
            .WithTags("NFO")
            .WithName("PushMovieNfo")
            .WithSummary("Writes the movie's .nfo file. Skips .nfo files edited outside Javbuddy unless includeExternal is true. The body is optional.")
            .Accepts<PushNfoRequest>(isOptional: true, "application/json", "*/*")
            .Produces<NfoPushResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/movies/{code}/javinizer/fetch", async (string code, ILibraryApiService library, CancellationToken ct) =>
                (await library.FetchJavinizerAsync(code, ct)).ToHttp(p => p))
            .WithTags("NFO")
            .WithName("FetchJavinizerMetadata")
            .WithSummary("Scrapes the movie's metadata through javinizer-go for comparison. Nothing is stored.")
            .Produces<JavinizerPreview>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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

    private static ApiMovieDetail WithUrl(HttpRequest request, MovieDetail detail) =>
        new(detail, AbsoluteUrl(request, detail.WebPath));

    private static ApiMovieSummary WithUrl(HttpRequest request, MovieSummary summary) =>
        new(summary, AbsoluteUrl(request, summary.WebPath));
}
