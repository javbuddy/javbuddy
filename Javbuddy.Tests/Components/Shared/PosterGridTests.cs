using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PosterGridTests : BunitContext
{
    [Fact]
    public void WrapsChildContentInPosterGridDiv()
    {
        var cut = Render<PosterGrid>(p => p.AddChildContent("<span>card</span>"));

        var div = cut.Find("div.poster-grid");
        Assert.Contains("card", div.TextContent);
    }
}
