using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class DropdownMenuButtonTests : BunitContext
{
    [Fact]
    public void RendersLabelAndArrow()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Sort: Added")
            .Add(x => x.Arrow, "▼"));

        Assert.Contains("Sort: Added", cut.Find("button").TextContent);
        Assert.Contains("▼", cut.Find(".sort-dropdown-arrow").TextContent);
    }

    [Fact]
    public void ShowMenuFalse_DoesNotRenderMenu()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Sort")
            .Add(x => x.ShowMenu, false));

        Assert.Empty(cut.FindAll(".sort-dropdown-menu"));
    }

    [Fact]
    public void ShowMenuTrue_RendersChildContentInsideMenu()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Sort")
            .Add(x => x.ShowMenu, true)
            .AddChildContent("<button class=\"sort-dropdown-item\">Title</button>"));

        var menu = cut.Find(".sort-dropdown-menu");
        Assert.Contains("Title", menu.TextContent);
    }

    [Fact]
    public void ActiveTrue_UsesSecondaryButtonClass()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter")
            .Add(x => x.Active, true));

        Assert.Contains("btn-secondary", cut.Find("button").GetAttribute("class"));
    }

    [Fact]
    public void MenuMinWidth_SetsInlineStyleOnMenu()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter")
            .Add(x => x.ShowMenu, true)
            .Add(x => x.MenuMinWidth, "280px"));

        Assert.Equal("min-width:280px", cut.Find(".sort-dropdown-menu").GetAttribute("style"));
    }

    [Fact]
    public void ClickingButton_InvokesOnToggle()
    {
        var toggled = false;
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Sort")
            .Add(x => x.OnToggle, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => toggled = true)));

        cut.Find("button").Click();

        Assert.True(toggled);
    }

    [Fact]
    public void ShowClearFalse_RendersSingleButton()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter")
            .Add(x => x.ShowClear, false));

        Assert.Single(cut.FindAll("button"));
        Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
    }

    [Fact]
    public void ShowClearTrue_RendersButtonGroupWithClearAndArrow()
    {
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter (2)")
            .Add(x => x.ShowClear, true)
            .Add(x => x.ClearTitle, "Clear all filters")
            .Add(x => x.ClearAriaLabel, "Clear all filters"));

        var buttons = cut.FindAll("button");
        Assert.Equal(3, buttons.Count);

        var mainBtn = cut.Find(".dropdown-main-btn");
        Assert.Equal("Filter (2)", mainBtn.TextContent.Trim());

        var clearBtn = cut.Find(".dropdown-clear-btn");
        Assert.Equal("×", clearBtn.TextContent.Trim());
        Assert.Equal("Clear all filters", clearBtn.GetAttribute("title"));
        Assert.Equal("Clear all filters", clearBtn.GetAttribute("aria-label"));

        var arrowBtn = cut.Find(".dropdown-arrow-btn");
        Assert.Contains("▼", arrowBtn.TextContent);
    }

    [Fact]
    public void ShowClearTrue_ClickingClear_InvokesOnClearWithoutOnToggle()
    {
        var cleared = false;
        var toggled = false;
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter (1)")
            .Add(x => x.ShowClear, true)
            .Add(x => x.OnClear, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => cleared = true))
            .Add(x => x.OnToggle, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => toggled = true)));

        cut.Find(".dropdown-clear-btn").Click();

        Assert.True(cleared);
        Assert.False(toggled);
    }

    [Fact]
    public void ShowClearTrue_ClickingMainOrArrow_InvokesOnToggleWithoutOnClear()
    {
        var cleared = false;
        var toggleCount = 0;
        var cut = Render<DropdownMenuButton>(p => p
            .Add(x => x.Label, "Filter (1)")
            .Add(x => x.ShowClear, true)
            .Add(x => x.OnClear, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => cleared = true))
            .Add(x => x.OnToggle, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => toggleCount++)));

        cut.Find(".dropdown-main-btn").Click();
        Assert.Equal(1, toggleCount);
        Assert.False(cleared);

        cut.Find(".dropdown-arrow-btn").Click();
        Assert.Equal(2, toggleCount);
        Assert.False(cleared);
    }
}
