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
}
