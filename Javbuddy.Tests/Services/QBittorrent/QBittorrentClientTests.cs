using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.QBittorrent;

public class QBittorrentClientTests
{
    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WithVersion()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings
            {
                BaseUrl = "http://localhost:8080",
                Username = "admin",
                Password = "adminadmin"
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/v2/auth/login")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                response.Headers.Add("Set-Cookie", "SID=fake-sid-123; Path=/");
                return Task.FromResult(response);
            }

            if (req.RequestUri?.AbsolutePath == "/api/v2/app/version")
            {
                Assert.Equal("SID=fake-sid-123", req.Headers.GetValues("Cookie").FirstOrDefault());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("v4.6.0\n")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Equal("v4.6.0", result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenVersionIsEmpty()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings
            {
                BaseUrl = "http://localhost:8080",
                Username = "admin",
                Password = "adminadmin"
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/v2/auth/login")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                response.Headers.Add("Set-Cookie", "SID=fake-sid-123; Path=/");
                return Task.FromResult(response);
            }

            if (req.RequestUri?.AbsolutePath == "/api/v2/app/version")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("   ")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Set a base URL, username and password first.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenLoginFails()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings
            {
                BaseUrl = "http://localhost:8080",
                Username = "admin",
                Password = "wrongpassword"
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/v2/auth/login")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Reached the server, but the login was rejected.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenVersionEndpointReturnsError()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings
            {
                BaseUrl = "http://localhost:8080",
                Username = "admin",
                Password = "adminadmin"
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/v2/auth/login")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                response.Headers.Add("Set-Cookie", "SID=fake-sid-123; Path=/");
                return Task.FromResult(response);
            }

            if (req.RequestUri?.AbsolutePath == "/api/v2/app/version")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Server returned 500.", result.Message);
        Assert.Null(result.Version);
    }

    private sealed record Seen(string Method, string Path, string Query, string Body, string? Cookie, Uri? Referrer);

    private static async Task<QBittorrentClient> CreateConfiguredClientAsync(
        List<Seen> seen, Func<string, HttpResponseMessage?> respond, string? category = null, string loginCookie = "SID=fake-sid-123; Path=/")
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings
            {
                BaseUrl = "http://localhost:8080/",
                Username = "admin",
                Password = "adminadmin",
                Category = category
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            var path = req.RequestUri!.AbsolutePath;
            seen.Add(new Seen(req.Method.Method, path, req.RequestUri.Query, body,
                req.Headers.TryGetValues("Cookie", out var c) ? c.First() : null, req.Headers.Referrer));

            if (path == "/api/v2/auth/login")
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                if (loginCookie.Length > 0) login.Headers.Add("Set-Cookie", loginCookie);
                return login;
            }

            return respond(path) ?? new HttpResponseMessage(HttpStatusCode.OK);
        });

        return new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task AddMagnetAsync_CreatesTagThenAdds_WithCookieAndReferer()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") });

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.True(result.Success);
        Assert.Equal(
            ["/api/v2/auth/login", "/api/v2/torrents/createTags", "/api/v2/torrents/add"],
            seen.Select(s => s.Path));
        var add = seen[^1];
        Assert.Equal("SID=fake-sid-123", add.Cookie);
        Assert.Equal(new Uri("http://localhost:8080/"), add.Referrer);
        Assert.Contains("urls=magnet%3A%3Fxt%3Durn%3Abtih%3Aabc", add.Body);
        Assert.Contains("tags=ABC-123", add.Body);
        Assert.DoesNotContain("category", add.Body);
    }

    [Fact]
    public async Task AddMagnetAsync_CreatesCategoryAndSendsIt_WhenConfigured()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => null, category: "javbuddy");

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.True(result.Success);
        Assert.Equal(
            ["/api/v2/auth/login", "/api/v2/torrents/createTags", "/api/v2/torrents/createCategory", "/api/v2/torrents/add"],
            seen.Select(s => s.Path));
        Assert.Contains("category=javbuddy", seen.Single(s => s.Path == "/api/v2/torrents/createCategory").Body);
        Assert.Contains("category=javbuddy", seen[^1].Body);
    }

    [Fact]
    public async Task AddTorrentFileAsync_UploadsMultipartWithTagAndCategory()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => null, category: "javbuddy");

        var result = await client.AddTorrentFileAsync("ABC-123", "movie.torrent", [1, 2, 3]);

        Assert.True(result.Success);
        var add = seen[^1];
        Assert.Equal("/api/v2/torrents/add", add.Path);
        Assert.Contains("name=torrents", add.Body);
        Assert.Contains("filename=movie.torrent", add.Body);
        Assert.Contains("application/x-bittorrent", add.Body);
        Assert.Contains("name=tags", add.Body);
        Assert.Contains("ABC-123", add.Body);
        Assert.Contains("name=category", add.Body);
    }

    [Fact]
    public async Task AddMagnetAsync_ReturnsFailure_WhenQBittorrentRespondsFails()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, path => path == "/api/v2/torrents/add"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") }
            : null);

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.False(result.Success);
        Assert.Equal("qBittorrent rejected the torrent.", result.ErrorMessage);
    }

    [Fact]
    public async Task AddMagnetAsync_ReturnsFailure_WhenAddReturnsErrorStatus()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, path => path == "/api/v2/torrents/add"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("nope") }
            : null);

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.False(result.Success);
        Assert.Equal("qBittorrent returned 403: nope", result.ErrorMessage);
    }

    [Fact]
    public async Task AddMagnetAsync_ReturnsFailure_WhenLoginRejected_AndDoesNotAdd()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => null, loginCookie: "");

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.False(result.Success);
        Assert.Contains("log in", result.ErrorMessage);
        Assert.Single(seen);
    }

    [Fact]
    public async Task AddMagnetAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK);
        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task AddMagnetAsync_ReturnsFailure_WhenServerUnreachable()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.QBittorrentSettings.Add(new QBittorrentSettings { BaseUrl = "http://localhost:8080", Username = "a", Password = "b" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("boom"));
        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.AddMagnetAsync("ABC-123", "magnet:?xt=urn:btih:abc");

        Assert.False(result.Success);
        Assert.Equal("Could not reach qBittorrent: boom", result.ErrorMessage);
    }

    [Fact]
    public async Task GetTorrentsAsync_ReturnsParsedTorrents_AndScopesToCategory()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, path => path == "/api/v2/torrents/info"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"hash\":\"h1\",\"name\":\"n\",\"size\":10,\"progress\":0.5,\"state\":\"downloading\",\"content_path\":\"/dl/n\",\"tags\":\"ABC-123\"}]")
            }
            : null, category: "my cat");

        var torrents = await client.GetTorrentsAsync();

        var torrent = Assert.Single(torrents);
        Assert.Equal("h1", torrent.Hash);
        Assert.Equal(0.5, torrent.Progress);
        Assert.Equal("/dl/n", torrent.ContentPath);
        Assert.Equal("?category=my%20cat", seen[^1].Query);
    }

    [Fact]
    public async Task GetTorrentsAsync_OmitsCategoryFilter_WhenNoCategory()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });

        var torrents = await client.GetTorrentsAsync();

        Assert.Empty(torrents);
        Assert.Equal("", seen[^1].Query);
    }

    [Fact]
    public async Task GetTorrentsAsync_ReturnsEmpty_WhenInfoFailsLoginFailsOrNotConfigured()
    {
        var failing = await CreateConfiguredClientAsync([], path => path == "/api/v2/torrents/info" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null);
        Assert.Empty(await failing.GetTorrentsAsync());

        var noLogin = await CreateConfiguredClientAsync([], _ => null, loginCookie: "");
        Assert.Empty(await noLogin.GetTorrentsAsync());

        var nullJson = await CreateConfiguredClientAsync([], _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") });
        Assert.Empty(await nullJson.GetTorrentsAsync());

        using var factory = new TestDbContextFactory();
        var unconfigured = new QBittorrentClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());
        Assert.Empty(await unconfigured.GetTorrentsAsync());
    }

    [Theory]
    [InlineData(true, "deleteFiles=true")]
    [InlineData(false, "deleteFiles=false")]
    public async Task DeleteTorrentAsync_SendsHashAndDeleteFilesFlag(bool deleteFiles, string expected)
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => null);

        var ok = await client.DeleteTorrentAsync("abc123", deleteFiles);

        Assert.True(ok);
        var delete = seen[^1];
        Assert.Equal("/api/v2/torrents/delete", delete.Path);
        Assert.Contains("hashes=abc123", delete.Body);
        Assert.Contains(expected, delete.Body);
    }

    [Fact]
    public async Task DeleteTorrentAsync_ReturnsFalse_WhenDeleteFailsLoginFailsOrNotConfigured()
    {
        var failing = await CreateConfiguredClientAsync([], path => path == "/api/v2/torrents/delete" ? new HttpResponseMessage(HttpStatusCode.NotFound) : null);
        Assert.False(await failing.DeleteTorrentAsync("h"));

        var noLogin = await CreateConfiguredClientAsync([], _ => null, loginCookie: "");
        Assert.False(await noLogin.DeleteTorrentAsync("h"));

        using var factory = new TestDbContextFactory();
        var unconfigured = new QBittorrentClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());
        Assert.False(await unconfigured.DeleteTorrentAsync("h"));
    }

    [Fact]
    public async Task Login_IgnoresNonSidCookies_AndPicksSidAmongSeveral()
    {
        var seen = new List<Seen>();
        var client = await CreateConfiguredClientAsync(seen, _ => null, loginCookie: "other=1; Path=/");

        Assert.False(await client.DeleteTorrentAsync("h"));
        Assert.Single(seen);
    }
}
