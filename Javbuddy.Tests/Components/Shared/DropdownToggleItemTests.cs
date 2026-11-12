using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class DropdownToggleItemTests : BunitContext
{
    [Fact]
    public void ActiveRow_IsHighlightedWithACheckmark_AndAnInactiveOneIsNot()
    {
        var on = Render<DropdownToggleItem>(p => p.Add(x => x.Label, "Favorites only").Add(x => x.Active, true));
        var off = Render<DropdownToggleItem>(p => p.Add(x => x.Label, "Favorites only"));

        Assert.Contains("active", on.Find("button.sort-dropdown-item").ClassList);
        Assert.Equal("✓", on.Find("span.ms-2").TextContent);
        Assert.DoesNotContain("active", off.Find("button.sort-dropdown-item").ClassList);
        Assert.Empty(off.FindAll("span.ms-2"));
        Assert.Equal("Favorites only", off.Find("button > span").TextContent);
    }

    [Fact]
    public void AriaPressedAndTitle_AreRenderedOnlyWhenGiven()
    {
        var plain = Render<DropdownToggleItem>(p => p.Add(x => x.Label, "Newest"));
        Assert.Null(plain.Find("button").GetAttribute("aria-pressed"));
        Assert.Null(plain.Find("button").GetAttribute("title"));

        var pressed = Render<DropdownToggleItem>(p => p.Add(x => x.Label, "X").Add(x => x.Pressed, false).Add(x => x.Title, "Hint"));
        Assert.Equal("false", pressed.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("Hint", pressed.Find("button").GetAttribute("title"));

        var on = Render<DropdownToggleItem>(p => p.Add(x => x.Label, "X").Add(x => x.Pressed, true));
        Assert.Equal("true", on.Find("button").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Click_RaisesOnClick()
    {
        var clicks = 0;
        var cut = Render<DropdownToggleItem>(p => p
            .Add(x => x.Label, "X")
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));

        cut.Find("button").Click();

        Assert.Equal(1, clicks);
    }
}
