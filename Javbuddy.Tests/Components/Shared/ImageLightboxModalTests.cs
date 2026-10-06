using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ImageLightboxModalTests : BunitContext
{
    public ImageLightboxModalTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void WhenShowFalse_RendersNothing()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345"));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void WhenShowTrue_TitleHasFullTitleTooltip()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "A very long descriptive movie title")
            .Add(x => x.ImageUrl, "/poster.jpg"));

        Assert.Equal("A very long descriptive movie title", cut.Find(".image-lightbox-name").GetAttribute("title"));
    }

    [Fact]
    public void WhenShowTrue_NoAlternate_RendersImageWithoutToggle()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345"));

        Assert.Contains("Yua Mikami", cut.Find(".image-lightbox-name").TextContent);
        var img = cut.Find(".image-lightbox-image");
        Assert.Equal("/actor-image/42/full?v=12345", img.GetAttribute("src"));
        Assert.Empty(cut.FindAll("[aria-label='Image variant']"));
        Assert.Empty(cut.FindAll(".image-lightbox-badge"));
    }

    [Fact]
    public void WhenShowTrue_AlternateAvailable_DefaultsToAlternateImageAndRendersToggle()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.AlternateImageUrl, "/actor-image/42/source?v=12345"));

        Assert.Contains("Yua Mikami", cut.Find(".image-lightbox-name").TextContent);
        Assert.Equal("Original photo", cut.Find(".image-lightbox-badge").TextContent);
        var img = cut.Find(".image-lightbox-image");
        Assert.Equal("/actor-image/42/source?v=12345", img.GetAttribute("src"));

        var buttons = cut.FindAll("[aria-label='Image variant'] button");
        Assert.Equal(2, buttons.Count);
        Assert.Contains("btn-light", buttons[0].ClassList);
        Assert.Contains("btn-outline-light", buttons[1].ClassList);
    }

    [Fact]
    public void WhenShowTrue_AlternateAvailable_DefaultToAlternateFalse_DefaultsToPrimaryImage()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.AlternateImageUrl, "/actor-image/42/source?v=12345")
            .Add(x => x.DefaultToAlternate, false));

        Assert.Contains("Yua Mikami", cut.Find(".image-lightbox-name").TextContent);
        Assert.Equal("Cropped portrait", cut.Find(".image-lightbox-badge").TextContent);
        var img = cut.Find(".image-lightbox-image");
        Assert.Equal("/actor-image/42/full?v=12345", img.GetAttribute("src"));

        var buttons = cut.FindAll("[aria-label='Image variant'] button");
        Assert.Equal(2, buttons.Count);
        Assert.Contains("btn-outline-light", buttons[0].ClassList);
        Assert.Contains("btn-light", buttons[1].ClassList);
    }

    [Fact]
    public void TogglingVariant_ChangesImageSrcAndBadge()
    {
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.AlternateImageUrl, "/actor-image/42/source?v=12345"));

        var buttons = cut.FindAll("[aria-label='Image variant'] button");
        var croppedBtn = buttons[1];
        croppedBtn.Click();

        var img = cut.Find(".image-lightbox-image");
        Assert.Equal("/actor-image/42/full?v=12345", img.GetAttribute("src"));
        Assert.Equal("Cropped portrait", cut.Find(".image-lightbox-badge").TextContent);

        // Click Original button again
        buttons = cut.FindAll("[aria-label='Image variant'] button");
        var originalBtn = buttons[0];
        originalBtn.Click();

        img = cut.Find(".image-lightbox-image");
        Assert.Equal("/actor-image/42/source?v=12345", img.GetAttribute("src"));
        Assert.Equal("Original photo", cut.Find(".image-lightbox-badge").TextContent);
    }

    [Fact]
    public void ClickingCloseButton_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.OnClose, () => closed = true));

        cut.Find(".image-lightbox-close-btn").Click();
        Assert.True(closed);
    }

    [Fact]
    public void ClickingBackdrop_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.OnClose, () => closed = true));

        cut.Find(".image-lightbox-backdrop").Click();
        Assert.True(closed);
    }

    [Fact]
    public async Task PressingEscape_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.OnClose, () => closed = true));

        await cut.InvokeAsync(() => cut.Instance.HandleEscape());
        Assert.True(closed);
    }

    [Fact]
    public async Task PressingEscape_WhileClosed_DoesNotInvokeOnClose()
    {
        var closed = false;
        var cut = Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345")
            .Add(x => x.OnClose, () => closed = true));

        await cut.InvokeAsync(() => cut.Instance.HandleEscape());
        Assert.False(closed);
    }

    // A lightbox disposed while its first render's module import is still pending (the Review page
    // moving on right after it went interactive) used to call init with its already-disposed
    // DotNetObjectReference, an ObjectDisposedException that killed the whole circuit, and leaked
    // the module. bUnit's SetupModule() always resolves an import synchronously, so IJSRuntime is
    // replaced outright to control when it completes.
    [Fact]
    public async Task DisposedWhileModuleImportPending_SkipsInitAndDisposesTheModule()
    {
        var importTcs = new TaskCompletionSource<IJSObjectReference>();
        var module = Substitute.For<IJSObjectReference>();
        var jsRuntime = Substitute.For<IJSRuntime>();
#pragma warning disable BL0016 // NSubstitute mock setup, not a real unguarded JS interop call
        // Child components (the zoom controls) import their own modules through the same runtime.
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Substitute.For<IJSObjectReference>()));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Is<object?[]>(a => a.Length > 0 && Equals(a[0], "./Components/Shared/ImageLightboxModal.razor.js")))
            .Returns(new ValueTask<IJSObjectReference>(importTcs.Task));
#pragma warning restore BL0016
        Services.AddSingleton(jsRuntime);
        Render<ImageLightboxModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Yua Mikami")
            .Add(x => x.ImageUrl, "/actor-image/42/full?v=12345"));

        await DisposeComponentsAsync();
        importTcs.SetResult(module);

        // The rest of OnAfterRenderAsync runs on the renderer's dispatcher once the import resolves.
        for (var i = 0; i < 100 && !module.ReceivedCalls().Any(); i++) await Task.Delay(10);

        _ = module.Received(1).DisposeAsync();
#pragma warning disable BL0016 // NSubstitute verification, not a real JS interop call
        _ = module.DidNotReceive().InvokeAsync<IJSVoidResult>("init", Arg.Any<object?[]?>());
#pragma warning restore BL0016
    }
}
