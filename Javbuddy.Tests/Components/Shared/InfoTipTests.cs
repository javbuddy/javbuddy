using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class InfoTipTests : BunitContext
{
    [Fact]
    public void RendersTheTextAsTooltipAndAccessibleName_OnAFocusableGlyph()
    {
        var cut = Render<InfoTip>(p => p.Add(x => x.Text, "Scroll to zoom."));

        var tip = cut.Find("span.info-tip");
        Assert.Equal("i", tip.TextContent);
        Assert.Equal("Scroll to zoom.", tip.GetAttribute("data-tip"));
        Assert.Equal("Scroll to zoom.", tip.GetAttribute("aria-label"));
        Assert.Equal("img", tip.GetAttribute("role"));
        Assert.Equal("0", tip.GetAttribute("tabindex"));
    }
}
