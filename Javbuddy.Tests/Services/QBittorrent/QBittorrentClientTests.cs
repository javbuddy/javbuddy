using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.QBittorrent;

public class QBittorrentClientTests
{
    private static async Task<TestDbContextFactory> SeededFactoryAsync()
    {
        var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.QBittorrentSettings.Add(new QBittorrentSettings
        {
            BaseUrl = "http://localhost:8080",
            Username = "admin",
            Password = "adminadmin"
        });
        await db.SaveChangesAsync();
        return factory;
    }

    /// <summary>Fake qBittorrent: hands out sequential SIDs on login (or rejects it), and answers
    /// /torrents/info with 403 unless the request carries the SID the server currently accepts.</summary>
    private sealed class FakeQBittorrent
    {
        public int Logins;
        public bool AcceptLogin = true;
        public HttpStatusCode? LoginStatusOverride;
        public bool LoginWithoutCookie;
        public string ValidSid = "sid-1";

        public FakeHttpMessageHandler Handler => new((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/v2/auth/login")
            {
                Logins++;
                if (LoginStatusOverride is { } status) return Task.FromResult(new HttpResponseMessage(status));
                if (LoginWithoutCookie) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") });
                if (!AcceptLogin) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") });
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", $"SID=sid-{Logins}; Path=/");
                return Task.FromResult(login);
            }

            if (req.Headers.GetValues("Cookie").FirstOrDefault() != $"SID={ValidSid}")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        });
    }

    [Fact]
    public async Task GetTorrentsAsync_ReusesCachedSession_AcrossCallsAndClients()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent();
        var cache = new QBittorrentSessionCache();
        var http = new FakeHttpClientFactory(server.Handler);

        for (var i = 0; i < 3; i++)
        {
            var client = new QBittorrentClient(http, factory, new ConfigurationBuilder().Build(), cache);
            Assert.True((await client.GetTorrentsAsync()).Success);
        }

        Assert.Equal(1, server.Logins);
    }

    [Fact]
    public async Task GetTorrentsAsync_ExpiredSession_ReLogsInOnceOn403AndRetries()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent();
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        Assert.True((await client.GetTorrentsAsync()).Success);
        server.ValidSid = "sid-2";
        Assert.True((await client.GetTorrentsAsync()).Success);

        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task GetTorrentsAsync_RejectedLogin_IsNotRetriedUntilForcedTestConnection()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent { AcceptLogin = false };
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        for (var i = 0; i < 3; i++)
        {
            var result = await client.GetTorrentsAsync();
            Assert.False(result.Success);
            Assert.Equal("qBittorrent login was rejected.", result.ErrorMessage);
        }
        Assert.Equal(1, server.Logins);

        // Credentials fixed on the qBittorrent side: an explicit connection test tries again and lifts the block.
        server.AcceptLogin = true;
        server.ValidSid = "sid-2";
        Assert.True((await client.TestConnectionAsync(forceLogin: true)).Success);
        Assert.Equal(2, server.Logins);
        Assert.True((await client.GetTorrentsAsync()).Success);
        Assert.Equal(2, server.Logins);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetTorrentsAsync_TemporaryLoginFailure_IsReportedButNotRemembered(HttpStatusCode status)
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent { LoginStatusOverride = status };
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        var failed = await client.GetTorrentsAsync();
        Assert.False(failed.Success);
        Assert.Contains("Could not reach qBittorrent", failed.ErrorMessage);

        server.LoginStatusOverride = null;
        server.ValidSid = "sid-2";
        Assert.True((await client.GetTorrentsAsync()).Success);
        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task GetTorrentsAsync_LoginWithoutSessionCookie_IsTemporary()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent { LoginWithoutCookie = true };
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        Assert.False((await client.GetTorrentsAsync()).Success);
        server.LoginWithoutCookie = false;
        server.ValidSid = "sid-2";
        Assert.True((await client.GetTorrentsAsync()).Success);
        Assert.Equal(2, server.Logins);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetTorrentsAsync_LoginRejectedWith401Or403_IsRemembered(HttpStatusCode status)
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent { LoginStatusOverride = status };
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal("qBittorrent login was rejected.", (await client.GetTorrentsAsync()).ErrorMessage);
        }
        Assert.Equal(1, server.Logins);
    }

    [Fact]
    public async Task GetTorrentsAsync_ChangingTheCredentials_LiftsTheRejectedMark()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent { AcceptLogin = false };
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());

        Assert.False((await client.GetTorrentsAsync()).Success);
        Assert.False((await client.GetTorrentsAsync()).Success);
        Assert.Equal(1, server.Logins);

        server.AcceptLogin = true;
        server.ValidSid = "sid-2";
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.QBittorrentSettings.SingleAsync()).Password = "new-password";
            await db.SaveChangesAsync();
        }

        Assert.True((await client.GetTorrentsAsync()).Success);
        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task TestConnectionAsync_WithoutForceLogin_KeepsValidSessionAndRejectedMark()
    {
        using var factory = await SeededFactoryAsync();
        var server = new FakeQBittorrent();
        var cache = new QBittorrentSessionCache();
        var client = new QBittorrentClient(new FakeHttpClientFactory(server.Handler), factory, new ConfigurationBuilder().Build(), cache);

        Assert.True((await client.GetTorrentsAsync()).Success);
        Assert.True((await client.TestConnectionAsync()).Success);
        Assert.True((await client.TestConnectionAsync()).Success);
        Assert.Equal(1, server.Logins);

        var rejectedServer = new FakeQBittorrent { AcceptLogin = false };
        var rejectedClient = new QBittorrentClient(new FakeHttpClientFactory(rejectedServer.Handler), factory, new ConfigurationBuilder().Build(), new QBittorrentSessionCache());
        Assert.False((await rejectedClient.GetTorrentsAsync()).Success);
        Assert.False((await rejectedClient.TestConnectionAsync()).Success);
        Assert.Equal(1, rejectedServer.Logins);
    }

    [Fact]
    public async Task GetTorrentsAsync_NonJsonBody_ReturnsUnexpectedResponseFailure()
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
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=fake-sid-123; Path=/");
                return Task.FromResult(login);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>proxy login</html>", System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new QBittorrentClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.GetTorrentsAsync();

        Assert.False(result.Success);
        Assert.Equal("Unexpected response from qBittorrent.", result.ErrorMessage);
    }

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
}
