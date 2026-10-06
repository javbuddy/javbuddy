using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Javinizer;

public class JavinizerClientTests
{
    [Fact]
    public void JavinizerEnvConfig_GetDestinationAliases_ReadsIndexedArray()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Javinizer:DestinationAliases:0:Name"] = "jav",
            ["Javinizer:DestinationAliases:0:Path"] = "/media/jav",
            ["Javinizer:DestinationAliases:1:Name"] = "vr",
            ["Javinizer:DestinationAliases:1:Path"] = "/media/vr"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var aliases = JavinizerEnvConfig.GetDestinationAliases(config);

        Assert.Equal(2, aliases.Count);
        Assert.Equal(new DestinationAlias("jav", "/media/jav"), aliases[0]);
        Assert.Equal(new DestinationAlias("vr", "/media/vr"), aliases[1]);
    }

    [Fact]
    public void JavinizerEnvConfig_GetDestinationAliases_SkipsEntriesMissingNameOrPath()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Javinizer:DestinationAliases:0:Name"] = "jav",
            ["Javinizer:DestinationAliases:0:Path"] = "/media/jav",
            ["Javinizer:DestinationAliases:1:Name"] = "",
            ["Javinizer:DestinationAliases:1:Path"] = "/media/incomplete",
            ["Javinizer:DestinationAliases:2:Name"] = "no-path",
            ["Javinizer:DestinationAliases:2:Path"] = ""
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var aliases = JavinizerEnvConfig.GetDestinationAliases(config);

        Assert.Single(aliases);
        Assert.Equal(new DestinationAlias("jav", "/media/jav"), aliases[0]);
    }

    [Fact]
    public void JavinizerEnvConfig_GetDestinationAliases_EmptyReturnsEmptyList()
    {
        var config = new ConfigurationBuilder().Build();

        var aliases = JavinizerEnvConfig.GetDestinationAliases(config);

        Assert.Empty(aliases);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WithCurrentVersion()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("http://localhost:8765/api/v1/version", req.RequestUri?.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("testtoken", req.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"current\":\"1.5.1\",\"commit\":\"abcdef\",\"build_date\":\"2026-03-24\"}")
            });
        });

        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Equal("1.5.1", result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WithFallbackVersionField()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "{\"version\":\"1.5.0\"}");
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Equal("1.5.0", result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenVersionMissingOrMalformed()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "not-valid-json");
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
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
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "{}");
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
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
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "badtoken" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.Unauthorized);
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Reached the server, but the API token was rejected.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var client = new JavinizerClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Set a base URL and API token first.", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenServerReturns500()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.InternalServerError, "{\"message\":\"Internal error\"}");
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Server returned 500: Internal error", result.Message);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task TestConnectionAsync_FallsBackToConfig_WhenVersionEndpointNotFound()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "testtoken" });
            await db.SaveChangesAsync();
        }

        var requestCount = 0;
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            requestCount++;
            if (req.RequestUri?.AbsolutePath == "/api/v1/version")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (req.RequestUri?.AbsolutePath == "/api/v1/config")
            {
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                Assert.Equal("testtoken", req.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Null(result.Version);
        Assert.Equal(2, requestCount);
    }

    private static async Task<TestDbContextFactory> SeededFactoryAsync()
    {
        var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "t" });
        await db.SaveChangesAsync();
        return factory;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetResultSourcesAsync_GetsSourcesRoute_AndParsesResults()
    {
        using var factory = await SeededFactoryAsync();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("http://localhost:8765/api/v1/batch/job%201/results/r1/sources", req.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json("""{"results":[{"source":"dmm","maker":"S1","release_date":"2020-01-02T00:00:00Z","runtime":120,"genres":["VR"],"actresses":[{"dmm_id":3,"first_name":"Yua"}]}]}"""));
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.GetResultSourcesAsync("job 1", "r1");

        Assert.True(result.Success);
        var src = Assert.Single(result.Results!);
        Assert.Equal(("dmm", "S1", "VR", (int?)3, (int?)120), (src.Source, src.Maker, src.Genres![0], src.Actresses![0].DmmId, src.Runtime));
    }

    [Fact]
    public async Task OverrideFieldAsync_PostsFieldAndSource()
    {
        using var factory = await SeededFactoryAsync();
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://localhost:8765/api/v1/batch/j/results/r/field-override", req.RequestUri!.AbsoluteUri);
            body = await req.Content!.ReadAsStringAsync(ct);
            return Json("""{"movie":{"maker":"S1"},"field_sources":{"maker":"dmm"}}""");
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.OverrideFieldAsync("j", "r", "maker", "dmm");

        Assert.True(result.Success);
        Assert.Equal("S1", result.Data!.Movie!.Maker);
        Assert.Equal("dmm", result.Data.FieldSources!["maker"]);
        Assert.Contains("\"field\":\"maker\"", body);
        Assert.Contains("\"source\":\"dmm\"", body);
    }

    [Fact]
    public async Task OverrideFieldAsync_Conflict_ReturnsError()
    {
        using var factory = await SeededFactoryAsync();
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.Conflict, """{"error":"job busy"}""");
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.OverrideFieldAsync("j", "r", "maker", "dmm");

        Assert.False(result.Success);
        Assert.Contains("409", result.ErrorMessage);
    }

    [Fact]
    public async Task GetScrapersAsync_ReturnsList()
    {
        using var factory = await SeededFactoryAsync();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("http://localhost:8765/api/v1/scrapers", req.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json("""{"scrapers":[{"name":"dmm","display_title":"DMM","enabled":true},{"name":"javdb","display_title":"JavDB","enabled":false}]}"""));
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.GetScrapersAsync();

        Assert.True(result.Success);
        Assert.Equal(["dmm", "javdb"], result.Scrapers!.Select(s => s.Name));
        Assert.Equal("DMM", result.Scrapers![0].DisplayTitle);
        Assert.False(result.Scrapers![1].Enabled);
    }

    [Fact]
    public async Task GetTempPosterAsync_ReturnsBytesFromAuthenticatedTempRoute()
    {
        using var factory = await SeededFactoryAsync();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("http://localhost:8765/api/v1/temp/posters/j/ABC-123-full.jpg", req.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.GetTempPosterAsync("j", "ABC-123", fullSize: true);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Bytes);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task GetTempPosterAsync_NotFound_ReturnsError()
    {
        using var factory = await SeededFactoryAsync();
        var client = new JavinizerClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.NotFound)), factory, new ConfigurationBuilder().Build());

        var result = await client.GetTempPosterAsync("j", "ABC-123", fullSize: true);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
        Assert.Contains("404", result.ErrorMessage);
    }

    [Fact]
    public async Task GetTempPosterAsync_NotFullSize_ReadsTheCroppedPoster()
    {
        using var factory = await SeededFactoryAsync();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("http://localhost:8765/api/v1/temp/posters/j/ABC-123.jpg", req.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4]) });
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.GetTempPosterAsync("j", "ABC-123", fullSize: false);

        Assert.True(result.Success);
        Assert.False(result.NotFound);
    }

    [Fact]
    public async Task CropPosterAsync_PostsIntegerBounds()
    {
        using var factory = await SeededFactoryAsync();
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            Assert.Equal("http://localhost:8765/api/v1/batch/j/results/r/poster-crop", req.RequestUri!.AbsoluteUri);
            body = await req.Content!.ReadAsStringAsync(ct);
            return Json("""{"cropped_poster_url":"/api/v1/temp/posters/j/ABC-123.jpg?v=1","should_crop_poster":false}""");
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.CropPosterAsync("j", "r", new PosterCropRequestDto { X = 1, Y = 2, Width = 3, Height = 4 });

        Assert.True(result.Success);
        Assert.Equal("""{"x":1,"y":2,"width":3,"height":4}""", body);
        Assert.False(result.Data!.ShouldCropPoster);
    }

    [Fact]
    public async Task UpdateResultAsync_WithRevision_SendsExpectedResultRevision()
    {
        using var factory = await SeededFactoryAsync();
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return Json("{}");
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        await client.UpdateResultAsync("j", "r", new MovieViewDto { CastVersion = "cv1" }, expectedResultRevision: 7);

        Assert.Contains("\"expected_result_revision\":7", body);
        Assert.Contains("\"cast_version\":\"cv1\"", body);
    }

    [Fact]
    public async Task UpdateResultAsync_WithoutRevision_OmitsIt()
    {
        using var factory = await SeededFactoryAsync();
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return Json("{}");
        });
        var client = new JavinizerClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        await client.UpdateResultAsync("j", "r", new MovieViewDto());

        Assert.DoesNotContain("expected_result_revision", body);
    }
}
