using Javbuddy.Services.Jellyfin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

/// <summary>Fake Jellyfin: system info (Test Connection), the library picker, item lookup (used
/// for the "already owned" check and Movie Detail's Jellyfin refresh), item refresh, and trickplay.
/// No video stream: the player streams the local file itself.</summary>
public sealed class FakeJellyfinServer : FakeHttpServer
{
    public const string ApiKey = "fake-jellyfin-key";
    public const string ServerId = "fake-jellyfin-server-id";
    public const string Version = "10.9.7";

    /// <summary>Items returned by every /Items lookup, regardless of search term/parent id —
    /// same "good enough for a fake" tradeoff as FakeProwlarrServer.Releases.</summary>
    public List<JellyfinItemDto> Items { get; set; } = [];

    public List<JellyfinVirtualFolderDto> Libraries { get; set; } =
    [
        new JellyfinVirtualFolderDto { Name = "Movies", ItemId = "fake-library-id", CollectionType = "movies" },
    ];

    public bool ConnectionSucceeds { get; set; } = true;

    /// <summary>Optional gate for observing a lookup in flight without depending on network speed.</summary>
    public Task? ItemLookupGate { get; set; }

    /// <summary>Item ids POSTed to /Items/{id}/Refresh, for tests asserting the refresh button
    /// actually called through.</summary>
    public List<string> RefreshedItemIds { get; } = [];

    /// <summary>When set, answers the player's trickplay lookup (<c>/Items?fields=Trickplay</c>), so
    /// it renders the ScrubBar instead of the stand-in scene timeline. Tile sheets aren't served.</summary>
    public JellyfinTrickplayItemDto? Trickplay { get; set; }

    protected override void MapEndpoints(WebApplication app)
    {
        app.MapGet("/System/Info", () => ConnectionSucceeds ? Results.Ok(new JellyfinSystemInfoDto { Id = ServerId, Version = Version }) : Results.Unauthorized());

        app.MapGet("/Library/VirtualFolders", () => Results.Ok(Libraries));

        app.MapGet("/Items", async (HttpContext context, CancellationToken ct) =>
        {
            if (Trickplay is { } trickplay && context.Request.Query["fields"] == "Trickplay")
            {
                return Results.Ok(new JellyfinTrickplayQueryResultDto { Items = [trickplay] });
            }
            if (ItemLookupGate is { } gate) await gate.WaitAsync(ct);
            return Results.Ok(new JellyfinQueryResultDto
            {
                Items = Items,
                TotalRecordCount = Items.Count,
            });
        });

        app.MapPost("/Items/{id}/Refresh", (string id) =>
        {
            RefreshedItemIds.Add(id);
            return Results.Ok();
        });
    }

    public override void Reset()
    {
        Items = [];
        Libraries =
        [
            new JellyfinVirtualFolderDto { Name = "Movies", ItemId = "fake-library-id", CollectionType = "movies" },
        ];
        ConnectionSucceeds = true;
        ItemLookupGate = null;
        RefreshedItemIds.Clear();
        Trickplay = null;
    }
}
