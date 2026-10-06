using Bunit;
using Javbuddy.Components.Shared;
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

        Assert.Empty(cut.FindAll(".crop-modal-backdrop"));
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

        Assert.NotNull(cut.Find(".crop-modal-backdrop"));
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

        var closeBtn = cut.Find(".crop-modal-close-btn");
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
}
