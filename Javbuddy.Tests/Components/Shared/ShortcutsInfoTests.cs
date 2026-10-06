using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class ShortcutsInfoTests : BunitContext
{
    [Fact]
    public void ListsEachShortcutsKeysAndAction_InAPopoverTheButtonDescribes()
    {
        var cut = Render<ShortcutsInfo>(p => p.Add(x => x.Shortcuts, [KeyboardShortcut.Seek, KeyboardShortcut.EndScene]));

        var button = cut.Find("button.shortcuts-info-btn");
        var popover = cut.Find(".shortcuts-info-popover");
        Assert.Equal("Keyboard shortcuts", button.GetAttribute("aria-label"));
        Assert.Equal(popover.Id, button.GetAttribute("aria-describedby"));
        Assert.Equal("tooltip", popover.GetAttribute("role"));

        var keys = cut.FindAll(".shortcuts-info-keys");
        Assert.Equal(["←", "→"], keys[0].QuerySelectorAll("kbd").Select(k => k.TextContent));
        Assert.Single(keys[0].QuerySelectorAll(".shortcuts-info-or"));
        Assert.Equal(["Shift+M"], keys[1].QuerySelectorAll("kbd").Select(k => k.TextContent));
        Assert.Empty(keys[1].QuerySelectorAll(".shortcuts-info-or"));
        Assert.Equal([KeyboardShortcut.Seek.Action, KeyboardShortcut.EndScene.Action], cut.FindAll(".shortcuts-info-action").Select(a => a.TextContent));
    }

    [Theory]
    [InlineData(false, false, "shortcuts-info")]
    [InlineData(true, true, "shortcuts-info shortcuts-info-above shortcuts-info-start")]
    public void Placement_AddsModifierClasses(bool above, bool alignStart, string expected)
    {
        var cut = Render<ShortcutsInfo>(p => p
            .Add(x => x.Shortcuts, [KeyboardShortcut.PlayPause])
            .Add(x => x.Above, above)
            .Add(x => x.AlignStart, alignStart));

        Assert.Equal(expected, string.Join(' ', cut.Find(".shortcuts-info").ClassList));
    }
}
