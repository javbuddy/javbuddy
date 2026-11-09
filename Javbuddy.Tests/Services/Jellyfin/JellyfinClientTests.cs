using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Jellyfin;

public class JellyfinClientTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(null, null)]
    [InlineData("invalid", null)]
    public void JellyfinEnvConfig_GetEnabled_ParsesBoolean(string? value, bool? expected)
    {
        var inMemory = new Dictionary<string, string?>();
        if (value is not null) inMemory["Jellyfin:Enabled"] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var result = JellyfinEnvConfig.GetEnabled(config);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsFalse_WhenDisabledInDb()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = false });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());
        Assert.False(await client.IsEnabledAsync());
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsFalse_WhenDisabledViaEnv()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = true });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jellyfin:Enabled"] = "false"
        }).Build();
        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, config);
        Assert.False(await client.IsEnabledAsync());
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsTrue_WhenConfiguredAndEnabled()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());
        Assert.True(await client.IsEnabledAsync());
    }

    [Fact]
    public async Task Disabled_ReturnsEarlyWithoutHttpCalls()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = false });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());

        var lookup = await client.LookupMovieAsync("test");
        Assert.True(lookup.Success);
        Assert.Empty(lookup.Items!);

        var webUrl = await client.GetWebUrlAsync("item-1", "server-1");
        Assert.Null(webUrl);
    }

    [Fact]
    public async Task GetServerIdAsync_ReturnsServerIdFromSystemInfo_AndCachesResult()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var calls = 0;
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            calls++;
            Assert.Equal("http://localhost:8096/System/Info?apikey=testkey", req.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":\"server-guid-1234\",\"ServerName\":\"MyJellyfin\"}")
            });
        });

        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var first = await client.GetServerIdAsync();
        var second = await client.GetServerIdAsync();

        Assert.Equal("server-guid-1234", first);
        Assert.Equal("server-guid-1234", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetServerIdAsync_ReturnsNull_WhenSystemInfoFails()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.InternalServerError);
        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var serverId = await client.GetServerIdAsync();

        Assert.Null(serverId);
    }

    [Fact]
    public async Task GetWebUrlAsync_ResolvesServerIdDynamically_WhenOmitted()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings
            {
                BaseUrl = "http://localhost:8096",
                ExternalUrl = "https://jellyfin.example.com",
                ApiKey = "testkey",
                Enabled = true
            });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":\"server-dynamic-999\"}")
            }));

        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var url = await client.GetWebUrlAsync("person-abc");

        Assert.Equal("https://jellyfin.example.com/web/index.html#!/details?id=person-abc&serverId=server-dynamic-999", url);
    }

    [Fact]
    public async Task GetWebUrlAsync_ReturnsNull_WhenServerIdCannotBeResolved()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings
            {
                BaseUrl = "http://localhost:8096",
                ApiKey = "testkey",
                Enabled = true
            });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.InternalServerError);
        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var url = await client.GetWebUrlAsync("person-abc");

        Assert.Null(url);
    }

    [Fact]
    public async Task GetWebUrlAsync_UsesExplicitServerId_WithoutResolvingDynamicServerId()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings
            {
                BaseUrl = "http://localhost:8096",
                ApiKey = "testkey",
                Enabled = true
            });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());

        var url = await client.GetWebUrlAsync("item-abc", "server-explicit-123");

        Assert.Equal("http://localhost:8096/web/index.html#!/details?id=item-abc&serverId=server-explicit-123", url);
    }

    [Fact]
    public async Task GetWebUrlAsync_ReturnsNull_WhenDisabled()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = false });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());
        var webUrl = await client.GetWebUrlAsync("item-1");
        Assert.Null(webUrl);
    }

    [Fact]
    public async Task LookupPersonAsync_ReturnsPerson_WhenDirectLookupSucceeds()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal("/Persons/Yua%20Mikami?apikey=testkey", req.RequestUri?.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":\"person-123\",\"Name\":\"Yua Mikami\",\"ServerId\":\"server-1\"}")
            });
        });

        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var person = await client.LookupPersonAsync("Yua Mikami");

        Assert.NotNull(person);
        Assert.Equal("person-123", person.Id);
        Assert.Equal("Yua Mikami", person.Name);
    }

    [Fact]
    public async Task LookupPersonAsync_FallsBackToSearchTerm_WhenDirectLookupNotFound()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri?.AbsolutePath.StartsWith("/Persons/") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Items\":[{\"Id\":\"person-search-456\",\"Name\":\"Yua Mikami\",\"ServerId\":\"server-1\"}],\"TotalRecordCount\":1}")
            });
        });

        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var person = await client.LookupPersonAsync("Yua Mikami");

        Assert.NotNull(person);
        Assert.Equal("person-search-456", person.Id);
        Assert.Equal("Yua Mikami", person.Name);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WithVersionAndServerId()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var calls = 0;
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            calls++;
            Assert.Equal("http://localhost:8096/System/Info?apikey=testkey", req.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":\"server-guid-1234\",\"Version\":\"10.9.7\",\"ServerName\":\"MyJellyfin\"}")
            });
        });

        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("Connected successfully.", result.Message);
        Assert.Equal("10.9.7", result.Version);
        Assert.Equal("server-guid-1234", result.ServerId);

        // Server ID cached from the TestConnectionAsync call:
        var cachedServerId = await client.GetServerIdAsync();
        Assert.Equal("server-guid-1234", cachedServerId);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenUnauthorized()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "badkey", Enabled = true });
            await db.SaveChangesAsync();
        }

        var handler = FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.Unauthorized);
        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Reached the server, but the API key was rejected.", result.Message);
        Assert.Null(result.Version);
        Assert.Null(result.ServerId);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var client = new JellyfinClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.OK)), factory, new ConfigurationBuilder().Build());
        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Equal("Set a base URL and API key first.", result.Message);
        Assert.Null(result.Version);
        Assert.Null(result.ServerId);
    }

    private static async Task<JellyfinClient> CreateLookupClientAsync(FakeHttpMessageHandler handler)
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }
        return new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
    }

    private static string ItemsJson(int count) =>
        "{\"Items\":[" + string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"Id\":\"item-{i}\",\"Name\":\"Movie {i}\",\"ServerId\":\"s1\"}}")) + "]}";

    [Fact]
    public async Task LookupMovieAsync_IsOneRequestAcrossTheWholeServer_AndAsksOnlyForWhatItUses()
    {
        var urls = new List<string>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ItemsJson(2)) });
        });
        var client = await CreateLookupClientAsync(handler);

        var result = await client.LookupMovieAsync("ABC-123");

        Assert.True(result.Success);
        Assert.Equal(["item-0", "item-1"], result.Items!.Select(i => i.Id).ToList());
        Assert.Equal("s1", result.Items![0].ServerId);
        var url = Assert.Single(urls);
        Assert.Contains("searchTerm=ABC-123", url);
        Assert.Contains("includeItemTypes=Movie", url);
        Assert.Contains("enableImages=false", url);
        Assert.Contains("enableUserData=false", url);
        Assert.Contains("enableTotalRecordCount=false", url);
        Assert.DoesNotContain("parentId", url);
        Assert.DoesNotContain("limit=", url);
        Assert.DoesNotContain("startIndex", url);
    }

    [Fact]
    public async Task LookupMovieAsync_ReportsAFailedRequest()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.InternalServerError);
        var client = await CreateLookupClientAsync(handler);

        var result = await client.LookupMovieAsync("ABC-123");

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task LookupMovieAsync_NotConfigured_ReturnsAnEmptyResultWithoutCallingTheServer()
    {
        using var factory = new TestDbContextFactory();
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not be called"));
        var client = new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());

        var result = await client.LookupMovieAsync("ABC-123");

        Assert.True(result.Success);
        Assert.Empty(result.Items!);
    }
}
