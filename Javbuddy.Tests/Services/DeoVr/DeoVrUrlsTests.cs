using Javbuddy.Services.DeoVr;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrUrlsTests
{
    [Fact]
    public void Stream_EndsInTheEscapedFileName() =>
        Assert.Equal("https://x/deovr/stream/4/9/MIDE-400%20RIFE.mkv", DeoVrUrls.Stream("https://x", 4, 9, "MIDE-400 RIFE.mkv"));

    [Theory]
    [InlineData("http", null, "http://jb.lan:8080/app")]
    [InlineData("http", "https", "https://jb.lan:8080/app")]
    [InlineData("http", "HTTPS, http", "https://jb.lan:8080/app")]
    [InlineData("https", "http", "http://jb.lan:8080/app")]
    [InlineData("http", "", "http://jb.lan:8080/app")]
    [InlineData("http", "javascript", "http://jb.lan:8080/app")]
    public void BaseUrl_UsesTheForwardedProto_WhenItIsHttpOrHttps(string scheme, string? forwardedProto, string expected) =>
        Assert.Equal(expected, DeoVrUrls.BaseUrl(scheme, "jb.lan:8080", "/app", forwardedProto));
}
