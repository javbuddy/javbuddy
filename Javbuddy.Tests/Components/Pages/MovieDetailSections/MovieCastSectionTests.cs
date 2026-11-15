using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieCastSectionTests : BunitContext
{
    private readonly IActorTagService actorTags = Substitute.For<IActorTagService>();
    private readonly Javbuddy.Services.Movies.IMovieCoverCropService coverCrop = Substitute.For<Javbuddy.Services.Movies.IMovieCoverCropService>();
    private readonly Javbuddy.Services.Actors.IActorService actorService = Substitute.For<Javbuddy.Services.Actors.IActorService>();

    public MovieCastSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        actorTags.GetMovieTagsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        actorTags.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(actorTags);
        Services.AddSingleton(Substitute.For<IMovieService>());
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<ITagService>());
        coverCrop.GetCropSourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Javbuddy.Services.Movies.MovieCropSources("ABC-123", null, [new Javbuddy.Services.Movies.MovieCropSource("backdrop", "Backdrop", "/full", "/thumb")]));
        coverCrop.GetSourceBytesAsync("ABC-123", "backdrop", Arg.Any<CancellationToken>()).Returns(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 });
        Services.AddSingleton(coverCrop);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
    }

    private IRenderedComponent<MovieCastSection> RenderCast() =>
        Render<MovieCastSection>(p => p
            .Add(x => x.Movie, new Movie { Id = 5 })
            .Add(x => x.Cast, [new MovieCastEntry("Mei", 1, false), new MovieCastEntry("Rio", 2, false)]));

    private static void EditCast(IRenderedComponent<MovieCastSection> cut) => cut.Find(".movie-detail-edit-cast-btn").Click();

    private static IReadOnlyList<string?> Expanded(IRenderedComponent<MovieCastSection> cut) =>
        [.. cut.FindAll(".actor-pill-add").Select(b => b.GetAttribute("aria-expanded"))];

    [Fact]
    public void OpeningAnotherActorsTagSearch_ClosesTheFirst()
    {
        var cut = RenderCast();
        EditCast(cut);

        cut.FindAll(".actor-pill-add")[0].Click();
        Assert.Equal(["true", "false"], Expanded(cut));

        cut.FindAll(".actor-pill-add")[1].Click();
        Assert.Equal(["false", "true"], Expanded(cut));
        Assert.Single(cut.FindAll("input.tag-search-input"));
    }

    [Fact]
    public void ClickingTheOpenActorsTagPillAgain_ClosesIt()
    {
        var cut = RenderCast();
        EditCast(cut);

        cut.FindAll(".actor-pill-add")[0].Click();
        cut.FindAll(".actor-pill-add")[0].Click();

        Assert.Equal(["false", "false"], Expanded(cut));
        Assert.Empty(cut.FindAll("input.tag-search-input"));
    }

    [Fact]
    public void CloseAdding_ClosesTheOpenSearch()
    {
        var cut = RenderCast();
        EditCast(cut);
        cut.FindAll(".actor-pill-add")[0].Click();

        cut.InvokeAsync(() => cut.Instance.CloseAdding());

        Assert.Equal(["false", "false"], Expanded(cut));
    }

    [Fact]
    public void TurningEditCastOff_ClosesTheOpenSearch_SoItIsClosedWhenEditingResumes()
    {
        var cut = RenderCast();
        EditCast(cut);
        cut.FindAll(".actor-pill-add")[0].Click();

        EditCast(cut);
        EditCast(cut);

        Assert.Equal(["false", "false"], Expanded(cut));
    }

    private IRenderedComponent<MovieCastSection> RenderWithPlaceholder() =>
        Render<MovieCastSection>(p => p
            .Add(x => x.Movie, new Movie { Id = 5 })
            .Add(x => x.Cast, [new MovieCastEntry("Mei", 1, false), new MovieCastEntry("Unidentified 1 (ABC-123)", 9, false, IsPlaceholder: true)]));

    [Fact]
    public void AnUnidentifiedPerformer_IsLabelled_AndTakesActorTagsLikeAnyCastMember()
    {
        var cut = RenderWithPlaceholder();
        EditCast(cut);

        Assert.Single(cut.FindAll(".movie-detail-cast-unidentified"));
        Assert.Equal(2, cut.FindAll(".actor-pill-add").Count);
    }

    [Fact]
    public void RemovingAnUnidentifiedPerformer_AsksFirst_ThenDeletesThem_NotJustUnlinksThem()
    {
        var cut = RenderWithPlaceholder();
        EditCast(cut);

        cut.Find("button[aria-label='Remove Unidentified 1 (ABC-123)']").Click();
        actorService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        cut.Find(".delete-confirm-confirm-btn").Click();

        actorService.Received(1).DeleteAsync(9, Arg.Any<CancellationToken>());
        Services.GetRequiredService<IMovieService>().DidNotReceiveWithAnyArgs().RemoveActorFromMovieAsync(default, default(int));
    }

    [Fact]
    public void TheUnidentifiedPerformerButton_AddsOneWithTheTypedLabel()
    {
        actorService.CreatePlaceholderAsync(5, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Javbuddy.Services.Actors.ActorOperationResult.Ok(new Actor { Id = 9, FirstName = "Blonde (ABC-123)" }));
        var cut = RenderCast();
        EditCast(cut);

        cut.Find("input.movie-detail-cast-unidentified-label").Change("Blonde");
        cut.Find("form.movie-detail-cast-unidentified-add").Submit();

        actorService.Received(1).CreatePlaceholderAsync(5, "Blonde", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnUnidentifiedPerformer_HasAPhotoFromMovieImageButton_OthersDoNot()
    {
        var cut = RenderWithPlaceholder();
        Assert.Empty(cut.FindAll(".movie-detail-cast-photo-btn"));

        EditCast(cut);

        Assert.Single(cut.FindAll(".movie-detail-cast-photo-btn"));
    }

    [Fact]
    public void CancellingTheDeleteConfirmation_KeepsTheUnidentifiedPerformer()
    {
        var cut = RenderWithPlaceholder();
        EditCast(cut);

        cut.Find("button[aria-label='Remove Unidentified 1 (ABC-123)']").Click();
        cut.Find(".delete-confirm-cancel-btn").Click();

        actorService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        Assert.Empty(cut.FindAll(".modal-shell-panel"));
    }

    [Fact]
    public void ThePhotoButton_OpensThePickerOfTheMoviesOwnImages()
    {
        var cut = RenderWithPlaceholder();
        EditCast(cut);

        cut.Find(".movie-detail-cast-photo-btn").Click();

        Assert.Contains("Photo from a movie image", cut.Find(".modal-shell-panel").TextContent);
    }

    [Fact]
    public void TheUnidentifiedPerformerButton_IsOnlyOfferedWhileEditingTheCast()
    {
        var cut = RenderCast();

        Assert.Empty(cut.FindAll("form.movie-detail-cast-unidentified-add"));
    }

    [Fact]
    public void ASecondSubmit_WhileTheFirstIsStillAdding_DoesNothing()
    {
        var pending = new TaskCompletionSource<Javbuddy.Services.Actors.ActorOperationResult>();
        actorService.CreatePlaceholderAsync(5, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderCast();
        EditCast(cut);

        cut.Find("form.movie-detail-cast-unidentified-add").Submit();
        cut.Find("form.movie-detail-cast-unidentified-add").Submit();
        pending.SetResult(Javbuddy.Services.Actors.ActorOperationResult.Ok(new Actor { Id = 9, FirstName = "Unidentified 1" }));

        actorService.Received(1).CreatePlaceholderAsync(5, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AFailedAdd_ShowsItsError_AndKeepsTheTypedLabel()
    {
        actorService.CreatePlaceholderAsync(5, "Blonde", Arg.Any<CancellationToken>())
            .Returns(Javbuddy.Services.Actors.ActorOperationResult.Fail("That name is already in use. Try a different label."));
        var cut = RenderCast();
        EditCast(cut);
        cut.Find("input.movie-detail-cast-unidentified-label").Change("Blonde");

        cut.Find("form.movie-detail-cast-unidentified-add").Submit();

        Assert.Contains("already in use", cut.Find(".movie-detail-cast-error").TextContent);
        Assert.Equal("Blonde", cut.Find("input.movie-detail-cast-unidentified-label").GetAttribute("value"));
    }

    [Fact]
    public async Task ClosingTheCropDialog_GoesBackToTheImages_AndClosingThePickerEndsIt()
    {
        var cut = RenderWithPlaceholder();
        EditCast(cut);
        await cut.Find(".movie-detail-cast-photo-btn").ClickAsync(new());
        await cut.Find(".movie-image-picker-item").ClickAsync(new());

        Assert.Contains("Change Portrait", cut.Find(".modal-shell-panel").TextContent);

        await cut.Find(".modal-shell-close-btn").ClickAsync(new());
        Assert.Contains("Photo from a movie image", cut.Find(".modal-shell-panel").TextContent);

        await cut.Find(".modal-shell-close-btn").ClickAsync(new());
        Assert.Empty(cut.FindAll(".modal-shell-panel"));
    }
}
