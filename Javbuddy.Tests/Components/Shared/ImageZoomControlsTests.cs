using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ImageZoomControlsTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/ImageZoomControls.razor.js";

    private readonly BunitJSModuleInterop module;

    public ImageZoomControlsTests()
    {
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AttachesTheModule_WithZoomOutAndResetDisabledAtFitToScreen()
    {
        var cut = Render<ImageZoomControls>();

        Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.True(cut.Find(".image-zoom-out").HasAttribute("disabled"));
        Assert.True(cut.Find(".image-zoom-reset").HasAttribute("disabled"));
        Assert.False(cut.Find(".image-zoom-in").HasAttribute("disabled"));
    }

    [Fact]
    public void Buttons_ZoomAndResetThroughTheModule()
    {
        var cut = Render<ImageZoomControls>();
        cut.InvokeAsync(() => cut.Instance.SetZoomed(true));

        cut.Find(".image-zoom-in").Click();
        cut.Find(".image-zoom-out").Click();
        cut.Find(".image-zoom-reset").Click();

        var zooms = module.Invocations.Where(i => i.Identifier == "zoomBy").Select(i => (double)i.Arguments[1]!).ToList();
        Assert.Equal([1.5, 1 / 1.5], zooms);
        Assert.Single(module.Invocations, i => i.Identifier == "reset");
    }

    [Fact]
    public void SetZoomed_RaisesZoomedChanged_AndEnablesZoomOutAndReset()
    {
        var reported = new List<bool>();
        var cut = Render<ImageZoomControls>(p => p.Add(x => x.ZoomedChanged, reported.Add));

        cut.InvokeAsync(() => cut.Instance.SetZoomed(true));

        Assert.Equal([true], reported);
        Assert.False(cut.Find(".image-zoom-out").HasAttribute("disabled"));
        Assert.False(cut.Find(".image-zoom-reset").HasAttribute("disabled"));

        cut.InvokeAsync(() => cut.Instance.SetZoomed(false));

        Assert.Equal([true, false], reported);
        Assert.True(cut.Find(".image-zoom-reset").HasAttribute("disabled"));
    }

    [Fact]
    public void ChangingResetKey_ResetsZoom_ButReRenderingWithTheSameKeyDoesNot()
    {
        var cut = Render<ImageZoomControls>(p => p.Add(x => x.ResetKey, 0));

        cut.Render(p => p.Add(x => x.ResetKey, 0));
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "reset");

        cut.Render(p => p.Add(x => x.ResetKey, 1));
        Assert.Single(module.Invocations, i => i.Identifier == "reset");
    }

    [Fact]
    public async Task DisposedWhileTheModuleIsStillImporting_NeverInitialisesIt()
    {
        // bUnit's module setup always completes the import straight away, so hold it open with a
        // hand-rolled IJSRuntime.
        await using var ctx = new BunitContext();
        var import = new TaskCompletionSource<IJSObjectReference>();
        var js = Substitute.For<IJSRuntime>();
#pragma warning disable BL0016 // NSubstitute mock setup, not a real unguarded JS interop call
        js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(new ValueTask<IJSObjectReference>(import.Task));
#pragma warning restore BL0016
        ctx.Services.AddSingleton(js);
        var cut = ctx.Render<ImageZoomControls>();

        await cut.Instance.DisposeAsync();
        var module = Substitute.For<IJSObjectReference>();
        await cut.InvokeAsync(() => import.SetResult(module));

        await module.Received(1).DisposeAsync();
        Assert.DoesNotContain(module.ReceivedCalls(), c => c.GetArguments().FirstOrDefault() as string == "init");
    }
}
