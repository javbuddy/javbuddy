using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class ClipApexRowTests : BunitContext
{
    private string SeekTitle(ApexWindow window)
    {
        var apex = new ApexItem(1, 1, 100, []) { Window = window };
        var cut = Render<ClipApexRow>(p => p.Add(x => x.Node, new ApexNode(apex, EffectiveActors.None)));
        return cut.Find(".apex-editor-row").GetAttribute("title")!;
    }

    [Fact]
    public void SeekTitle_NamesTheDefaultLeadIn() =>
        Assert.Equal("Jump to 5 s before this apex", SeekTitle(ApexWindow.Default));

    [Fact]
    public void SeekTitle_NamesTheApexsOwnLeadIn() =>
        Assert.Equal("Jump to 12.5 s before this apex", SeekTitle(new ApexWindow(12.5, 3)));

    [Fact]
    public void SeekTitle_WithNoLeadIn_JumpsToTheApex() =>
        Assert.Equal("Jump to this apex", SeekTitle(new ApexWindow(0, 3)));
}
