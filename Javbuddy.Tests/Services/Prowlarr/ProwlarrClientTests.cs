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
}
