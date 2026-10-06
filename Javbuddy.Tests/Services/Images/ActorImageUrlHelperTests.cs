using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class ActorImageUrlHelperTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not a url", false)]
    [InlineData("https://pics.r18.com/mono/actjpgs/test.jpg", true)]
    [InlineData("http://pics.r18.com/mono/actjpgs/test.jpg", true)]
    [InlineData("https://r18.com/actress/test.jpg", true)]
    [InlineData("https://www.r18.com/actress/test.jpg", true)]
    [InlineData("https://pics.dmm.co.jp/mono/actjpgs/test.jpg", false)]
    [InlineData("https://example.com/test.jpg", false)]
    public void IsDefunctUrl_IdentifiesDefunctDomainsCorrectly(string? url, bool expected)
    {
        var result = ActorImageUrlHelper.IsDefunctUrl(url);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-a-url", false)]
    [InlineData("/path/to/local/file.jpg", false)]
    [InlineData("file:///path/to/file.jpg", false)]
    [InlineData("ftp://example.com/pic.jpg", false)]
    [InlineData("https://pics.r18.com/mono/actjpgs/test.jpg", false)]
    [InlineData("http://pics.r18.com/mono/actjpgs/test.jpg", false)]
    [InlineData("https://r18.com/actress/test.jpg", false)]
    [InlineData("https://www.r18.com/actress/test.jpg", false)]
    [InlineData("https://pics.dmm.co.jp/mono/actjpgs/test.jpg", true)]
    [InlineData("http://pics.dmm.co.jp/mono/actjpgs/test.jpg", true)]
    [InlineData("https://images.example.com/actress.jpg", true)]
    public void IsUsableRemoteActorImageUrl_ValidatesRemoteUrlsCorrectly(string? url, bool expected)
    {
        var result = ActorImageUrlHelper.IsUsableRemoteActorImageUrl(url);
        Assert.Equal(expected, result);
    }
}
