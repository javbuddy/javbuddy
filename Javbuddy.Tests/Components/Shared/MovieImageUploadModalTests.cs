using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieImageUploadModalTests : BunitContext
{
    private readonly IMovieExtraFanartService service = Substitute.For<IMovieExtraFanartService>();
    private int uploadedCount;

    public MovieImageUploadModalTests()
    {
        Services.AddSingleton(service);
    }

    private IRenderedComponent<MovieImageUploadModal> RenderModal(bool show = true) =>
        Render<MovieImageUploadModal>(p => p
            .Add(m => m.Show, show)
            .Add(m => m.MovieId, 7)
            .Add(m => m.Code, "IPX-535")
            .Add(m => m.OnUploaded, () => uploadedCount++));

    [Fact]
    public void RendersNothing_WhenShowIsFalse()
    {
        var cut = RenderModal(show: false);

        Assert.Empty(cut.FindAll(".movie-image-upload-backdrop"));
    }

    [Fact]
    public async Task FetchFromUrl_Success_RaisesOnUploadedAndClearsTheInput()
    {
        service.AddFromUrlAsync(7, "https://example.com/a.jpg", Arg.Any<CancellationToken>()).Returns(ExtraFanartResult.Ok(1));
        var cut = RenderModal();

        cut.Find("#movie-image-url-input").Input("https://example.com/a.jpg");
        await cut.Find(".fetch-btn").ClickAsync(new());

        Assert.Equal(1, uploadedCount);
        Assert.Equal("", cut.Find("#movie-image-url-input").GetAttribute("value"));
        Assert.Contains("Added 1 image.", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public async Task FetchFromUrl_Failure_ShowsTheServiceError()
    {
        service.AddFromUrlAsync(7, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Fail("Image not found at the specified URL (HTTP 404)."));
        var cut = RenderModal();

        cut.Find("#movie-image-url-input").Input("https://example.com/x.jpg");
        await cut.Find(".fetch-btn").ClickAsync(new());

        Assert.Contains("HTTP 404", cut.Find(".alert-danger").TextContent);
        Assert.Equal(0, uploadedCount);
    }

    [Fact]
    public async Task UploadFiles_SendsEverySelectedFileToTheService()
    {
        service.AddAsync(7, Arg.Any<IReadOnlyList<ExtraFanartUploadItem>>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(2));
        var cut = RenderModal();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "a.jpg", contentType: "image/jpeg"),
            InputFileContent.CreateFromBinary([4, 5], "b.png", contentType: "image/png"));
        await cut.Find(".upload-btn").ClickAsync(new());

        await service.Received(1).AddAsync(7, Arg.Is<IReadOnlyList<ExtraFanartUploadItem>>(l => l.Count == 2), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, uploadedCount);
        Assert.Contains("Added 2 images.", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public void SelectingAnUnsupportedExtension_ShowsAnErrorAndKeepsUploadDisabled()
    {
        var cut = RenderModal();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "a.gif"));

        Assert.Contains("Only JPEG, PNG, and WebP", cut.Find(".alert-danger").TextContent);
        Assert.True(cut.Find(".upload-btn").HasAttribute("disabled"));
    }
}
