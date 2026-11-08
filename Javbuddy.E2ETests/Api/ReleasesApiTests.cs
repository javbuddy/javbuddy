using System.Net;
using System.Text;
using System.Text.Json;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;

namespace Javbuddy.E2ETests.Api;

/// <summary>Routing and error shapes of the release search and grab routes. Prowlarr is the fake server and nothing here reaches a
/// real qBittorrent: the grab that gets past the cache is stopped by an app with qBittorrent unconfigured.</summary>
[Collection(E2ECollection.Name)]
public class ReleasesApiTests(E2EFixture fixture)
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Search_ProwlarrNotConfigured_Is503Problem()
    {
        // The shared fixture leaves Prowlarr unconfigured (see E2EFixture).
        using var http = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };

        var response = await http.GetAsync("/api/v1/movies/ZZZ-123/releases");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Grab_UnknownGuid_Is410Problem_AndBadBodiesAre400Or415()
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };

        var gone = await http.PostAsync("/api/v1/movies/ZZZ-123/releases/grab", Json("""{"guid":"never-searched"}"""));

        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Equal("application/problem+json", gone.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Search for releases again", await gone.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/v1/movies/ZZZ-123/releases/grab", Json("{}"))).StatusCode);
        // A client can't smuggle a URL in: unknown properties are rejected.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PostAsync("/api/v1/movies/ZZZ-123/releases/grab", Json("""{"guid":"x","magnetUrl":"magnet:?xt=evil"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await http.PostAsync("/api/v1/movies/ZZZ-123/releases/grab", new StringContent("""{"guid":"x"}""", Encoding.UTF8, "text/plain"))).StatusCode);
    }

    [Fact]
    public async Task Search_ThenGrab_WithQBittorrentUnconfigured_ReturnsReleasesWithoutUrls_And503()
    {
        fixture.ResetFakes();
        await using var app = new JavbuddyAppFactory
        {
            IntegrationSettings = new Dictionary<string, string>
            {
                ["Prowlarr:BaseUrl"] = fixture.FakeProwlarr.Address,
                ["Prowlarr:ApiKey"] = FakeProwlarrServer.ApiKey,
            },
        };
        app.StartServer();
        using var http = new HttpClient { BaseAddress = new Uri(app.ServerAddress) };

        var search = await http.GetAsync("/api/v1/movies/NOT-IN-LIB-1/releases?q=custom%20query");

        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal("custom query", fixture.FakeProwlarr.LastQuery);
        var text = await search.Content.ReadAsStringAsync();
        Assert.DoesNotContain("magnet", text);
        var json = JsonDocument.Parse(text).RootElement;
        var release = json.GetProperty("releases")[0];
        Assert.Equal("fake-release-1", release.GetProperty("guid").GetString());
        Assert.Equal(10, release.GetProperty("seeders").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("warnings").ValueKind);

        var grab = await http.PostAsync("/api/v1/movies/NOT-IN-LIB-1/releases/grab", Json("""{"guid":"fake-release-1"}"""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, grab.StatusCode);
        // Nothing was added to the library by the refused grab.
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/movies/NOT-IN-LIB-1")).StatusCode);
    }
}
