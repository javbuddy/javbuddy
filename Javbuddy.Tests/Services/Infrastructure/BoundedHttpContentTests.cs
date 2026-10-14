using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Infrastructure;

public class BoundedHttpContentTests
{
    [Fact]
    public async Task ReadBytesAsync_ReturnsABodyWithinTheLimit()
    {
        var bytes = await BoundedHttpContent.ReadBytesAsync(new ByteArrayContent(new byte[100]), 100, CancellationToken.None);

        Assert.Equal(100, bytes.Length);
    }

    [Fact]
    public async Task ReadBytesAsync_ThrowsOnADeclaredLengthOverTheLimit()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            BoundedHttpContent.ReadBytesAsync(new ByteArrayContent(new byte[101]), 100, CancellationToken.None));
    }

    [Fact]
    public async Task ReadBytesAsync_ThrowsOnAStreamedBodyOverTheLimit()
    {
        var content = new StreamContent(new ControlledStream(new byte[101]));
        Assert.Null(content.Headers.ContentLength);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            BoundedHttpContent.ReadBytesAsync(content, 100, CancellationToken.None));
    }

    [Fact]
    public async Task ReadStreamAsync_ReturnsNullPastTheLimit_AndEmptyForAnEmptyStream()
    {
        Assert.Null(await BoundedHttpContent.ReadStreamAsync(new MemoryStream(new byte[11]), 10, CancellationToken.None));
        Assert.Empty((await BoundedHttpContent.ReadStreamAsync(new MemoryStream(), 10, CancellationToken.None))!);
    }
}
