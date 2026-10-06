using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class BirthdayIndicatorTests : BunitContext
{
    [Fact]
    public void BirthdayToday_ShowsConfetti_WithAgeInLabel()
    {
        var cut = Render<BirthdayIndicator>(p => p
            .Add(x => x.BirthDate, new DateTime(1996, 5, 3))
            .Add(x => x.AsOf, new DateOnly(2026, 5, 3)));

        var indicator = cut.Find(".birthday-indicator");
        Assert.Equal("🎉", indicator.TextContent);
        Assert.Equal("Birthday today (turns 30)", indicator.GetAttribute("title"));
        Assert.Equal("Birthday today (turns 30)", indicator.GetAttribute("aria-label"));
    }

    [Fact]
    public void NotBirthdayToday_RendersNothing()
    {
        var cut = Render<BirthdayIndicator>(p => p
            .Add(x => x.BirthDate, new DateTime(1996, 5, 3))
            .Add(x => x.AsOf, new DateOnly(2026, 5, 4)));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void NoBirthDate_RendersNothing()
    {
        var cut = Render<BirthdayIndicator>(p => p.Add(x => x.AsOf, new DateOnly(2026, 5, 3)));

        Assert.Empty(cut.Markup.Trim());
    }
}
