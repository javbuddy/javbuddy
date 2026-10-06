using System.Net;
using Javbuddy.Services.Images;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Images;

public class RemoteImageDownloaderTests
{
    private static readonly Uri ImageUri = new("https://example.com/photo.jpg");

    private static HttpClient ClientReturning(HttpContent content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = content })));

    private static ByteArrayContent Bytes(int length, string? mediaType = "image/jpeg")
    {
        var content = new ByteArrayContent(new byte[length]);
        if (mediaType is not null) content.Headers.ContentType = new(mediaType);
        return content;
    }

    [Theory]
    [InlineData(null, RemoteImageDownloadFailure.MissingUrl)]
    [InlineData("   ", RemoteImageDownloadFailure.MissingUrl)]
    [InlineData("https://pics.r18.com/mono/actjpgs/test.jpg", RemoteImageDownloadFailure.DefunctHost)]
    [InlineData("ftp://example.com/photo.jpg", RemoteImageDownloadFailure.InvalidUrl)]
    [InlineData("not-a-url", RemoteImageDownloadFailure.InvalidUrl)]
    [InlineData(" https://example.com/photo.jpg ", RemoteImageDownloadFailure.None)]
    public void TryParseUrl_ClassifiesUrls(string? url, RemoteImageDownloadFailure expected)
    {
        var failure = RemoteImageDownloader.TryParseUrl(url, out var uri);

        Assert.Equal(expected, failure);
        Assert.Equal(expected == RemoteImageDownloadFailure.None, uri is not null);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsBytesAndLowercasedMediaType()
    {
        var content = Bytes(10, "IMAGE/PNG");

        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 1024, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(10, result.Bytes!.Length);
        Assert.Equal("image/png", result.MediaType);
    }

    [Fact]
    public async Task DownloadAsync_MissingContentLength_StillDownloadsWithinLimit()
    {
        var content = new StreamContent(new ControlledStream(new byte[500]));
        Assert.Null(content.Headers.ContentLength);

        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 1024, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(500, result.Bytes!.Length);
    }

    [Fact]
    public async Task DownloadAsync_DeclaredLengthOverLimit_FailsBeforeReadingBody()
    {
        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(Bytes(2048)), ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.DeclaredTooLarge, result.Failure);
        Assert.Equal(2048, result.DeclaredLength);
    }

    [Fact]
    public async Task DownloadAsync_ChunkedBodyOverLimit_FailsWhileStreaming()
    {
        var content = new StreamContent(new ControlledStream(new byte[200_000]));

        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 100_000, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.TooLarge, result.Failure);
    }

    [Fact]
    public async Task DownloadAsync_EmptyBody_Fails()
    {
        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(Bytes(0)), ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.Empty, result.Failure);
    }

    [Fact]
    public async Task DownloadAsync_NonSuccessStatus_ReportsStatusAndReason()
    {
        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(Bytes(1), HttpStatusCode.NotFound), ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.HttpStatus, result.Failure);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task DownloadAsync_RejectedMediaType_FailsBeforeReadingBody()
    {
        var content = new StreamContent(ControlledStream.Blocking());
        content.Headers.ContentType = new("text/html");

        var result = await RemoteImageDownloader.DownloadAsync(
            ClientReturning(content), ImageUri, 1024, CancellationToken.None,
            isAcceptableMediaType: mediaType => mediaType?.StartsWith("image/") == true);

        Assert.Equal(RemoteImageDownloadFailure.UnacceptableMediaType, result.Failure);
        Assert.Equal("text/html", result.MediaType);
    }

    [Fact]
    public async Task DownloadAsync_AppliesConfigureRequest()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "x");

        await RemoteImageDownloader.DownloadAsync(
            new HttpClient(handler), ImageUri, 1024, CancellationToken.None,
            configureRequest: request => request.Headers.UserAgent.ParseAdd("Javbuddy/1.0"));

        Assert.Equal("Javbuddy/1.0", handler.LastRequest!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task DownloadAsync_SendFailure_ReportsTheException()
    {
        var client = new HttpClient(FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused")));

        var result = await RemoteImageDownloader.DownloadAsync(client, ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.SendFailed, result.Failure);
        Assert.IsType<HttpRequestException>(result.Error);
    }

    [Fact]
    public async Task DownloadAsync_MidStreamFailure_ReportsReadFailed()
    {
        var content = new StreamContent(new ControlledStream(new byte[100], ControlledStream.AfterData.Throw));

        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.ReadFailed, result.Failure);
        Assert.IsType<IOException>(result.Error);
    }

    [Fact]
    public async Task DownloadAsync_TimeoutWaitingForResponse_ReportsTimeout()
    {
        var client = new HttpClient(new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await RemoteImageDownloader.DownloadAsync(client, ImageUri, 1024, CancellationToken.None, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(RemoteImageDownloadFailure.Timeout, result.Failure);
    }

    [Fact]
    public async Task DownloadAsync_TimeoutMidStream_ReportsTimeout()
    {
        var content = new StreamContent(new ControlledStream(new byte[100], ControlledStream.AfterData.BlockUntilCancelled));

        var result = await RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 1024, CancellationToken.None, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(RemoteImageDownloadFailure.Timeout, result.Failure);
    }

    [Fact]
    public async Task DownloadAsync_HttpClientsOwnTimeout_ReportsTimeout()
    {
        var client = new HttpClient(FakeHttpMessageHandler.Throwing(new TaskCanceledException("HttpClient.Timeout elapsed")));

        var result = await RemoteImageDownloader.DownloadAsync(client, ImageUri, 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.Timeout, result.Failure);
    }

    [Fact]
    public async Task DownloadAsync_CallerCancelledWaitingForResponse_Propagates()
    {
        var client = new HttpClient(new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoteImageDownloader.DownloadAsync(client, ImageUri, 1024, cts.Token));
    }

    [Fact]
    public async Task DownloadAsync_CallerCancelledMidStream_Propagates()
    {
        var content = new StreamContent(new ControlledStream(new byte[100], ControlledStream.AfterData.BlockUntilCancelled));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoteImageDownloader.DownloadAsync(ClientReturning(content), ImageUri, 1024, cts.Token));
    }
}
