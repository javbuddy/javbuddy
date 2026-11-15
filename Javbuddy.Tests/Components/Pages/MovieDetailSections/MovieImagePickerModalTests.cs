using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieImagePickerModalTests : BunitContext
{
    private readonly IMovieCoverCropService crop = Substitute.For<IMovieCoverCropService>();

    public MovieImagePickerModalTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(crop);
        Sources();
    }

    private void Sources(params MovieCropSource[] sources) =>
        crop.GetCropSourcesAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(new MovieCropSources("ABC-123", null, sources));

    private IRenderedComponent<MovieImagePickerModal> RenderPicker(Action<Javbuddy.Components.Shared.ActorImageUploadModal.InitialImageSource>? picked = null) =>
        Render<MovieImagePickerModal>(p => p
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.ActorName, "Unidentified 1")
            .Add(x => x.OnPicked, picked ?? (_ => { })));

    [Fact]
    public void OffersEveryImageTheMovieHas()
    {
        Sources(new MovieCropSource("backdrop", "Backdrop / Jacket", "/full", "/thumb"), new MovieCropSource("extrafanart-0", "Gallery #1", "/g/full", "/g/thumb"));

        var cut = RenderPicker();

        Assert.Equal(["Backdrop / Jacket", "Gallery #1"], cut.FindAll(".movie-image-picker-item span").Select(s => s.TextContent));
        Assert.Equal("/thumb", cut.Find(".movie-image-picker-item img").GetAttribute("src"));
    }

    [Fact]
    public void WithNoImages_SaysSo()
    {
        var cut = RenderPicker();

        Assert.Contains("no images", cut.Markup);
        Assert.Empty(cut.FindAll(".movie-image-picker-item"));
    }

    [Fact]
    public void AnOfflineLibrary_ShowsAnError_InsteadOfBreakingThePage()
    {
        crop.GetCropSourcesAsync("ABC-123", Arg.Any<CancellationToken>()).Returns<MovieCropSources>(_ => throw new IOException("offline"));

        var cut = RenderPicker();

        Assert.Contains("could not be listed", cut.Find("[role=alert]").TextContent);
    }

    [Fact]
    public async Task PickingAnImage_HandsItsBytesToTheHost()
    {
        Sources(new MovieCropSource("extrafanart-0", "Gallery #1", "/g/full", "/g/thumb"));
        crop.GetSourceBytesAsync("ABC-123", "extrafanart-0", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
        byte[]? received = null;
        var cut = RenderPicker(image => received = image.Bytes);

        await cut.Find(".movie-image-picker-item").ClickAsync(new());

        Assert.Equal([1, 2, 3], received);
    }

    [Fact]
    public async Task AnImageThatCantBeRead_ShowsAnError_AndPicksNothing()
    {
        Sources(new MovieCropSource("extrafanart-0", "Gallery #1", "/g/full", "/g/thumb"));
        crop.GetSourceBytesAsync("ABC-123", "extrafanart-0", Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var picked = false;
        var cut = RenderPicker(_ => picked = true);

        await cut.Find(".movie-image-picker-item").ClickAsync(new());

        Assert.False(picked);
        Assert.Contains("not available", cut.Find("[role=alert]").TextContent);
    }
}
