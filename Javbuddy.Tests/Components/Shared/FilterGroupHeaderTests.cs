using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class FilterGroupHeaderTests : BunitContext
{
    [Fact]
    public void ShowsTheText_WithAnArrowForTheExpandedState()
    {
        var closed = Render<FilterGroupHeader>(p => p.Add(x => x.Text, "Genre (2)"));
        var open = Render<FilterGroupHeader>(p => p.Add(x => x.Text, "Genre").Add(x => x.Expanded, true));

        Assert.Equal("Genre (2)", closed.Find("button.sort-dropdown-item > span").TextContent);
        Assert.Equal("▼", closed.Find(".sort-dropdown-arrow").TextContent);
        Assert.Equal("▲", open.Find(".sort-dropdown-arrow").TextContent);
    }

    [Fact]
    public void IsHighlightedOnlyWhileActive_AndClickingRaisesOnToggle()
    {
        var toggled = 0;
        var cut = Render<FilterGroupHeader>(p => p
            .Add(x => x.Text, "Studio")
            .Add(x => x.OnToggle, EventCallback.Factory.Create(this, () => toggled++)));
        Assert.DoesNotContain("active", cut.Find("button").ClassList);

        cut.Find("button").Click();
        Assert.Equal(1, toggled);

        cut.Render(p => p.Add(x => x.Text, "Studio").Add(x => x.Active, true));
        Assert.Contains("active", cut.Find("button").ClassList);
    }
}
