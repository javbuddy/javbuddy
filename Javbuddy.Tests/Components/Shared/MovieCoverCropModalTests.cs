using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Images;
using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieCoverCropModalTests : BunitContext
{
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
        Assert.Contains("Upload File", cut.Markup);
        Assert.Contains("Import URL", cut.Markup);
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
        Assert.True(cut.Find(".straighten-reset-btn").HasAttribute("disabled"));

        cut.Find(".straighten-slider").Input("-3.5");

        Assert.Equal("-3.5°", cut.Find(".straighten-value").TextContent);
        Assert.Equal(-3.5, module.Invocations.Last(i => i.Identifier == "setRotation").Arguments[0]);
        Assert.False(cut.Find(".straighten-reset-btn").HasAttribute("disabled"));

        // A 90° turn reloads the cropper but keeps the fine angle, applied again after init.
        cut.Find(".rotate-right-btn").Click();
        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper", 2));
        cut.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(i => i.Identifier == "setRotation")));
        Assert.Equal(-3.5, module.Invocations.Last(i => i.Identifier == "setRotation").Arguments[0]);

        cut.Find(".straighten-reset-btn").Click();

        Assert.Equal(0.0, module.Invocations.Last(i => i.Identifier == "setRotation").Arguments[0]);
        Assert.Equal("0.0°", cut.Find(".straighten-value").TextContent);
    }

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
        cut.FindAll(".source-tab-btn").First(b => b.TextContent.Contains("Current Poster")).Click();
        cut.WaitForAssertion(() => Assert.Equal("0.0°", cut.Find(".straighten-value").TextContent));

        cut.Find(".straighten-slider").Input("-12.3");
        cut.Find(".crop-save-btn").Click();

        cut.WaitForAssertion(() => cropService.Received(1).SaveCroppedCoverAsync(
            "IPX-535",
            Arg.Any<byte[]>(),
            new NormalizedCropRect(0.1, 0.2, 0.3, 0.4),
            -12.3,
            Arg.Any<CancellationToken>()));
    }
}
