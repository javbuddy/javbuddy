using System.Net;
using System.Text;
using System.Text.Json;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.E2ETests.Api;

/// <summary>HTTP contract of the write routes under /api/v1. Real JSON POST/PATCH/PUT/DELETE through the running app also
/// settles that <c>UseAntiforgery</c> doesn't reject them.</summary>
[Collection(E2ECollection.Name)]
public class WriteApiTests(E2EFixture fixture) : IDisposable
{
    private readonly List<string> scratchFolders = [];

    public void Dispose()
    {
        foreach (var folder in scratchFolders.Where(Directory.Exists)) Directory.Delete(folder, recursive: true);
    }

    private HttpClient NewClient() => new() { BaseAddress = new Uri(fixture.App.ServerAddress) };

    private static StringContent Json(string json, string mediaType = "application/json") => new(json, Encoding.UTF8, mediaType);

    private async Task<string> SeedAsync(string prefix)
    {
        var code = $"{prefix}-{Random.Shared.Next(100, 999)}";
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, code);
        return code;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task AddMovie_Created_ThenDuplicateIs409WithExistingMovie()
    {
        fixture.ResetFakes();
        var code = $"APIW-{Random.Shared.Next(1000, 9999)}";
        using var http = NewClient();

        var created = await http.PostAsync("/api/v1/movies", Json($$"""{"code":"{{code}}"}"""));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{fixture.App.ServerAddress}api/v1/movies/{code}", created.Headers.Location?.ToString());
        var body = await BodyAsync(created);
        Assert.Equal(code, body.GetProperty("movie").GetProperty("code").GetString());
        Assert.Equal("missing", body.GetProperty("movie").GetProperty("status").GetString());
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", body.GetProperty("movie").GetProperty("webUrl").GetString());
        Assert.True(body.TryGetProperty("ownedViaJellyfin", out _));
        Assert.True(body.TryGetProperty("metadataFound", out _));

        var duplicate = await http.PostAsync("/api/v1/movies", Json($$"""{"code":"{{code.ToLowerInvariant()}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        var problem = await BodyAsync(duplicate);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", problem.GetProperty("webUrl").GetString());

        // The shared database outlives this test: don't leave a Missing movie behind for tests that count them.
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Movies.Where(m => m.Code == code).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("""{"code":"not a code!"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"code":"ABP-1","bogus":1}""")]
    [InlineData("""not json""")]
    public async Task AddMovie_BadBody_Is400(string json)
    {
        using var http = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/v1/movies", Json(json))).StatusCode);
    }

    [Fact]
    public async Task AddMovie_NoBody_Is400()
    {
        using var http = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/v1/movies", null)).StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task MutatingRoutes_NonJsonBody_Are415(string mediaType)
    {
        var code = await SeedAsync("APIM");
        using var http = NewClient();

        var post = await http.PostAsync("/api/v1/movies", Json($$"""{"code":"{{code}}"}""", mediaType));
        var patch = await http.PatchAsync($"/api/v1/movies/{code}/metadata", Json("""{"set":{"studio":"X"}}""", mediaType));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, post.StatusCode);
        Assert.Equal("application/problem+json", post.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, patch.StatusCode);
    }

    [Fact]
    public async Task CrossSiteSimplePost_IsRejectedByTheOriginGuard()
    {
        var code = await SeedAsync("APIX");
        using var http = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/movies/{code}/nfo/push");
        request.Headers.Add("Origin", "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task PatchMetadata_SetsAndClears_ThenValidatesAndNotFound()
    {
        var code = await SeedAsync("APIP");
        using var http = NewClient();

        var response = await http.PatchAsync($"/api/v1/movies/{code}/metadata",
            Json("""{"set":{"studio":"Patched Studio","releaseDate":"2025-02-03","contentType":"uncensored"},"clear":["description"]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        var movie = body.GetProperty("movie");
        Assert.Equal("Patched Studio", movie.GetProperty("studio").GetString());
        Assert.Equal("2025-02-03", movie.GetProperty("releaseDate").GetString());
        Assert.Equal("uncensored", movie.GetProperty("contentType").GetString());
        Assert.Equal($"Test Movie {code}", movie.GetProperty("title").GetString());
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", movie.GetProperty("webUrl").GetString());
        Assert.True(body.TryGetProperty("warning", out _));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PatchAsync($"/api/v1/movies/{code}/metadata", Json("""{"set":{"releaseDate":"2025-13-45"}}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PatchAsync($"/api/v1/movies/{code}/metadata", Json("""{"set":{"nonsense":"x"}}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await http.PatchAsync("/api/v1/movies/NOPE-999/metadata", Json("""{"set":{"studio":"X"}}"""))).StatusCode);
    }

    [Fact]
    public async Task Tags_PutAndDelete()
    {
        var code = await SeedAsync("APIT");
        int tagId;
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = $"Api Tag {Guid.NewGuid():N}" };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        }
        using var http = NewClient();

        var put = await http.PutAsync($"/api/v1/movies/{code}/tags/{tagId}", null);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var tags = (await BodyAsync(put)).GetProperty("tags");
        Assert.Contains(tags.EnumerateArray(), t => t.GetProperty("id").GetInt32() == tagId && t.GetProperty("explicit").GetBoolean());

        var delete = await http.DeleteAsync($"/api/v1/movies/{code}/tags/{tagId}");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.DoesNotContain((await BodyAsync(delete)).GetProperty("tags").EnumerateArray(), t => t.GetProperty("id").GetInt32() == tagId);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsync($"/api/v1/movies/{code}/tags/99999999", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsync($"/api/v1/movies/NOPE-999/tags/{tagId}", null)).StatusCode);
    }

    [Fact]
    public async Task Cast_AddByActorId_ThenRemoveByIdAndByName()
    {
        var code = await SeedAsync("APIC");
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, $"ApiCast{Random.Shared.Next(1000, 9999)}");
        using var http = NewClient();

        var add = await http.PostAsync($"/api/v1/movies/{code}/cast", Json($$"""{"actorId":{{actor.Id}}}"""));

        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var cast = (await BodyAsync(add)).GetProperty("cast");
        Assert.Contains(cast.EnumerateArray(), c => c.TryGetProperty("actorId", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == actor.Id);

        var removeById = await http.DeleteAsync($"/api/v1/movies/{code}/cast/{actor.Id}");

        Assert.Equal(HttpStatusCode.OK, removeById.StatusCode);
        Assert.DoesNotContain((await BodyAsync(removeById)).GetProperty("cast").EnumerateArray(),
            c => c.GetProperty("actorId").ValueKind == JsonValueKind.Number && c.GetProperty("actorId").GetInt32() == actor.Id);

        var removeByName = await http.DeleteAsync($"/api/v1/movies/{code}/cast?name=Test%20Actress");

        Assert.Equal(HttpStatusCode.OK, removeByName.StatusCode);
        Assert.DoesNotContain((await BodyAsync(removeByName)).GetProperty("cast").EnumerateArray(), c => c.GetProperty("name").GetString() == "Test Actress");

        Assert.Equal(HttpStatusCode.BadRequest, (await http.DeleteAsync($"/api/v1/movies/{code}/cast")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync($"/api/v1/movies/{code}/cast", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/api/v1/movies/{code}/cast", Json("""{"actorId":99999999}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/api/v1/movies/NOPE-999/cast/{actor.Id}")).StatusCode);
    }

    [Fact]
    public async Task Nfo_PreviewThenPush_WithAndWithoutBody()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"javbuddy-e2e-apinfo-{Guid.NewGuid():N}");
        scratchFolders.Add(scratch);
        var code = $"APIN-{Random.Shared.Next(100, 999)}";
        var folder = Directory.CreateDirectory(Path.Combine(scratch, code)).FullName;
        var nfoPath = Path.Combine(folder, $"{code}.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie>\n  <title>Old Title</title>\n</movie>");

        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, metaTitle: "New Title");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            tracked!.NfoDriftKind = NfoDriftKind.JavbuddyChanged;
            tracked.NfoConflictDetails = "[Javbuddy changed] Title: .nfo has 'Old Title', canonical is 'New Title'";
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, scratch);
        using var http = NewClient();

        var preview = await BodyAsync(await http.GetAsync($"/api/v1/movies/{code}/nfo"));
        Assert.Equal("javbuddyChanged", preview.GetProperty("driftKind").GetString());
        Assert.Contains("New Title", preview.GetProperty("proposedNfo").GetString());

        // No body at all: includeExternal defaults to false.
        var push = await http.PostAsync($"/api/v1/movies/{code}/nfo/push", null);

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal(1, (await BodyAsync(push)).GetProperty("written").GetInt32());
        Assert.Equal("New Title", System.Xml.Linq.XDocument.Parse(await File.ReadAllTextAsync(nfoPath)).Root!.Element("title")?.Value);

        // An explicit body is accepted too.
        var again = await http.PostAsync($"/api/v1/movies/{code}/nfo/push", Json("""{"includeExternal":false}"""));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/movies/NOPE-999/nfo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/v1/movies/NOPE-999/nfo/push", null)).StatusCode);
    }

    [Fact]
    public async Task JavinizerFetch_ReturnsScrape_AndUnconfiguredIs503()
    {
        fixture.ResetFakes();
        var code = await SeedAsync("APIJ");
        using var http = NewClient();

        var response = await http.PostAsync($"/api/v1/movies/{code}/javinizer/fetch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal($"Test Movie {code}", body.GetProperty("title").GetString());
        Assert.Equal("Test Studio", body.GetProperty("studio").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/v1/movies/NOPE-999/javinizer/fetch", null)).StatusCode);

        fixture.FakeJavinizer.ScrapeError = "scraper exploded";
        Assert.Equal(HttpStatusCode.BadGateway, (await http.PostAsync($"/api/v1/movies/{code}/javinizer/fetch", null)).StatusCode);
        fixture.ResetFakes();

        // A separate app with no javinizer configured.
        await using var unconfigured = new JavbuddyAppFactory();
        unconfigured.StartServer();
        var factory = unconfigured.Services.GetRequiredService<IDbContextFactory<Javbuddy.Data.AppDbContext>>();
        await DbSeeding.SeedMovieAsync(factory, "APIJ-1");
        using var other = new HttpClient { BaseAddress = new Uri(unconfigured.ServerAddress) };

        var notConfigured = await other.PostAsync("/api/v1/movies/APIJ-1/javinizer/fetch", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, notConfigured.StatusCode);
        Assert.Equal("application/problem+json", notConfigured.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SystemInfo_QbittorrentKeyIsLowerCase()
    {
        using var http = NewClient();

        var integrations = (await BodyAsync(await http.GetAsync("/api/v1/system/info"))).GetProperty("integrations");

        Assert.True(integrations.TryGetProperty("qbittorrent", out _));
        Assert.False(integrations.TryGetProperty("qBittorrent", out _));
    }
}
