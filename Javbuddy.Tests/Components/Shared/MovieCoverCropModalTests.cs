using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Images;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieCoverCropModalTests : BunitContext
{
    public MovieCoverCropModalTests() => JSInterop.AllowModalShell();

    [Fact]
    public void MovieCoverCropModal_RendersHidden_WhenShowIsFalse()
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Code, "IPX-535")
            .Add(m => m.Title, "Sample Title"));

        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }

    [Fact]
    public void MovieCoverCropModal_RendersWithTabs_WhenShowIsTrue()
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource>
            {
                new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full"),
                new("poster", "Current Poster", "/image-cache/IPX-535/poster/full")
            }));
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535")
            .Add(m => m.Title, "Sample Title"));

        Assert.NotNull(cut.Find(".modal-shell-backdrop"));
        Assert.Contains("Crop Cover — Sample Title", cut.Markup);
        Assert.Contains("Backdrop / Jacket", cut.Markup);
        Assert.Contains("Current Poster", cut.Markup);
        Assert.NotEmpty(cut.FindAll("button[aria-label='Upload']"));
        Assert.Empty(cut.FindAll("button[aria-label='Import URL']"));
    }

    [Fact]
    public void MovieCoverCropModal_SourceStrip_UsesThumbnailsWithShortCaptions()
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            null,
            new List<MovieCropSource>
            {
                new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full", "/image-cache/IPX-535/fanart/thumb"),
                new("poster", "Current Poster", "/image-cache/IPX-535/poster/full"),
                new("extrafanart-0", "Gallery #1", "/image-cache/IPX-535/extrafanart/0/full"),
                new("extrafanart-1", "Gallery #2", "/image-cache/IPX-535/extrafanart/1/full"),
            }));
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));

        var thumbs = cut.FindAll(".source-thumb");
        Assert.Equal(4, thumbs.Count);
        // The strip uses the small variant when there is one, and falls back to the full image otherwise.
        Assert.Equal("/image-cache/IPX-535/fanart/thumb", thumbs[0].QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("/image-cache/IPX-535/poster/full", thumbs[1].QuerySelector("img")!.GetAttribute("src"));
        // Backdrop and poster carry an icon instead of a caption; gallery images are numbered without a "Gallery" prefix.
        Assert.NotNull(thumbs[0].QuerySelector(".source-thumb-icon"));
        Assert.NotNull(thumbs[1].QuerySelector(".source-thumb-icon"));
        Assert.Equal("1", thumbs[2].QuerySelector("span")!.TextContent);
        Assert.Equal("2", thumbs[3].QuerySelector("span")!.TextContent);
        Assert.DoesNotContain("Gallery", cut.Find(".source-strip").TextContent);
    }

    [Fact]
    public void MovieCoverCropModal_UploadPanel_OffersFileAndUrlTogether()
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535", null, new List<MovieCropSource>()));
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));

        Assert.NotEmpty(cut.FindAll(".upload-dropzone input[type='file']"));
        Assert.NotEmpty(cut.FindAll("#cover-image-url-input"));
    }

    [Fact]
    public void MovieCoverCropModal_ClickingUploadWithAnImageLoaded_ShowsTheUploadOptionsAgain()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.Empty(cut.FindAll(".upload-dropzone"));

        // Upload is the active source after the first click; a second click must still bring the options back.
        cut.Find("button[aria-label='Upload']").Click();
        Assert.NotEmpty(cut.FindAll(".upload-dropzone"));
        Assert.Empty(cut.FindAll(".cropper-viewport"));

        cut.Find("button[aria-label='Upload']").Click();
        Assert.NotEmpty(cut.FindAll(".upload-dropzone"));
    }

    [Fact]
    public void MovieCoverCropModal_InfoHint_SitsInTheHeaderBesideTheCloseButton_OnceAnImageIsLoaded()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535", null, new List<MovieCropSource>()));
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        Assert.Empty(cut.FindAll(".header-info"));

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[] { 1, 2, 3 }, "cover.jpg", null, "image/jpeg"));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".modal-shell-header-actions .header-info")));
        Assert.Contains("Scroll to zoom", cut.Find(".header-info").GetAttribute("data-tip"));
    }

    [Fact]
    public void MovieCoverCropModal_TriggersOnClose_WhenCloseButtonClicked()
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            null,
            new List<MovieCropSource>()));
        Services.AddSingleton(cropService);

        var closed = false;
        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535")
            .Add(m => m.OnClose, () => closed = true));

        var closeBtn = cut.Find(".modal-shell-close-btn");
        closeBtn.Click();

        Assert.True(closed);
    }

    [Theory]
    [InlineData(true, "Saving writes poster.jpg to the movie folder")]
    [InlineData(false, "kept in Javbuddy's image cache only")]
    public void MovieCoverCropModal_ExplainsWhereTheCropIsSaved(bool hasLocalFolder, string expectedHint)
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") },
            hasLocalFolder));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));

        cut.WaitForAssertion(() => Assert.Contains(expectedHint, cut.Find(".crop-save-btn").GetAttribute("title")));
    }

    [Fact]
    public void MovieCoverCropModal_DefaultSource_StreamsImageInsteadOfDataUrl()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));

        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.Null(cut.Find(".cropper-viewport img").GetAttribute("src"));
        Assert.DoesNotContain("data:", cut.Markup);
        var invocation = module.VerifyInvoke("setImageSource");
        Assert.Equal("image/jpeg", invocation.Arguments[2]);
    }

    [Theory]
    [InlineData(".rotate-right-btn", true)]
    [InlineData(".rotate-left-btn", false)]
    public void MovieCoverCropModal_Rotate_ReloadsTheCropperWithTheRotatedImage(string button, bool clockwise)
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var rotated = new byte[] { 9, 9, 9 };
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Any<byte[]>(), clockwise, Arg.Any<CancellationToken>()).Returns(rotated);
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));

        cut.Find(button).Click();

        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 2));
        cropService.Received(1).RotateImageAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3, 4 })), clockwise, Arg.Any<CancellationToken>());
        var contentTypes = module.Invocations.Where(i => i.Identifier == "setImageSource").Select(i => i.Arguments[2]).ToList();
        Assert.Equal(["image/jpeg", "image/png"], contentTypes);
    }

    [Fact]
    public void MovieCoverCropModal_Rotate_Failure_ShowsErrorAndKeepsTheImage()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Any<byte[]>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));

        cut.Find(".rotate-right-btn").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not rotate the image.", cut.Markup));
        Assert.NotEmpty(cut.FindAll(".crop-save-btn"));
    }

    [Fact]
    public void MovieCoverCropModal_Rotate_KeepsTheChosenAspectRatio_AndChainsRotations()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var firstRotation = new byte[] { 7, 7 };
        var secondRotation = new byte[] { 8, 8 };
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3, 4 })), true, Arg.Any<CancellationToken>()).Returns(firstRotation);
        cropService.RotateImageAsync(Arg.Is<byte[]>(b => b.SequenceEqual(firstRotation)), false, Arg.Any<CancellationToken>()).Returns(secondRotation);
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));

        // Free-form (no ratio) must survive a rotation instead of snapping back to the DVD ratio.
        cut.FindAll(".ratio-group .btn-tool").Last().Click();
        cut.Find(".rotate-right-btn").Click();
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 2));
        cut.Find(".rotate-left-btn").Click();
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 3));

        cropService.Received(1).RotateImageAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(firstRotation)), false, Arg.Any<CancellationToken>());
        var lastRatioCall = module.Invocations.Last(i => i.Identifier == "setAspectRatio");
        Assert.Null(lastRatioCall.Arguments[0]);
        Assert.Contains("active", cut.FindAll(".ratio-group .btn-tool").Last().ClassList);
    }

    [Fact]
    public void MovieCoverCropModal_StraightenSlider_RotatesTheCropperAndResetButtonClearsIt()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>()).Returns(new byte[] { 9, 9 });
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));

        cut.Find(".straighten-slider").Input("-3.5");

        Assert.Equal("-3.5°", StraightenValue(cut));
        Assert.Equal(-3.5, module.Invocations.Last(i => i.Identifier == "setRotation").Arguments[0]);

        // A 90° turn reloads the cropper but keeps the fine angle, applied again after init.
        cut.Find(".rotate-right-btn").Click();
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 2));
        cut.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(i => i.Identifier == "setRotation")));
        Assert.Equal(-3.5, module.Invocations.Last(i => i.Identifier == "setRotation").Arguments[0]);

        cut.FindAll(".cropper-toolbar > .btn-tool").Single(b => b.TextContent == "Reset").Click();

        // After the 90° turn, Reset reloads the original image, so the fine angle is cleared without a further setRotation.
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 3));
        Assert.Equal("0.0°", StraightenValue(cut));
        Assert.Equal(2, module.Invocations.Count(i => i.Identifier == "setRotation"));
    }

    [Fact]
    public void MovieCoverCropModal_StraightenSlider_KeepsTheZoomLevel()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);

        cut.Find(".zoom-slider").Input("2.0");
        cut.Find(".straighten-slider").Input("5");

        Assert.Equal("2.0×", cut.Find(".zoom-controls .slider-value").TextContent);
        Assert.Equal(2.0, double.Parse(cut.Find(".zoom-slider").GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture));
        // The zoom is not re-sent to the cropper by the modal: the cropper keeps it itself when it lays out again.
        Assert.DoesNotContain(module.Invocations.Where(i => i.Identifier == "setZoom").Skip(1), i => (double)i.Arguments[0]! == 1.0);
    }

    [Fact]
    public void MovieCoverCropModal_Toolbar_ShowsStraightenBeforeZoom_WithShortRatioLabelsAndIconRotateButtons()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);

        var toolbar = cut.Find(".cropper-toolbar");
        Assert.True(toolbar.InnerHtml.IndexOf("straighten-controls", StringComparison.Ordinal) < toolbar.InnerHtml.IndexOf("zoom-controls", StringComparison.Ordinal));
        Assert.Equal(["DVD", "2:3", "Free"], cut.FindAll(".ratio-group .btn-tool").Select(b => b.TextContent.Trim()));
        Assert.Equal("Rotate left", cut.Find(".rotate-left-btn").GetAttribute("aria-label"));
        Assert.Equal("", cut.Find(".rotate-left-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".preview-title, .preview-hint"));
    }

    [Fact]
    public void MovieCoverCropModal_Reset_UndoesNinetyDegreeTurns_AndKeepsTheRatio()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>()).Returns(new byte[] { 9, 9 });
        Services.AddSingleton(cropService);
        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        cut.FindAll(".ratio-group .btn-tool").Last().Click();
        cut.Find(".rotate-right-btn").Click();
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 2));

        cut.FindAll(".cropper-toolbar > .btn-tool").Single(b => b.TextContent == "Reset").Click();

        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 3));
        var contentTypes = module.Invocations.Where(i => i.Identifier == "setImageSource").Select(i => i.Arguments[2]).ToList();
        Assert.Equal(["image/jpeg", "image/png", "image/jpeg"], contentTypes);
        Assert.Null(module.Invocations.Last(i => i.Identifier == "setAspectRatio").Arguments[0]);
    }

    [Fact]
    public void MovieCoverCropModal_Reset_WithoutTurns_ResetsZoomStraightenAndCropBoxOnly()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);
        cut.Find(".straighten-slider").Input("4");

        cut.FindAll(".cropper-toolbar > .btn-tool").Single(b => b.TextContent == "Reset").Click();

        module.VerifyInvoke("resetCrop");
        Assert.Equal(1, module.Invocations.Count(i => i.Identifier == "initCropper"));
        Assert.Equal("0.0°", StraightenValue(cut));
        Assert.Equal("1.0×", cut.Find(".zoom-controls .slider-value").TextContent);
    }

    [Fact]
    public void MovieCoverCropModal_Rotate_ShowsASpinnerOnlyOverlayWhileRotating()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var gate = new TaskCompletionSource<byte[]?>();
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.RotateImageAsync(Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        Services.AddSingleton(cropService);
        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.Empty(cut.FindAll(".cropper-busy"));

        cut.Find(".rotate-right-btn").Click();

        cut.WaitForAssertion(() =>
        {
            var busy = cut.Find(".cropper-busy");
            Assert.NotNull(busy.QuerySelector(".spinner-border"));
            // No visible text, only a screen-reader label.
            Assert.Equal("Rotating image", busy.QuerySelector(".visually-hidden")!.TextContent);
            Assert.Equal("", busy.TextContent.Replace("Rotating image", "").Trim());
        });

        gate.SetResult(new byte[] { 9, 9 });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cropper-busy")));
    }

    [Fact]
    public void MovieCoverCropModal_ActiveSourceIsExposedWithAriaPressed_AndClickingUploadDestroysTheCropper()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);
        Assert.Equal("true", cut.Find(".source-thumb").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("button[aria-label='Upload']").GetAttribute("aria-pressed"));

        cut.Find("button[aria-label='Upload']").Click();

        Assert.Equal("true", cut.Find("button[aria-label='Upload']").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find(".source-thumb").GetAttribute("aria-pressed"));
        module.VerifyInvoke("destroyCropper");
    }

    [Fact]
    public async Task MovieCoverCropModal_WheelZoomFromJs_MovesTheSliderAndReadout()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);

        await cut.InvokeAsync(() => cut.Instance.OnZoomChanged(2.5000000000000004));

        Assert.Equal("2.5×", cut.Find(".zoom-controls .slider-value").TextContent);
        Assert.Equal("2.50", cut.Find(".zoom-slider").GetAttribute("value"));
    }

    [Fact]
    public async Task MovieCoverCropModal_ImageDecodeFailure_ShowsAnErrorInsteadOfABlankCropper()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderWithLoadedImage(module);

        await cut.InvokeAsync(() => cut.Instance.OnImageError());

        Assert.Contains("could not be displayed", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll(".cropper-busy"));
    }

    [Fact]
    public void MovieCoverCropModal_WhileAnImageIsPreparing_SaveRotateResetAndSourcesAreLocked()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource>
            {
                new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full"),
                new("poster", "Current Poster", "/image-cache/IPX-535/poster/full"),
            }));
        cropService.GetSourceBytesAsync("IPX-535", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        Services.AddSingleton(cropService);
        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.False(cut.Find(".crop-save-btn").HasAttribute("disabled"));

        // Hold the next image's setup open so the modal stays in its "preparing" state.
        var pending = module.SetupVoid("setImageSource", _ => true);
        cut.FindAll(".source-thumb").Last().Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cropper-busy")));
        Assert.True(cut.Find(".crop-save-btn").HasAttribute("disabled"));
        Assert.True(cut.Find(".rotate-right-btn").HasAttribute("disabled"));
        Assert.True(cut.Find(".straighten-slider").HasAttribute("disabled"));
        Assert.All(cut.FindAll(".source-thumb"), t => Assert.True(t.HasAttribute("disabled")));
        Assert.Equal("Preparing image", cut.Find(".cropper-busy .visually-hidden").TextContent);

        pending.SetVoidResult();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cropper-busy")));
        Assert.False(cut.Find(".crop-save-btn").HasAttribute("disabled"));
    }

    private IRenderedComponent<MovieCoverCropModal> RenderWithLoadedImage(BunitJSModuleInterop module)
    {
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource> { new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full") }));
        cropService.GetSourceBytesAsync("IPX-535", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        Services.AddSingleton(cropService);
        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        return cut;
    }

    private static string StraightenValue(IRenderedComponent<MovieCoverCropModal> cut) =>
        cut.Find(".straighten-controls .slider-value").TextContent;

    [Fact]
    public void MovieCoverCropModal_Save_PassesTheStraightenAngle_AndANewSourceResetsIt()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<MovieCoverCropModal.CropRectDto>("getCropRect").SetResult(new MovieCoverCropModal.CropRectDto(0.1, 0.2, 0.3, 0.4));
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new MovieCropSources(
            "IPX-535",
            "backdrop",
            new List<MovieCropSource>
            {
                new("backdrop", "Backdrop / Jacket", "/image-cache/IPX-535/fanart/full"),
                new("poster", "Current Poster", "/image-cache/IPX-535/poster/full"),
            }));
        cropService.GetSourceBytesAsync("IPX-535", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4 });
        cropService.SaveCroppedCoverAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NormalizedCropRect>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new MovieCoverCropSaveResult(true, Message: "Saved."));
        Services.AddSingleton(cropService);

        var cut = Render<MovieCoverCropModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Code, "IPX-535"));
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));

        // Switching source reloads the image and clears the fine angle.
        cut.Find(".straighten-slider").Input("7.5");
        cut.FindAll(".source-thumb").First(b => b.GetAttribute("title") == "Current Poster").Click();
        cut.WaitForAssertion(() => Assert.Equal("0.0°", StraightenValue(cut)));

        cut.Find(".straighten-slider").Input("-12.3");
        cut.Find(".crop-save-btn").Click();

        cut.WaitForAssertion(() => cropService.Received(1).SaveCroppedCoverAsync(
            "IPX-535",
            Arg.Any<byte[]>(),
            new NormalizedCropRect(0.1, 0.2, 0.3, 0.4),
            -12.3,
            Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void MovieCoverCropModal_Save_BeforeTheImageIsLaidOut_SavesNothing()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        // The cropper session returns null until the image has loaded and has a crop box.
        module.Setup<MovieCoverCropModal.CropRectDto>("getCropRect").SetResult(null!);
        var cut = RenderWithLoadedImage(module);
        var cropService = Services.GetRequiredService<IMovieCoverCropService>();

        cut.Find(".crop-save-btn").Click();

        cut.WaitForAssertion(() => Assert.Contains("still loading", cut.Markup));
        cropService.DidNotReceiveWithAnyArgs().SaveCroppedCoverAsync(default!, default!, default!, default, default);
    }
}
