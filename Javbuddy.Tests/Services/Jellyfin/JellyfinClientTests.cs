using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Jellyfin;

public class JellyfinClientTests
{
    [Fact]
    public void JellyfinEnvConfig_GetSelectedLibraryNames_ReadsIndexedArray()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames:0"] = "lib-1",
            ["Jellyfin:SelectedLibraryNames:1"] = "lib-2",
            ["Jellyfin:SelectedLibraryNames:2"] = "  lib-3  "
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var names = JellyfinEnvConfig.GetSelectedLibraryNames(config);

        Assert.Equal(3, names.Count);
        Assert.Equal("lib-1", names[0]);
        Assert.Equal("lib-2", names[1]);
        Assert.Equal("lib-3", names[2]);
    }

    [Fact]
    public void JellyfinEnvConfig_GetSelectedLibraryNames_ReadsJsonArrayString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames"] = "[\"lib-1\", \"lib-2\"]"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var names = JellyfinEnvConfig.GetSelectedLibraryNames(config);

        Assert.Equal(2, names.Count);
        Assert.Equal("lib-1", names[0]);
        Assert.Equal("lib-2", names[1]);
    }

    [Fact]
    public void JellyfinEnvConfig_GetSelectedLibraryNames_ReadsCommaDelimitedString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames"] = "lib-1, lib-2"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var names = JellyfinEnvConfig.GetSelectedLibraryNames(config);

        Assert.Equal(2, names.Count);
        Assert.Equal("lib-1", names[0]);
        Assert.Equal("lib-2", names[1]);
    }

    [Fact]
    public void JellyfinEnvConfig_GetSelectedLibraryNames_ReadsNewlineDelimitedString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames"] = "lib-1\nlib-2\r\nlib-3"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var names = JellyfinEnvConfig.GetSelectedLibraryNames(config);

        Assert.Equal(3, names.Count);
        Assert.Equal("lib-1", names[0]);
        Assert.Equal("lib-2", names[1]);
        Assert.Equal("lib-3", names[2]);
    }

    [Fact]
    public void JellyfinEnvConfig_GetSelectedLibraryNames_EmptyReturnsEmptyList()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames"] = ""
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var names = JellyfinEnvConfig.GetSelectedLibraryNames(config);

        Assert.Empty(names);
    }

    [Fact]
    public async Task GetSelectedLibraryNamesAsync_PrefersEnvOverDb()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", SelectedLibraryNames = "db-lib1,db-lib2" });
            await db.SaveChangesAsync();
        }

        var inMemory = new Dictionary<string, string?>
        {
            ["Jellyfin:SelectedLibraryNames:0"] = "env-lib1",
            ["Jellyfin:SelectedLibraryNames:1"] = "env-lib2"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, config);
        var names = await client.GetSelectedLibraryNamesAsync();

        Assert.Equal(2, names.Count);
        Assert.Equal("env-lib1", names[0]);
        Assert.Equal("env-lib2", names[1]);
    }

    [Fact]
    public async Task GetSelectedLibraryNamesAsync_FallsBackToDbWhenEnvNotSet()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", SelectedLibraryNames = "db-lib1\ndb-lib2" });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().Build();
        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, config);
        var names = await client.GetSelectedLibraryNamesAsync();

        Assert.Equal(2, names.Count);
        Assert.Equal("db-lib1", names[0]);
        Assert.Equal("db-lib2", names[1]);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a,b,c", 3)]
    [InlineData("a;b;c", 3)]
    [InlineData("a\nb\nc", 3)]
    [InlineData("[\"a\",\"b\"]", 2)]
    public void ParseLibraryNames_ParsesVariousFormats(string? raw, int expectedCount)
    {
        var result = JellyfinClient.ParseLibraryNames(raw);
        Assert.Equal(expectedCount, result.Count);
    }

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
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost", ApiKey = "k", Enabled = false, SelectedLibraryNames = "Movies" });
            await db.SaveChangesAsync();
        }

        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());

        var libraries = await client.GetLibrariesAsync();
        Assert.False(libraries.Success);
        Assert.Equal("Jellyfin integration is disabled.", libraries.ErrorMessage);

        var lookup = await client.LookupInSelectedLibrariesAsync("test");
        Assert.True(lookup.Success);
        Assert.Empty(lookup.Items!);

        var list = await client.ListMoviesInSelectedLibrariesAsync();
        Assert.True(list.Success);
        Assert.Empty(list.Items!);

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
            db.Movies.Add(new Movie { Code = "TEST-001", JellyfinServerId = "db-server-5678" });
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

    private const string TrickplayItemJson = """
        {"Items":[{"Id":"item-abc","RunTimeTicks":1800000000,"Trickplay":{"item-abc":{"320":{"Width":320,"Height":180,"TileWidth":10,"TileHeight":10,"ThumbnailCount":18,"Interval":10000,"Bandwidth":492}}}}],"TotalRecordCount":1}
        """;

    private static async Task<(JellyfinClient Client, FakeHttpMessageHandler Handler)> CreateTrickplayClientAsync(
        FakeHttpMessageHandler handler, string? externalUrl = "https://jellyfin.example.com", bool enabled = true)
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings
            {
                BaseUrl = "http://jellyfin-internal:8096",
                ExternalUrl = externalUrl,
                ApiKey = "testkey",
                Enabled = enabled
            });
            await db.SaveChangesAsync();
        }
        return (new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build()), handler);
    }

    private static FakeHttpMessageHandler JsonHandler(string json) =>
        FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.OK, json);

    [Fact]
    public async Task GetTrickplayAsync_ReturnsGeometryAndBrowserTileUrlTemplate()
    {
        var (client, handler) = await CreateTrickplayClientAsync(JsonHandler(TrickplayItemJson));

        var trickplay = await client.GetTrickplayAsync("item-abc");

        Assert.NotNull(trickplay);
        Assert.Equal(320, trickplay.Width);
        Assert.Equal(180, trickplay.Height);
        Assert.Equal(10, trickplay.TileWidth);
        Assert.Equal(10, trickplay.TileHeight);
        Assert.Equal(18, trickplay.ThumbnailCount);
        Assert.Equal(10000, trickplay.IntervalMs);
        Assert.Equal(180d, trickplay.DurationSeconds);
        Assert.Equal("https://jellyfin.example.com/Videos/item-abc/Trickplay/320/{index}.jpg?mediaSourceId=item-abc&api_key=testkey", trickplay.TileUrlTemplate);
        // The server-side lookup goes to the internal BaseUrl, not the browser-facing ExternalUrl.
        Assert.Equal("http://jellyfin-internal:8096/Items?ids=item-abc&fields=Trickplay&apikey=testkey", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetTrickplayAsync_FallsBackToBaseUrl_WhenExternalUrlNotSet()
    {
        var (client, _) = await CreateTrickplayClientAsync(JsonHandler(TrickplayItemJson), externalUrl: null);

        var trickplay = await client.GetTrickplayAsync("item-abc");

        Assert.StartsWith("http://jellyfin-internal:8096/Videos/item-abc/Trickplay/320/", trickplay?.TileUrlTemplate);
    }

    [Fact]
    public async Task GetTrickplayAsync_PicksTheSmallestAvailableWidth()
    {
        const string json = """
            {"Items":[{"Id":"item-abc","RunTimeTicks":1800000000,"Trickplay":{"item-abc":{
              "640":{"Width":640,"Height":360,"TileWidth":5,"TileHeight":5,"ThumbnailCount":18,"Interval":10000,"Bandwidth":900},
              "320":{"Width":320,"Height":180,"TileWidth":10,"TileHeight":10,"ThumbnailCount":18,"Interval":10000,"Bandwidth":492}}}}]}
            """;
        var (client, _) = await CreateTrickplayClientAsync(JsonHandler(json));

        var trickplay = await client.GetTrickplayAsync("item-abc");

        Assert.Equal(320, trickplay?.Width);
        Assert.Contains("/Trickplay/320/{index}.jpg", trickplay?.TileUrlTemplate);
    }

    [Fact]
    public async Task GetTrickplayAsync_PrefersTheMediaSourceMatchingTheItemId()
    {
        const string json = """
            {"Items":[{"Id":"item-abc","RunTimeTicks":1800000000,"Trickplay":{
              "other-source":{"160":{"Width":160,"Height":90,"TileWidth":10,"TileHeight":10,"ThumbnailCount":18,"Interval":10000,"Bandwidth":100}},
              "item-abc":{"320":{"Width":320,"Height":180,"TileWidth":10,"TileHeight":10,"ThumbnailCount":18,"Interval":10000,"Bandwidth":492}}}}]}
            """;
        var (client, _) = await CreateTrickplayClientAsync(JsonHandler(json));

        var trickplay = await client.GetTrickplayAsync("item-abc");

        Assert.Equal(320, trickplay?.Width);
        Assert.Contains("mediaSourceId=item-abc", trickplay?.TileUrlTemplate);
    }

    [Theory]
    [InlineData("""{"Items":[{"Id":"item-abc","RunTimeTicks":1800000000,"Trickplay":{}}]}""")]
    [InlineData("""{"Items":[{"Id":"item-abc","RunTimeTicks":1800000000}]}""")]
    [InlineData("""{"Items":[{"Id":"item-abc","Trickplay":{"item-abc":{"320":{"Width":320,"Height":180,"TileWidth":10,"TileHeight":10,"ThumbnailCount":18,"Interval":10000}}}}]}""")]
    [InlineData("""{"Items":[]}""")]
    [InlineData("""{"Items":[{"Id":"item-abc","RunTimeTicks":1800000000,"Trickplay":{"item-abc":{"320":{"Width":320,"Height":180,"TileWidth":0,"TileHeight":10,"ThumbnailCount":18,"Interval":10000}}}}]}""")]
    public async Task GetTrickplayAsync_ReturnsNull_WhenItemHasNoUsableTrickplay(string json)
    {
        var (client, _) = await CreateTrickplayClientAsync(JsonHandler(json));

        Assert.Null(await client.GetTrickplayAsync("item-abc"));
    }

    [Fact]
    public async Task GetTrickplayAsync_ReturnsNull_WhenServerErrors()
    {
        var (client, _) = await CreateTrickplayClientAsync(FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.InternalServerError));

        Assert.Null(await client.GetTrickplayAsync("item-abc"));
    }

    [Fact]
    public async Task GetTrickplayAsync_ReturnsNullWithoutCallingServer_WhenDisabled()
    {
        var (client, handler) = await CreateTrickplayClientAsync(JsonHandler(TrickplayItemJson), enabled: false);

        Assert.Null(await client.GetTrickplayAsync("item-abc"));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GetTrickplayAsync_ReturnsNull_WhenNotConfigured()
    {
        using var factory = new TestDbContextFactory();
        var client = new JellyfinClient(Substitute.For<IHttpClientFactory>(), factory, new ConfigurationBuilder().Build());

        Assert.Null(await client.GetTrickplayAsync("item-abc"));
    }

    private static async Task<JellyfinClient> CreateItemsClientAsync(FakeHttpMessageHandler handler)
    {
        var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "testkey", Enabled = true });
            await db.SaveChangesAsync();
        }
        return new JellyfinClient(new FakeHttpClientFactory(handler), factory, new ConfigurationBuilder().Build());
    }

    private static string ItemsJson(int from, int count) =>
        "{\"Items\":[" + string.Join(",", Enumerable.Range(from, count).Select(i => $"{{\"Id\":\"item-{i}\",\"Name\":\"Movie {i}\",\"ServerId\":\"s1\"}}")) + "]}";

    [Fact]
    public async Task LookupAsync_LibraryListing_ReadsPagesUntilShortPage_AndAsksOnlyForWhatItUses()
    {
        var urls = new List<string>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query);
            var start = int.Parse(query["startIndex"]!);
            var count = start == 0 ? 500 : start == 500 ? 500 : 3;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ItemsJson(start, count)) });
        });
        var client = await CreateItemsClientAsync(handler);

        var result = await client.LookupAsync("", "lib-1");

        Assert.True(result.Success);
        Assert.Equal(1003, result.Items!.Count);
        Assert.All(result.Items, i => Assert.Equal("lib-1", i.LibraryId));
        Assert.Equal(3, urls.Count);
        Assert.All(urls, u =>
        {
            Assert.Contains("enableImages=false", u);
            Assert.Contains("enableUserData=false", u);
            Assert.Contains("enableTotalRecordCount=false", u);
            Assert.Contains("limit=500", u);
            Assert.Contains("parentId=lib-1", u);
        });
        Assert.Contains("startIndex=1000", urls[2]);
    }

    [Fact]
    public async Task LookupAsync_Search_IsOneUnpagedRequest()
    {
        var urls = new List<string>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ItemsJson(0, 2)) });
        });
        var client = await CreateItemsClientAsync(handler);

        var result = await client.LookupAsync("ABC-123", "lib-1");

        Assert.Equal(2, result.Items!.Count);
        var url = Assert.Single(urls);
        Assert.Contains("searchTerm=ABC-123", url);
        Assert.DoesNotContain("limit=", url);
        Assert.DoesNotContain("startIndex", url);
    }

    [Fact]
    public async Task LookupAsync_LibraryListing_StopsWhenAServerIgnoresPagingAndRepeatsItems()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ItemsJson(0, 500)) });
        });
        var client = await CreateItemsClientAsync(handler);

        var result = await client.LookupAsync("", "lib-1");

        Assert.Equal(500, result.Items!.Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LookupAsync_LibraryListing_ReportsAFailedPage()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ItemsJson(0, 500)) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        });
        var client = await CreateItemsClientAsync(handler);

        var result = await client.LookupAsync("", "lib-1");

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }
}
