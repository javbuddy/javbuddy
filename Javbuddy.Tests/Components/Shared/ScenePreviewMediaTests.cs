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
    public void WithAPreview_PlaysItMutedAndLooping_OverTheScreenshot()
    {
        var cut = Render<ScenePreviewMedia>(p => p
            .Add(x => x.ThumbUrl, "/scene-image/1/thumb?v=1")
            .Add(x => x.PreviewUrl, "/scene-image/1/preview?v=2&x=\"y\""));

        Assert.Equal("/scene-image/1/thumb?v=1", cut.Find("img").GetAttribute("src"));
        var video = cut.Find("video");
        Assert.Equal("/scene-image/1/preview?v=2&x=\"y\"", video.GetAttribute("src"));
        Assert.Equal("/scene-image/1/thumb?v=1", video.GetAttribute("poster"));
        // Chrome only autoplays a muted video; the attributes come from parsed markup
        // (see the component), so escaping has to hold up too — the quote above stays inside src.
        Assert.All(["autoplay", "muted", "loop", "playsinline"], attribute => Assert.True(video.HasAttribute(attribute), attribute));
    }

    [Fact]
    public void WithoutAScreenshot_PlaysThePreviewAlone()
    {
        // An apex has only a preview.
        var cut = Render<ScenePreviewMedia>(p => p.Add(x => x.PreviewUrl, "/apex-image/1/preview?v=2"));

        Assert.Empty(cut.FindAll("img"));
        var video = cut.Find("video");
        Assert.Equal("/apex-image/1/preview?v=2", video.GetAttribute("src"));
        Assert.False(video.HasAttribute("poster"));
    }
}
