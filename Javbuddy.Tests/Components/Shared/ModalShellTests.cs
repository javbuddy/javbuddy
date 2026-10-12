using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Javbuddy.Tests.Components.Shared;

public class ModalShellTests : BunitContext
{
    private IRenderedComponent<ModalShell> RenderShell(
        Action? onClose = null,
        bool busy = false,
        bool withFooter = true,
        string? maxWidth = null) =>
        Render<ModalShell>(p =>
        {
            p.Add(x => x.Title, "Edit things")
                .Add(x => x.Busy, busy)
                .Add(x => x.OnClose, () => onClose?.Invoke())
                .AddChildContent("<p id=\"body\">Body text</p>");
            if (withFooter) p.Add(x => x.FooterContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button id=\"ok\">OK</button>")));
            if (maxWidth is not null) p.Add(x => x.MaxWidth, maxWidth);
        });

    [Fact]
    public void RendersTitleBodyAndFooter()
    {
        var cut = RenderShell();

        Assert.Equal("Edit things", cut.Find(".modal-shell-title").TextContent);
        Assert.NotNull(cut.Find(".modal-shell-body #body"));
        Assert.NotNull(cut.Find(".modal-shell-footer #ok"));
    }

    [Fact]
    public void TitleContent_TakesPrecedenceOverTitle()
    {
        var cut = Render<ModalShell>(p => p
            .Add(x => x.Title, "plain")
            .Add(x => x.TitleContent, (RenderFragment)(b => b.AddMarkupContent(0, "<em>rich</em>"))));

        Assert.Equal("rich", cut.Find(".modal-shell-title em").TextContent);
    }

    [Fact]
    public void NoFooterContent_DrawsNoFooter()
    {
        var cut = RenderShell(withFooter: false);

        Assert.Empty(cut.FindAll(".modal-shell-footer"));
    }

    [Fact]
    public void MaxWidth_IsAppliedToThePanel()
    {
        var cut = RenderShell(maxWidth: "680px");

        Assert.Contains("max-width: 680px", cut.Find(".modal-shell-panel").GetAttribute("style"));
    }

    [Fact]
    public void CloseButton_BackdropClickAndEscape_RaiseOnClose()
    {
        var closed = 0;
        var cut = RenderShell(onClose: () => closed++);

        cut.Find(".modal-shell-close-btn").Click();
        cut.Find(".modal-shell-backdrop").Click();
        cut.Find(".modal-shell-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(3, closed);
    }

    [Fact]
    public void NonEscapeKey_DoesNotClose()
    {
        var closed = 0;
        var cut = RenderShell(onClose: () => closed++);

        cut.Find(".modal-shell-backdrop").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(0, closed);
    }

    [Fact]
    public void Busy_BlocksEveryWayOfClosing()
    {
        var closed = 0;
        var cut = RenderShell(onClose: () => closed++, busy: true);

        Assert.True(cut.Find(".modal-shell-close-btn").HasAttribute("disabled"));
        cut.Find(".modal-shell-backdrop").Click();
        cut.Find(".modal-shell-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(0, closed);
    }
}
