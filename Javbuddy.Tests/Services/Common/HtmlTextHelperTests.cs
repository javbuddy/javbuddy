using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class HtmlTextHelperTests
{
    [Theory]
    [InlineData("<b>Hello</b>  &amp;\n world", "Hello & world")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    public void CleanText_StripsTagsDecodesAndCollapsesWhitespace(string raw, string expected) =>
        Assert.Equal(expected, HtmlTextHelper.CleanText(raw));

    [Theory]
    [InlineData("https://cdn.example/a.jpg", "https://base.example/", "https://cdn.example/a.jpg")]
    [InlineData("HTTP://cdn.example/a.jpg", "https://base.example/", "HTTP://cdn.example/a.jpg")]
    [InlineData("/img/a.jpg", "https://base.example/", "https://base.example/img/a.jpg")]
    [InlineData("img/a.jpg", "https://base.example", "https://base.example/img/a.jpg")]
    public void ResolveUrl_JoinsRelativeAndKeepsAbsolute(string input, string baseUrl, string expected) =>
        Assert.Equal(expected, HtmlTextHelper.ResolveUrl(input, baseUrl));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ResolveUrl_BlankReturnsNull(string? input) =>
        Assert.Null(HtmlTextHelper.ResolveUrl(input, "https://base.example"));
}
