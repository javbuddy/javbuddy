using Bunit;
using Javbuddy.Components.Pages.TorrentSortSections;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.TorrentSortSections;

/// <summary>The sort editor's 2:3 poster crop dialog.</summary>
public class SortPosterCropModalTests : BunitContext
{
    [Fact]
    public void Save_SendsNormalizedRectWithSourceSize_AndRaisesOnCropped()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<SortPosterCropModal.CropRectDto>("getCropRect").SetResult(new SortPosterCropModal.CropRectDto(0.1, 0.2, 0.3, 0.4));
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(7, "ABC-123", Arg.Any<CancellationToken>())
            .Returns((new PosterCropSource([1, 2], "image/jpeg", 800, 538), (string?)null));
        svc.CropPosterAsync(7, "res-1", Arg.Any<NormalizedCropRect>(), 800, 538, Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterCropResult(true, new PosterCropResponseDto(), null));
        Services.AddSingleton(svc);
        var cropped = 0;

        var cut = Render<SortPosterCropModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.TorrentDownloadId, 7)
            .Add(x => x.ResultId, "res-1")
            .Add(x => x.MovieId, "ABC-123")
            .Add(x => x.OnCropped, () => cropped++));

        cut.WaitForAssertion(() => module.VerifyInvoke("initCropper"));
        Assert.Equal(2.0 / 3.0, (double)module.VerifyInvoke("setAspectRatio").Arguments[0]!, 5);
        cut.Find(".spc-save").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, cropped));
        svc.Received().CropPosterAsync(7, "res-1", new NormalizedCropRect(0.1, 0.2, 0.3, 0.4), 800, 538, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SourceLoadFailure_ShowsError_NoSaveButton()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(7, "ABC-123", Arg.Any<CancellationToken>()).Returns(((PosterCropSource?)null, "javinizer-go returned 404: gone"));
        Services.AddSingleton(svc);

        var cut = Render<SortPosterCropModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.TorrentDownloadId, 7)
            .Add(x => x.ResultId, "res-1")
            .Add(x => x.MovieId, "ABC-123"));

        cut.WaitForAssertion(() => Assert.Contains("404", cut.Markup));
        Assert.Empty(cut.FindAll(".spc-save"));
    }

    [Fact]
    public void SaveFailure_ShowsErrorAndStaysOpen()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<SortPosterCropModal.CropRectDto>("getCropRect").SetResult(new SortPosterCropModal.CropRectDto(0, 0, 1, 1));
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(7, "ABC-123", Arg.Any<CancellationToken>())
            .Returns((new PosterCropSource([1, 2], "image/jpeg", 800, 538), (string?)null));
        svc.CropPosterAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs(new JavinizerPosterCropResult(false, null, "javinizer-go returned 409: busy"));
        Services.AddSingleton(svc);
        var cropped = 0;

        var cut = Render<SortPosterCropModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.TorrentDownloadId, 7)
            .Add(x => x.ResultId, "res-1")
            .Add(x => x.MovieId, "ABC-123")
            .Add(x => x.OnCropped, () => cropped++));

        cut.WaitForAssertion(() => cut.Find(".spc-save"));
        cut.Find(".spc-save").Click();

        cut.WaitForAssertion(() => Assert.Contains("409", cut.Markup));
        Assert.Equal(0, cropped);
        Assert.NotNull(cut.Find(".spc-save"));
    }

    [Fact]
    public void KeyPresses_DoNotBubbleToTheDrawer()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(default, default!, default).ReturnsForAnyArgs(((PosterCropSource?)null, "x"));
        Services.AddSingleton(svc);

        var cut = Render<SortPosterCropModal>(p => p.Add(x => x.Show, true).Add(x => x.ResultId, "r").Add(x => x.MovieId, "A"));

        Assert.Contains("onkeydown:stopPropagation", string.Join(",", cut.Find(".spc-backdrop").Attributes.Select(a => a.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_WhenCropperScriptFails_ShowsErrorInsteadOfThrowing()
    {
        var module = JSInterop.SetupModule("./Components/Shared/MovieCoverCropModal.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<SortPosterCropModal.CropRectDto>("getCropRect").SetException(new Microsoft.JSInterop.JSException("cropper gone"));
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(7, "ABC-123", Arg.Any<CancellationToken>())
            .Returns((new PosterCropSource([1, 2], "image/jpeg", 800, 538), (string?)null));
        Services.AddSingleton(svc);

        var cut = Render<SortPosterCropModal>(p => p.Add(x => x.Show, true).Add(x => x.TorrentDownloadId, 7).Add(x => x.ResultId, "res-1").Add(x => x.MovieId, "ABC-123"));
        cut.WaitForAssertion(() => cut.Find(".spc-save"));
        cut.Find(".spc-save").Click();

        cut.WaitForAssertion(() => Assert.Contains("cropper gone", cut.Markup));
        svc.DidNotReceiveWithAnyArgs().CropPosterAsync(default, default!, default!, default, default, default);
    }

    [Fact]
    public void CroppedPosterFallback_SaysWhy()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var svc = Substitute.For<ITorrentSortService>();
        svc.GetPosterCropSourceAsync(7, "ABC-123", Arg.Any<CancellationToken>())
            .Returns((new PosterCropSource([1, 2], "image/jpeg", 400, 600, IsFullSize: false), (string?)null));
        Services.AddSingleton(svc);

        var cut = Render<SortPosterCropModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.TorrentDownloadId, 7)
            .Add(x => x.ResultId, "res-1")
            .Add(x => x.MovieId, "ABC-123"));

        cut.WaitForAssertion(() => Assert.Contains("no full-size poster", cut.Find(".spc-fallback-note").TextContent));
        Assert.NotNull(cut.Find(".spc-save"));
    }
}
