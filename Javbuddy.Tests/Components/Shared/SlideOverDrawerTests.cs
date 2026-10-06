using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class SlideOverDrawerTests : BunitContext
{
    [Fact]
    public void ShowFalse_RendersNothing()
    {
        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, false);
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.OnClose, EventCallback.Empty);
        });

        Assert.Empty(cut.FindAll(".slide-over-drawer-overlay"));
    }

    [Fact]
    public void ShowTrue_RendersTitleAndChildContent()
    {
        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.OnClose, EventCallback.Empty);
            p.AddChildContent("<div class=\"drawer-content-marker\">form here</div>");
        });

        Assert.NotEmpty(cut.FindAll(".slide-over-drawer-overlay"));
        Assert.Contains("javinizer-go", cut.Find(".slide-over-drawer-head h2").TextContent);
        Assert.NotEmpty(cut.FindAll(".drawer-content-marker"));
    }

    [Fact]
    public void ClickCloseButton_InvokesOnClose()
    {
        var closed = false;

        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true));
        });

        cut.Find(".slide-over-drawer-close-btn").Click();

        Assert.True(closed);
    }

    [Fact]
    public void ClickOverlay_InvokesOnClose()
    {
        var closed = false;

        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "javinizer-go");
            p.Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true));
        });

        cut.Find(".slide-over-drawer-overlay").Click();

        Assert.True(closed);
    }

    [Fact]
    public void ReRenderWhileShown_PreservesOverlay()
    {
        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "WAPdB");
            p.Add(x => x.OnClose, EventCallback.Empty);
            p.AddChildContent("<input id=\"test-input\" />");
        });

        // Re-render (simulating state update or typing while open)
        cut.Render();

        Assert.NotEmpty(cut.FindAll(".slide-over-drawer-overlay"));
    }

    [Fact]
    public void Wide_AddsWideClass_AndCtrlEnterRaisesCallback_EscapeCloses()
    {
        var ctrlEnter = 0;
        var closed = 0;
        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "T");
            p.Add(x => x.Wide, true);
            p.Add(x => x.OnClose, () => closed++);
            p.Add(x => x.OnCtrlEnter, () => ctrlEnter++);
        });

        Assert.Contains("slide-over-drawer-panel-wide", cut.Find(".slide-over-drawer-panel").ClassList);
        cut.Find(".slide-over-drawer-overlay").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find(".slide-over-drawer-overlay").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".slide-over-drawer-overlay").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, ctrlEnter);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void NotWide_HasNoWideClass()
    {
        var cut = Render<SlideOverDrawer>(p =>
        {
            p.Add(x => x.Show, true);
            p.Add(x => x.Title, "T");
            p.Add(x => x.OnClose, EventCallback.Empty);
        });

        Assert.DoesNotContain("slide-over-drawer-panel-wide", cut.Find(".slide-over-drawer-panel").ClassList);
    }
}
