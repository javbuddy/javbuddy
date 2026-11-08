using System.Net;
using System.Text.Json;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;

namespace Javbuddy.E2ETests.Api;

/// <summary>HTTP contract of the read routes under /api/v1 — plain HttpClient against the real Kestrel app, no browser.</summary>
[Collection(E2ECollection.Name)]
public class ReadApiTests(E2EFixture fixture)
{
    private HttpClient NewClient() => new() { BaseAddress = new Uri(fixture.App.ServerAddress) };

    private async Task<string> SeedAsync(string prefix)
    {
        var code = $"{prefix}-{Random.Shared.Next(100, 999)}";
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, code);
        return code;
    }

    [Fact]
    public async Task SystemInfo_ReturnsVersionAndIntegrations()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/api/v1/system/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        Assert.True(json.GetProperty("integrations").GetProperty("javinizer").GetBoolean());
        Assert.False(json.GetProperty("integrations").GetProperty("prowlarr").GetBoolean());
    }

    [Fact]
    public async Task ListMovies_ReturnsCamelCaseShapeWithAbsoluteWebUrl()
    {
        var code = await SeedAsync("APIL");
        using var http = NewClient();

        var json = await http.GetFromJsonAsync<JsonElement>($"/api/v1/movies?q={code}&status=got&take=5");

        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var item = json.GetProperty("items")[0];
        Assert.Equal(code, item.GetProperty("code").GetString());
        Assert.Equal("got", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("contentType").ValueKind);
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", item.GetProperty("webUrl").GetString());
    }

    [Fact]
    public async Task ListMovies_TakeOver200_Is400Problem()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/api/v1/movies?take=201");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.True(json.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ListMovies_UnknownStatus_Is400()
    {
        using var http = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/movies?status=bogus")).StatusCode);
    }

    [Fact]
    public async Task Lookup_AcceptsCommaSeparatedAndRepeatedCodes()
    {
        var code = await SeedAsync("APIK");
        using var http = NewClient();

        var comma = await http.GetFromJsonAsync<JsonElement>($"/api/v1/movies/lookup?codes={code.ToLowerInvariant()},NOPE-1");
        var repeated = await http.GetFromJsonAsync<JsonElement>($"/api/v1/movies/lookup?codes={code}&codes=NOPE-1");

        foreach (var json in new[] { comma, repeated })
        {
            Assert.Equal(2, json.GetArrayLength());
            Assert.True(json[0].GetProperty("inLibrary").GetBoolean());
            Assert.Equal(code, json[0].GetProperty("code").GetString());
            Assert.EndsWith($"/movies/{code}", json[0].GetProperty("movie").GetProperty("webUrl").GetString());
            Assert.False(json[1].GetProperty("inLibrary").GetBoolean());
            Assert.Equal(JsonValueKind.Null, json[1].GetProperty("movie").ValueKind);
        }
    }

    [Fact]
    public async Task Lookup_MoreThan100Codes_Is400()
    {
        using var http = NewClient();
        var codes = string.Join(",", Enumerable.Range(1, 101).Select(i => $"ZZZ-{i}"));

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/v1/movies/lookup?codes={codes}")).StatusCode);
    }

    [Fact]
    public async Task Lookup_NoCodes_Is400()
    {
        using var http = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/movies/lookup")).StatusCode);
    }

    [Fact]
    public async Task GetMovie_ReturnsDetailWithWebUrl()
    {
        var code = await SeedAsync("APID");
        using var http = NewClient();

        var json = await http.GetFromJsonAsync<JsonElement>($"/api/v1/movies/{code.ToLowerInvariant()}");

        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.Equal($"Test Movie {code}", json.GetProperty("title").GetString());
        Assert.Equal("got", json.GetProperty("status").GetString());
        Assert.Equal("2024-01-01", json.GetProperty("releaseDate").GetString());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("tags").ValueKind);
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", json.GetProperty("webUrl").GetString());
    }

    [Fact]
    public async Task GetMovie_UnknownCode_Is404ProblemJsonNotHtml()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/api/v1/movies/NOPE-999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UnknownApiRoute_Is404ProblemJsonNotHtml()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/api/v1/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownNonApiRoute_StillServesTheHtmlNotFoundPage()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/this-is-not-an-api-route");

        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SearchTags_ReturnsArray()
    {
        using var http = NewClient();

        var json = await http.GetFromJsonAsync<JsonElement>("/api/v1/tags?q=zzzz-no-such-tag");

        Assert.Equal(JsonValueKind.Array, json.ValueKind);
    }

    [Fact]
    public async Task SearchActors_ReturnsMatchingActor()
    {
        var name = $"ApiActress{Random.Shared.Next(1000, 9999)}";
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, name);
        using var http = NewClient();

        var json = await http.GetFromJsonAsync<JsonElement>($"/api/v1/actors?q={name}");

        Assert.Contains(json.EnumerateArray(), a => a.GetProperty("id").GetInt32() == actor.Id);
    }

    [Fact]
    public async Task SearchActors_WithoutQuery_Is400()
    {
        using var http = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/actors")).StatusCode);
    }

    [Fact]
    public async Task OriginGuard_ForeignOrigin_Is403Problem()
    {
        using var http = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OriginGuard_OwnOriginAndNoOrigin_AreAllowed()
    {
        using var http = NewClient();
        using var own = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        own.Headers.Add("Origin", fixture.App.ServerAddress.TrimEnd('/'));

        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(own)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/system/info")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_ListsApiRoutesOnly()
    {
        using var http = NewClient();

        var response = await http.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paths");
        var names = paths.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/movies", names);
        Assert.Contains("/api/v1/movies/{code}", names);
        Assert.All(names, n => Assert.StartsWith("/api/v1/", n));
        Assert.DoesNotContain(names, n => n.Contains("image-cache"));
    }
}
