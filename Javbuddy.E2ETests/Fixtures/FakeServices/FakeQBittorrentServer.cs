using Javbuddy.Services.QBittorrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

public sealed record FakeQBittorrentAddedTorrent(string? Urls, string? FileName, string? Tags, string? Category);

/// <summary>Fake qBittorrent WebUI API v2: cookie-session login, add (magnet or uploaded
/// .torrent file), list, and delete — enough to drive the Activity Queue page end to end. Unlike
/// the real client's async download lifecycle, an added torrent is reflected in
/// <see cref="Torrents"/> immediately (synchronously, on the /add call) rather than needing a
/// background sync tick, which keeps tests deterministic — see the E2E test plan's "search &amp;
/// grab" flow note.</summary>
public sealed class FakeQBittorrentServer : FakeHttpServer
{
    public const string Username = "fake-user";
    public const string Password = "fake-pass";
    public const string Version = "v4.6.0";
    private const string Sid = "fake-qbittorrent-sid";

    public bool LoginSucceeds { get; set; } = true;

    public List<QBittorrentTorrentDto> Torrents { get; set; } = [];

    public List<FakeQBittorrentAddedTorrent> AddedTorrents { get; } = [];
    public List<string> DeletedHashes { get; } = [];

    protected override void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/v2/auth/login", (HttpContext ctx) =>
        {
            if (!LoginSucceeds)
            {
                return Results.Text("Fails.");
            }

            ctx.Response.Headers.Append("Set-Cookie", $"SID={Sid}; Path=/");
            return Results.Text("Ok.");
        });

        app.MapGet("/api/v2/app/version", () => Results.Text(Version));

        app.MapPost("/api/v2/torrents/createTags", () => Results.Ok());
        app.MapPost("/api/v2/torrents/createCategory", () => Results.Ok());

        app.MapPost("/api/v2/torrents/add", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files["torrents"];
            var added = new FakeQBittorrentAddedTorrent(
                Urls: form["urls"],
                FileName: file?.FileName,
                Tags: form["tags"],
                Category: form["category"]);
            AddedTorrents.Add(added);

            Torrents.Add(new QBittorrentTorrentDto
            {
                Hash = $"fake-hash-{AddedTorrents.Count}",
                Name = file?.FileName ?? added.Tags ?? "Fake Torrent",
                Size = 2_000_000_000,
                Progress = 0,
                DlSpeed = 0,
                Eta = 8640000,
                State = "downloading",
                Tags = added.Tags,
                AddedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });

            return Results.Text("Ok.");
        });

        app.MapGet("/api/v2/torrents/info", (HttpContext ctx) =>
        {
            var category = ctx.Request.Query["category"];
            var torrents = string.IsNullOrEmpty(category)
                ? Torrents
                : Torrents.Where(t => t.Tags?.Contains(category!, StringComparison.OrdinalIgnoreCase) == true).ToList();
            return Results.Ok(torrents);
        });

        app.MapPost("/api/v2/torrents/delete", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var hashes = ((string?)form["hashes"])?.Split(',') ?? [];
            foreach (var hash in hashes)
            {
                DeletedHashes.Add(hash);
                Torrents.RemoveAll(t => t.Hash == hash);
            }
            return Results.Ok();
        });
    }

    public override void Reset()
    {
        LoginSucceeds = true;
        Torrents = [];
        AddedTorrents.Clear();
        DeletedHashes.Clear();
    }
}
