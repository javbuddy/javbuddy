using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Images;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorImageUploadModalTests : BunitContext
{
    [Fact]
    public void ActorImageUploadModal_RendersHidden_WhenShowIsFalse()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Empty(cut.FindAll(".crop-modal-backdrop"));
    }

    [Fact]
    public void ActorImageUploadModal_RendersDropzone_WhenShowIsTrue()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.NotNull(cut.Find(".crop-modal-backdrop"));
        Assert.Contains("Change Portrait — Yua Mikami", cut.Markup);
        Assert.NotNull(cut.Find(".upload-dropzone"));
        Assert.NotNull(cut.Find("input[type='file']"));
    }

    [Fact]
    public void ActorImageUploadModal_ShowsRevertButton_WhenActorHasCustomImage()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(42, Arg.Any<CancellationToken>()).Returns(true);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 42)
            .Add(m => m.ActorName, "Karen Kaede"));

        Assert.Contains("Custom Portrait Active", cut.Markup);
        var revertBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Revert to Library Photo"));
        Assert.NotNull(revertBtn);
    }

    [Fact]
    public async Task ActorImageUploadModal_CloseButton_TriggersOnClose()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        Services.AddSingleton(cacheService);

        var closed = false;
        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami")
            .Add(m => m.OnClose, () => closed = true));

        var closeBtn = cut.Find("button.crop-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }

    [Fact]
    public void ActorImageUploadModal_RendersUrlImportSection_WhenShowIsTrue()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.NotNull(cut.Find(".upload-divider"));
        Assert.NotNull(cut.Find(".upload-url-section"));
        Assert.NotNull(cut.Find("input[type='url']"));
        var fetchBtn = cut.Find("button.fetch-btn");
        Assert.NotNull(fetchBtn);
        Assert.True(fetchBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void ActorImageUploadModal_UrlInput_EnablesFetchButton()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        var urlInput = cut.Find("input[type='url']");
        var fetchBtn = cut.Find("button.fetch-btn");
        Assert.True(fetchBtn.HasAttribute("disabled"));

        urlInput.Input("https://example.com/photo.jpg");
        fetchBtn = cut.Find("button.fetch-btn");
        Assert.False(fetchBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ActorImageUploadModal_FetchUrlFailure_DisplaysErrorMessage()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        cacheService.DownloadImageFromUrlAsync("https://example.com/missing.jpg", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(false, ErrorMessage: "Image not found at the specified URL (HTTP 404)."));
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        var urlInput = cut.Find("input[type='url']");
        urlInput.Input("https://example.com/missing.jpg");

        var fetchBtn = cut.Find("button.fetch-btn");
        await cut.InvokeAsync(() => fetchBtn.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Image not found at the specified URL (HTTP 404).", cut.Markup);
            Assert.NotNull(cut.Find(".upload-url-section"));
        });
    }

    [Fact]
    public async Task ActorImageUploadModal_FetchUrlSuccess_TransitionsToCropper()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var fakeBytes = new byte[] { 1, 2, 3, 4 };
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        cacheService.DownloadImageFromUrlAsync("https://example.com/photo.jpg", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(true, Bytes: fakeBytes, ContentType: "image/jpeg"));
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        var urlInput = cut.Find("input[type='url']");
        urlInput.Input("https://example.com/photo.jpg");

        var fetchBtn = cut.Find("button.fetch-btn");
        await cut.InvokeAsync(() => fetchBtn.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".upload-dropzone"));
            Assert.Empty(cut.FindAll(".upload-url-section"));
            Assert.NotNull(cut.Find(".cropper-toolbar"));
            Assert.NotNull(cut.Find(".cropper-workspace"));
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save Photo"));
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Upload Full (Skip Crop)"));
        });
    }

    [Fact]
    public async Task ActorImageUploadModal_PressingEnterInUrlInput_TriggersFetch()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var fakeBytes = new byte[] { 1, 2, 3, 4 };
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        cacheService.DownloadImageFromUrlAsync("https://example.com/enter.jpg", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(true, Bytes: fakeBytes, ContentType: "image/png"));
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        var urlInput = cut.Find("input[type='url']");
        urlInput.Input("https://example.com/enter.jpg");
        await cut.InvokeAsync(() => urlInput.KeyDown(Key.Enter));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".cropper-workspace")));
        await cacheService.Received(1).DownloadImageFromUrlAsync("https://example.com/enter.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ActorImageUploadModal_RendersDefaultMaxSize_WhenNotConfigured()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("Supports JPEG, PNG, or WebP (up to 20 MB)", cut.Markup);
    }

    [Fact]
    public void ActorImageUploadModal_RendersCustomMaxSize_WhenConfigured()
    {
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);
        Services.AddSingleton(new ImageUploadSettings(50));

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("Supports JPEG, PNG, or WebP (up to 50 MB)", cut.Markup);
    }

    [Fact]
    public async Task ActorImageUploadModal_FetchUrlSuccess_StreamsImageInsteadOfDataUrl()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ActorImageUploadModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var fakeBytes = new byte[] { 1, 2, 3, 4 };
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        cacheService.DownloadImageFromUrlAsync("https://example.com/photo.png", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(true, Bytes: fakeBytes, ContentType: "image/png"));
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        cut.Find("input[type='url']").Input("https://example.com/photo.png");
        await cut.InvokeAsync(() => cut.Find("button.fetch-btn").Click());

        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.Null(cut.Find(".cropper-viewport img").GetAttribute("src"));
        Assert.DoesNotContain("data:", cut.Markup);
        var invocation = module.VerifyInvoke("setImageSource");
        Assert.Equal("image/png", invocation.Arguments[2]);
    }

    [Fact]
    public async Task ActorImageUploadModal_SavePhoto_IsDisabledUntilTheCropperIsUp()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ActorImageUploadModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var initCropper = module.SetupVoid("initCropper", _ => true);
        var cacheService = Substitute.For<IActorImageCacheService>();
        cacheService.HasCustomImageAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        cacheService.DownloadImageFromUrlAsync("https://example.com/photo.png", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(true, Bytes: [1, 2, 3, 4], ContentType: "image/png"));
        Services.AddSingleton(cacheService);

        var cut = Render<ActorImageUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        cut.Find("input[type='url']").Input("https://example.com/photo.png");
        await cut.InvokeAsync(() => cut.Find("button.fetch-btn").Click());

        // Clicked now, Save Photo would have no crop rect to read.
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.True(SavePhotoButton(cut).HasAttribute("disabled"));

        initCropper.SetVoidResult();
        cut.WaitForAssertion(() => Assert.False(SavePhotoButton(cut).HasAttribute("disabled")));
    }

    private static AngleSharp.Dom.IElement SavePhotoButton(IRenderedComponent<ActorImageUploadModal> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save Photo"));
}
