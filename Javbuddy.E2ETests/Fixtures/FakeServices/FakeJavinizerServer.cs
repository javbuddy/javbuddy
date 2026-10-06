using System.Collections.Concurrent;
using Javbuddy.Services.Javinizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

/// <summary>Fake javinizer-go: scrape, connection checks, and arranged batch review/preview
/// responses. Scanning and organizing real files are not implemented.</summary>
public sealed class FakeJavinizerServer : FakeHttpServer
{
    public const string ApiToken = "fake-javinizer-token";
    public const string Version = "1.5.1";

    /// <summary>Scrape responses keyed by code (case-insensitive). A code not present here gets
    /// an auto-generated minimal movie from <see cref="BuildDefaultMovie"/> so most tests don't
    /// need to arrange one explicitly.</summary>
    public ConcurrentDictionary<string, MovieViewDto> ScrapeResponses { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, BatchJobResponseDto> BatchResponses { get; } = new();

    /// <summary>When set, /api/v1/scrape returns this error for every code instead of a movie.</summary>
    public string? ScrapeError { get; set; }

    /// <summary>Controls whether GET /api/v1/config (Test Connection) reports success.</summary>
    public bool ConnectionSucceeds { get; set; } = true;

    /// <summary>The Authorization header of the most recent request, for tests that want to
    /// assert the app actually sent the configured API token.</summary>
    public string? LastAuthorizationHeader { get; private set; }

    protected override void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/batch/{jobId}", (string jobId) =>
            BatchResponses.TryGetValue(jobId, out var job) ? Results.Ok(job) : Results.NotFound());

        app.MapPost("/api/v1/batch/{jobId}/results/{resultId}/preview", (string jobId, string resultId) =>
            BatchResponses.TryGetValue(jobId, out var job) && job.Results?.ContainsKey(resultId) == true
                ? Results.Ok(new OrganizePreviewResponseDto { FolderName = resultId, FileName = $"{resultId}.mp4" })
                : Results.NotFound());

        app.MapPost("/api/v1/scrape", async (HttpContext ctx) =>
        {
            LastAuthorizationHeader = ctx.Request.Headers.Authorization;

            var request = await ctx.Request.ReadFromJsonAsync<ScrapeRequestDto>();
            var code = request?.Id ?? string.Empty;

            if (ScrapeError is not null)
            {
                return Results.Json(new ErrorResponseDto { Message = ScrapeError }, statusCode: StatusCodes.Status502BadGateway);
            }

            var movie = ScrapeResponses.GetOrAdd(code, BuildDefaultMovie);
            return Results.Ok(new ScrapeResponseDto { Cached = false, Movie = movie, SourcesUsed = 1 });
        });

        app.MapGet("/api/v1/config", (HttpContext ctx) =>
        {
            LastAuthorizationHeader = ctx.Request.Headers.Authorization;
            return ConnectionSucceeds ? Results.Ok() : Results.Unauthorized();
        });

        app.MapGet("/api/v1/version", (HttpContext ctx) =>
        {
            LastAuthorizationHeader = ctx.Request.Headers.Authorization;
            return ConnectionSucceeds
                ? Results.Ok(new JavinizerVersionResponseDto { Current = Version })
                : Results.Unauthorized();
        });
    }

    public static MovieViewDto BuildDefaultMovie(string code) => new()
    {
        Id = code,
        Code = code.ToLowerInvariant().Replace("-", ""),
        DisplayTitle = $"Test Movie {code}",
        Title = $"Test Movie {code}",
        ReleaseDate = new DateTime(2024, 1, 1),
        ReleaseYear = 2024,
        Runtime = 120,
        Director = "Test Director",
        Maker = "Test Studio",
        Label = "Test Studio",
        Actresses = [new ActressViewDto { FirstName = "Test", LastName = "Actress" }],
        Genres = [new GenreViewDto { Name = "Test Genre" }],
    };

    public override void Reset()
    {
        ScrapeResponses.Clear();
        BatchResponses.Clear();
        ScrapeError = null;
        ConnectionSucceeds = true;
        LastAuthorizationHeader = null;
    }
}
