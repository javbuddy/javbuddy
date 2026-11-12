using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class ScenePreviewMediaTests : BunitContext
{
    [Fact]
    public void WithoutAPreview_ShowsJustTheScreenshot()
    {
        var cut = Render<ScenePreviewMedia>(p => p.Add(x => x.ThumbUrl, "/scene-image/1/thumb?v=1"));

        Assert.Equal("/scene-image/1/thumb?v=1", cut.Find("img").GetAttribute("src"));
        Assert.Empty(cut.FindAll("video"));
    }

    [Fact]
    public void WithAHoverPreview_RendersOnlyAnEmptySlotForTheBrowserToFill()
    {
        var cut = Render<ScenePreviewMedia>(p => p
            .Add(x => x.ThumbUrl, "/scene-image/1/thumb?v=1")
            .Add(x => x.HoverPreviewUrl, "/scene-image/1/preview?v=2&x=\"y\""));

        Assert.Empty(cut.FindAll("video"));
        var slot = cut.Find(".scene-preview-live");
        Assert.Equal("/scene-image/1/preview?v=2&x=\"y\"", slot.GetAttribute("data-preview-src"));
        // Blazor must never render children into it, or a re-render would clear the video the browser put there.
        Assert.Empty(slot.Children);
        Assert.Empty(slot.TextContent);
    }

    [Fact]
    public void WithoutAHoverPreview_HasNoSlot()
    {
        var cut = Render<ScenePreviewMedia>(p => p.Add(x => x.ThumbUrl, "/scene-image/1/thumb?v=1"));

        Assert.Empty(cut.FindAll(".scene-preview-live"));
    }
}
