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

    private static async Task<(ProwlarrClient Client, TestDbContextFactory Factory)> CreateClientAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://localhost:9696", ApiKey = "testkey" });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) => Task.FromResult(respond(req)));
        return (new ProwlarrClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build()), factory);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task GetDisabledIndexersAsync_ReportsManuallyDisabledAndTemporarilyBlocked()
    {
        var future = DateTime.UtcNow.AddHours(2).ToString("o");
        var past = DateTime.UtcNow.AddHours(-2).ToString("o");
        var (client, factory) = await CreateClientAsync(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/indexer" => Json("""
                [{"id":1,"name":"Alpha","enable":true},{"id":2,"name":"Beta","enable":false},
                 {"id":3,"name":"Gamma","enable":true},{"id":4,"name":"Delta","enable":true}]
                """),
            "/api/v1/indexerstatus" => Json($$"""
                [{"indexerId":3,"disabledTill":"{{future}}"},{"indexerId":4,"disabledTill":"{{past}}"},{"indexerId":1}]
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var _ = factory;

        var result = await client.GetDisabledIndexersAsync();

        Assert.Collection(result,
            beta => { Assert.Equal("Beta", beta.Name); Assert.Null(beta.DisabledTill); Assert.Equal("disabled in Prowlarr", beta.Reason); },
            gamma => { Assert.Equal("Gamma", gamma.Name); Assert.NotNull(gamma.DisabledTill); Assert.Contains("repeated failures", gamma.Reason); });
    }

    [Fact]
    public async Task GetDisabledIndexersAsync_StatusEndpointFails_StillReportsManuallyDisabled()
    {
        var (client, factory) = await CreateClientAsync(req => req.RequestUri!.AbsolutePath == "/api/v1/indexer"
            ? Json("""[{"id":2,"name":"Beta","enable":false}]""")
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var _ = factory;

        var result = await client.GetDisabledIndexersAsync();

        Assert.Equal("Beta", Assert.Single(result).Name);
    }

    [Fact]
    public async Task GetDisabledIndexersAsync_ServerError_ReturnsEmpty()
    {
        var (client, factory) = await CreateClientAsync(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var _ = factory;

        Assert.Empty(await client.GetDisabledIndexersAsync());
    }

    [Fact]
    public async Task GetDisabledIndexersAsync_NotConfigured_ReturnsEmpty()
    {
        using var factory = new TestDbContextFactory();
        var client = new ProwlarrClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());

        Assert.Empty(await client.GetDisabledIndexersAsync());
    }
}
