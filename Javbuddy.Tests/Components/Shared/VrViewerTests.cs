using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class VrViewerTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/VrViewer.razor.js";

    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    [Fact]
    public void VrViewer_RendersCanvasProjectionPickerAndHiddenError()
    {
        SetUpModule();

        var cut = Render<VrViewer>(p => p.Add(x => x.VideoSelector, "video.cleanup-video"));

        Assert.Single(cut.FindAll(".vr-viewer canvas.vr-viewer-canvas"));
        var options = cut.FindAll(".vr-viewer select.vr-viewer-projection option");
        // Equirectangular is the common encoding, so it is first and therefore the default.
        Assert.Equal(["equirect", "fisheye"], options.Select(o => o.GetAttribute("value")));
        // The player's own fullscreen button (the scrub bar's, or the browser's) is the only one.
        Assert.Empty(cut.FindAll(".vr-viewer button"));
        Assert.True(cut.Find(".vr-viewer-error").HasAttribute("hidden"));
    }

    [Fact]
    public void VrViewer_InitialisesModuleWithVideoSelector()
    {
        var module = SetUpModule();

        var cut = Render<VrViewer>(p => p.Add(x => x.VideoSelector, "video.cleanup-video"));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal(2, init.Arguments.Count);
        Assert.Equal("video.cleanup-video", init.Arguments[1]);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".vr-viewer")));
    }

    [Fact]
    public async Task VrViewer_Dispose_TearsDownTheModule()
    {
        var module = SetUpModule();
        var cut = Render<VrViewer>(p => p.Add(x => x.VideoSelector, "video.cleanup-video"));

        await cut.Instance.DisposeAsync();

        Assert.Single(module.Invocations, i => i.Identifier == "dispose");
    }
}
