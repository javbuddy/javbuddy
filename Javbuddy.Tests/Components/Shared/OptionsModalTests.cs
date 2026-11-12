using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Components.Shared;

public class OptionsModalTests : BunitContext
{
    public OptionsModalTests() => JSInterop.AllowModalShell();

    private int closed;

    private IRenderedComponent<OptionsModal> RenderModal() =>
        Render<OptionsModal>(p => p
            .Add(x => x.Title, "Actor Options")
            .Add(x => x.OnClose, () => closed++)
            .AddChildContent("<div class=\"options-row\"><span class=\"options-label\">Card Size</span><input type=\"range\" /></div>"));

    [Fact]
    public void RendersTitleAsTheDialogsLabel_AndTheCallersRows()
    {
        var cut = RenderModal();

        var title = cut.Find(".modal-shell-title");
        Assert.Equal("Actor Options", title.TextContent);
        Assert.Equal(title.Id, cut.Find("[role=dialog]").GetAttribute("aria-labelledby"));
        Assert.Equal("Card Size", cut.Find(".modal-shell-body .options-row .options-label").TextContent);
    }

    [Fact]
    public void CloseButtonsAndBackdrop_Close()
    {
        var cut = RenderModal();

        cut.Find(".modal-shell-close-btn").Click();
        cut.Find(".modal-shell-footer button").Click();
        cut.Find(".modal-shell-backdrop").Click();
        Assert.Equal(3, closed);
    }
}
