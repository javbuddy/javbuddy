using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class LoadingIndicatorTests : BunitContext
{
    [Fact]
    public void RendersLoadingParagraph()
    {
        var cut = Render<LoadingIndicator>();

        cut.MarkupMatches("<p><em>Loading…</em></p>");
    }
}
