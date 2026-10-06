using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PosterGridSkeletonTests : BunitContext
{
    [Fact]
    public void RendersTwentyFourShimmeringCards_HiddenFromAssistiveTech()
    {
        var cut = Render<PosterGridSkeleton>();

        Assert.Equal("true", cut.Find(".skeleton-wrapper").GetAttribute("aria-hidden"));
        var cards = cut.FindAll(".skeleton-grid > .skeleton-card");
        Assert.Equal(24, cards.Count);
        Assert.All(cards, card => Assert.Equal(3, card.QuerySelectorAll(".skeleton-shimmer").Length));
    }
}
