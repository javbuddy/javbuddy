using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Prowlarr;

public class ProwlarrClientTests
{
    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WithVersion()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("http://localhost:9696/api/v1/system/status", req.RequestUri?.ToString());
            Assert.Equal("testkey", req.Headers.GetValues("X-Api-Key").FirstOrDefault());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"version\":\"1.24.3.4754\",\"appName\":\"Prowlarr\"}")
            });
        });

        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Equal("1.24.3.4754", result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenVersionMissingOrMalformed()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "not-valid-json");
        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenVersionIsNullInPayload()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "{}");
        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenUnauthorized()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "badkey" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.Unauthorized);
        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Reached the server, but the API key was rejected.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var client = new ProwlarrClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Set a base URL and API key first.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenServerReturns500()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.InternalServerError, "{\"message\":\"Crash\"}");
        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Server returned 500: Crash", result.Message);
        Assert.Null(result.Version);
    }

    private sealed record SeenRequest(string Url, string? ApiKey);

    private static async Task<(ProwlarrClient Client, List<SeenRequest> Seen)> CreateConfiguredClientAsync(HttpStatusCode status, string body)
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696/", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var seen = new List<SeenRequest>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            seen.Add(new SeenRequest(req.RequestUri!.AbsoluteUri, req.Headers.TryGetValues("X-Api-Key", out var k) ? k.First() : null));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        });
        return (new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build()), seen);
    }

    [Fact]
    public async Task SearchAsync_ReturnsParsedReleases_WithEscapedQueryAndApiKey()
    {
        var (client, seen) = await CreateConfiguredClientAsync(HttpStatusCode.OK,
            "[{\"guid\":\"g1\",\"title\":\"ABC-123 1080p\",\"size\":123,\"indexer\":\"idx\",\"indexerId\":4}]");

        var result = await client.SearchAsync("ABC 123&x");

        Assert.True(result.Success);
        var release = Assert.Single(result.Releases!);
        Assert.Equal("g1", release.Guid);
        Assert.Equal("ABC-123 1080p", release.Title);
        Assert.Equal(123, release.Size);
        Assert.Equal(4, release.IndexerId);
        var request = Assert.Single(seen);
        Assert.Equal("http://localhost:9696/api/v1/search?query=ABC%20123%26x&type=search", request.Url);
        Assert.Equal("testkey", request.ApiKey);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenBodyIsJsonNull()
    {
        var (client, _) = await CreateConfiguredClientAsync(HttpStatusCode.OK, "null");

        var result = await client.SearchAsync("ABC-123");

        Assert.True(result.Success);
        Assert.Empty(result.Releases!);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFailure_WithServerMessage_OnErrorStatus()
    {
        var (client, _) = await CreateConfiguredClientAsync(HttpStatusCode.InternalServerError, "{\"message\":\"Indexer down\"}");

        var result = await client.SearchAsync("ABC-123");

        Assert.False(result.Success);
        Assert.Null(result.Releases);
        Assert.Equal("Prowlarr returned 500: Indexer down", result.ErrorMessage);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var client = new ProwlarrClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());

        var result = await client.SearchAsync("ABC-123");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFailure_WhenServerUnreachable()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var client = new ProwlarrClient(new FakeHttpClientFactory(FakeHttpMessageHandler.Throwing(new HttpRequestException("boom"))), factory, new ConfigurationBuilder().Build());

        var result = await client.SearchAsync("ABC-123");

        Assert.False(result.Success);
        Assert.Equal("Could not reach Prowlarr: boom", result.ErrorMessage);
    }

    [Fact]
    public async Task SearchAsync_UsesEnvSettings_WhenSet()
    {
        using var factory = new TestDbContextFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Prowlarr:BaseUrl"] = "http://env-host:9696",
            ["Prowlarr:ApiKey"] = "envkey"
        }).Build();
        var seen = new List<SeenRequest>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            seen.Add(new SeenRequest(req.RequestUri!.ToString(), req.Headers.GetValues("X-Api-Key").First()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        });
        var client = new ProwlarrClient(new FakeHttpClientFactory(handler), factory, config);

        var result = await client.SearchAsync("ABC-123");

        Assert.True(result.Success);
        Assert.StartsWith("http://env-host:9696/api/v1/search", Assert.Single(seen).Url);
        Assert.Equal("envkey", seen[0].ApiKey);
    }
}
