using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Response compression over a real Kestrel socket: the prerendered HTML
/// document is compressed, while video streaming keeps answering Range requests byte-for-byte.</summary>
[Collection(E2ECollection.Name)]
public class ResponseCompressionTests
{
    private readonly E2EFixture fixture;

    public ResponseCompressionTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task HtmlDocument_IsBrotliCompressed()
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/settings");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
        await using var brotli = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var reader = new StreamReader(brotli);
        Assert.StartsWith("<!DOCTYPE html>", await reader.ReadToEndAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VideoRangeRequest_IsNotCompressed()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-COMPRESS-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/movies/{movie.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(100, 199);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(100, (await response.Content.ReadAsByteArrayAsync()).Length);
    }
}
