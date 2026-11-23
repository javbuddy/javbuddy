using System.Net;
using System.Net.Http.Headers;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The local video stream endpoint over a real Kestrel socket: browsers seek
/// with Range requests, so it has to answer them with 206 Partial Content, not the whole file.</summary>
[Collection(E2ECollection.Name)]
public class MovieStreamEndpointTests
{
    private readonly E2EFixture fixture;

    public MovieStreamEndpointTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task RangeRequest_GetsPartialContent_WithoutJellyfin()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STREAM-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var length = new FileInfo(Path.Combine(DbSeeding.PlayableLibraryRoot, "E2E-STREAM-1", "E2E-STREAM-1.webm")).Length;
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/movies/{movie.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(100, 199);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("video/webm", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new ContentRangeHeaderValue(100, 199, length), response.Content.Headers.ContentRange);
        Assert.Equal(100, (await response.Content.ReadAsByteArrayAsync()).Length);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
    }

    [Fact]
    public async Task MovieWithoutALocalFile_IsNotFound()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STREAM-NOFILE", MovieStatus.Got);
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };

        using var response = await client.GetAsync($"/api/movies/{movie.Id}/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Response_MustRevalidate_AndCarriesAStrongETag()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STREAM-2", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };

        using var response = await client.GetAsync($"/api/movies/{movie.Id}/stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
    }

    [Fact]
    public async Task MatchingIfNoneMatch_GetsNotModified()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STREAM-3", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var first = await client.GetAsync($"/api/movies/{movie.Id}/stream", HttpCompletionOption.ResponseHeadersRead);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/movies/{movie.Id}/stream");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task RewrittenFile_ChangesTheETag_AndAStaleIfRangeGetsTheWholeFile()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STREAM-4", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var path = Path.Combine(DbSeeding.PlayableLibraryRoot, "E2E-STREAM-4", "E2E-STREAM-4.webm");
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var before = await client.GetAsync($"/api/movies/{movie.Id}/stream", HttpCompletionOption.ResponseHeadersRead);

        // What a repair does: a new file replaces the old one under the same path.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/movies/{movie.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(0, 9);
        request.Headers.IfRange = new RangeConditionHeaderValue(before.Headers.ETag!);
        using var after = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.NotEqual(before.Headers.ETag, after.Headers.ETag);
    }
}
