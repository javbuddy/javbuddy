using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class BackButtonTests : BunitContext
{
    [Fact]
    public void RendersHrefCssClassAndChildContent()
    {
        var cut = Render<BackButton>(p => p
            .Add(x => x.Href, "/movies")
            .Add(x => x.CssClass, "movie-toolbar-btn")
            .AddChildContent("All movies"));

        var anchor = cut.Find("a");
        Assert.Equal("/movies", anchor.GetAttribute("href"));
        Assert.Equal("movie-toolbar-btn", anchor.GetAttribute("class"));
        Assert.Contains("All movies", anchor.TextContent);
        Assert.NotNull(cut.Find("svg"));
    }
}
