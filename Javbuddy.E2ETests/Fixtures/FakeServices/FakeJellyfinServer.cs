using Javbuddy.Services.Jellyfin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

/// <summary>Fake Jellyfin: system info (Test Connection) and an item lookup that never matches (the link
/// check on add and the background link sync). No video stream or trickplay: the player streams the
/// local file itself.</summary>
public sealed class FakeJellyfinServer : FakeHttpServer
{
    public const string ApiKey = "fake-jellyfin-key";
    public const string ServerId = "fake-jellyfin-server-id";
    public const string Version = "10.9.7";

    /// <summary>Optional gate for observing a lookup in flight without depending on network speed.</summary>
    public Task? ItemLookupGate { get; set; }

    protected override void MapEndpoints(WebApplication app)
    {
        app.MapGet("/System/Info", () => Results.Ok(new JellyfinSystemInfoDto { Id = ServerId, Version = Version }));

        app.MapGet("/Items", async (CancellationToken ct) =>
        {
            if (ItemLookupGate is { } gate) await gate.WaitAsync(ct);
            return Results.Ok(new JellyfinQueryResultDto
            {
                Items = [],
            });
        });
    }

    public override void Reset()
    {
        ItemLookupGate = null;
    }
}
