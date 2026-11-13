using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class CropWorkspaceTests : BunitContext
{
    [Fact]
    public void PutsTheViewportBesideTheFramedPreviews_WithTheHostsSizingOnTheRoot()
    {
        var cut = Render<CropWorkspace>(p => p
            .Add(x => x.Style, "--crop-previews-width: 140px")
            .Add(x => x.PreviewsTitle, "Poster preview")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"vp\">viewport</div>")))
            .Add(x => x.Previews, (RenderFragment)(b => b.AddMarkupContent(0, "<canvas class=\"pv\"></canvas>"))));

        var root = cut.Find("div.cropper-workspace");
        Assert.Equal("--crop-previews-width: 140px", root.GetAttribute("style"));
        Assert.Equal("viewport", cut.Find(".cropper-viewport-wrapper > .vp").TextContent);
        Assert.NotNull(cut.Find(".cropper-previews > canvas.pv"));
        Assert.Equal("Poster preview", cut.Find(".cropper-previews").GetAttribute("title"));
    }

    [Fact]
    public void BusyOverlay_ShowsOnlyWhileALabelIsSet_AndIsAnnounced()
    {
        var idle = Render<CropWorkspace>();
        Assert.Empty(idle.FindAll(".cropper-busy"));

        var busy = Render<CropWorkspace>(p => p.Add(x => x.BusyLabel, "Rotating image"));
        Assert.NotNull(busy.Find(".cropper-viewport-wrapper > .cropper-busy[role='status'] .spinner-border"));
        Assert.Equal("Rotating image", busy.Find(".cropper-busy .visually-hidden").TextContent);
    }
}
