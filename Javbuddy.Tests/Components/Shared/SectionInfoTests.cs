using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class SectionInfoTests : BunitContext
{
    [Fact]
    public void ShowsItsContent_InAPopoverTheLabelledButtonDescribes()
    {
        var cut = Render<SectionInfo>(p => p
            .Add(x => x.Label, "What are highlights?")
            .AddChildContent("Favorite clips."));

        var button = cut.Find("button.section-info-btn");
        var popover = cut.Find(".section-info-popover");
        Assert.Equal("What are highlights?", button.GetAttribute("aria-label"));
        Assert.Equal(popover.Id, button.GetAttribute("aria-describedby"));
        Assert.Equal("tooltip", popover.GetAttribute("role"));
        Assert.Equal("Favorite clips.", popover.TextContent.Trim());
    }
}
