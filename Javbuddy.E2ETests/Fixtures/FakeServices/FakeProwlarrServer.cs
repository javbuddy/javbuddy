using Javbuddy.Services.Prowlarr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

/// <summary>Fake Prowlarr: search (used by the Movie Detail search modal) and system status
/// (Test Connection).</summary>
public sealed class FakeProwlarrServer : FakeHttpServer
{
    public const string ApiKey = "fake-prowlarr-key";
    public const string Version = "1.24.3.4754";

    /// <summary>Releases returned for every search query, regardless of the query text — good
    /// enough for a fake that only needs to support "search returns some results to grab", not
    /// real query matching. Defaults to one magnet-link release so the search-and-grab flow test
    /// has something to select without arranging one itself.</summary>
    public List<ReleaseResourceDto> Releases { get; set; } = DefaultReleases();

    public bool ConnectionSucceeds { get; set; } = true;

    public string? LastApiKeyHeader { get; private set; }
    public string? LastQuery { get; private set; }

    protected override void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/search", (HttpContext ctx) =>
        {
            LastApiKeyHeader = ctx.Request.Headers["X-Api-Key"];
            LastQuery = ctx.Request.Query["query"];
            return Results.Ok(Releases);
        });

        app.MapGet("/api/v1/system/status", (HttpContext ctx) =>
        {
            LastApiKeyHeader = ctx.Request.Headers["X-Api-Key"];
            return ConnectionSucceeds
                ? Results.Ok(new ProwlarrSystemStatusDto { Version = Version })
                : Results.Unauthorized();
        });
    }

    private static List<ReleaseResourceDto> DefaultReleases() =>
    [
        new ReleaseResourceDto
        {
            Guid = "fake-release-1",
            Title = "Test Release 1080p",
            Indexer = "Fake Indexer",
            IndexerId = 1,
            Size = 2_000_000_000,
            Seeders = 10,
            Leechers = 1,
            Protocol = "torrent",
            PublishDate = DateTime.UtcNow.AddDays(-1),
            MagnetUrl = "magnet:?xt=urn:btih:0000000000000000000000000000000000000000&dn=Test+Release",
        },
    ];

    public override void Reset()
    {
        Releases = DefaultReleases();
        ConnectionSucceeds = true;
        LastApiKeyHeader = null;
        LastQuery = null;
    }
}
