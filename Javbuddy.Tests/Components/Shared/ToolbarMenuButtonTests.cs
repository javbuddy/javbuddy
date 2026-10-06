using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ToolbarMenuButtonTests : BunitContext
{
    private IRenderedComponent<ToolbarMenuButton> RenderMenu(Action? onFirst = null, bool secondDisabled = false) =>
        Render<ToolbarMenuButton>(p => p
            .Add(x => x.Label, "Tools")
            .Add(x => x.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<ToolbarMenuItem>(0);
                b.AddAttribute(1, nameof(ToolbarMenuItem.OnClick), EventCallback.Factory.Create(this, () => onFirst?.Invoke()));
                b.AddAttribute(2, nameof(ToolbarMenuItem.ChildContent), (RenderFragment)(c => c.AddContent(0, "Crop Cover")));
                b.CloseComponent();
                b.OpenComponent<ToolbarMenuItem>(3);
                b.AddAttribute(4, nameof(ToolbarMenuItem.Disabled), secondDisabled);
                b.AddAttribute(5, nameof(ToolbarMenuItem.ChildContent), (RenderFragment)(c => c.AddContent(0, "Repair Video")));
                b.CloseComponent();
            })));

    [Fact]
    public void RendersClosedByDefault()
    {
        var cut = RenderMenu();

        var trigger = cut.Find("button.toolbar-menu-btn");
        Assert.Contains("Tools", trigger.TextContent);
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".toolbar-menu-list"));
    }

    [Fact]
    public void ClickingTrigger_OpensMenuWithItems_AndClickingAgainClosesIt()
    {
        var cut = RenderMenu();

        cut.Find("button.toolbar-menu-btn").Click();

        Assert.Equal("true", cut.Find("button.toolbar-menu-btn").GetAttribute("aria-expanded"));
        Assert.Equal(["Crop Cover", "Repair Video"], cut.FindAll(".toolbar-menu-item").Select(b => b.TextContent.Trim()));

        cut.Find("button.toolbar-menu-btn").Click();

        Assert.Empty(cut.FindAll(".toolbar-menu-list"));
    }

    [Fact]
    public void ClickingItem_RunsItsHandler_AndClosesMenu()
    {
        var clicks = 0;
        var cut = RenderMenu(onFirst: () => clicks++);

        cut.Find("button.toolbar-menu-btn").Click();
        cut.FindAll(".toolbar-menu-item")[0].Click();

        Assert.Equal(1, clicks);
        Assert.Empty(cut.FindAll(".toolbar-menu-list"));
    }

    [Fact]
    public void ClickingBackdrop_ClosesMenu()
    {
        var cut = RenderMenu();

        cut.Find("button.toolbar-menu-btn").Click();
        cut.Find(".toolbar-menu-backdrop").Click();

        Assert.Empty(cut.FindAll(".toolbar-menu-list"));
    }

    [Fact]
    public void Escape_ClosesMenu()
    {
        var cut = RenderMenu();

        cut.Find("button.toolbar-menu-btn").Click();
        cut.Find(".toolbar-menu").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".toolbar-menu-list"));
    }

    [Fact]
    public void DisabledItem_RendersDisabled()
    {
        var cut = RenderMenu(secondDisabled: true);

        cut.Find("button.toolbar-menu-btn").Click();

        var items = cut.FindAll(".toolbar-menu-item");
        Assert.False(items[0].HasAttribute("disabled"));
        Assert.True(items[1].HasAttribute("disabled"));
    }
}
