using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MetadataEditorModalTests : BunitContext
{
    public MetadataEditorModalTests() => JSInterop.AllowModalShell();

    private IMovieService SetUpMovieService()
    {
        var movieService = Substitute.For<IMovieService>();
        Services.AddSingleton(movieService);
        return movieService;
    }

    [Fact]
    public void ModalClosedByDefault()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));

        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }

    [Fact]
    public async Task Show_PrefillsFieldsFromMovie()
    {
        SetUpMovieService();
        var movie = new Movie
        {
            Id = 1,
            Code = "ABC-123",
            MetaTitle = "Display Title",
            MetaStudio = "Studio Name",
            MetaRuntimeMinutes = 115,
        };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        cut.Render(p => p.Add(x => x.Show, true));

        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));
        Assert.Equal("Display Title", cut.Find("#metadata-meta-title").GetAttribute("value"));
        Assert.Equal("Studio Name", cut.Find("#metadata-studio").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#metadata-runtime"));
    }

    [Fact]
    public async Task Show_HasNoPersonalNotesField()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123", Notes = "Some notes" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Empty(cut.FindAll("#metadata-notes"));
        Assert.DoesNotContain("Personal Notes", cut.Markup);
    }

    [Fact]
    public async Task Cancel_ClosesWithoutSaving()
    {
        var movieService = SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123" };

        var closeRequests = 0;
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie).Add(x => x.OnClose, () => closeRequests++));
        cut.Render(p => p.Add(x => x.Show, true));

        var cancelBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");
        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.Equal(1, closeRequests);
        cut.Render(p => p.Add(x => x.Show, false));
        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
        await movieService.DidNotReceive().UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReopeningStartsFromTheMoviesValuesAgain_ButARenderWhileOpenKeepsTheEdits()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123", MetaTitle = "Original" };
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie).Add(x => x.Show, true));
        cut.Find("#metadata-meta-title").Change("Edited");

        // The page reloads the movie after a save; that must not wipe what is on screen.
        cut.Render(p => p.Add(x => x.Movie, movie));
        Assert.Equal("Edited", cut.Find("#metadata-meta-title").GetAttribute("value"));

        cut.Render(p => p.Add(x => x.Show, false));
        cut.Render(p => p.Add(x => x.Show, true));
        Assert.Equal("Original", cut.Find("#metadata-meta-title").GetAttribute("value"));
    }

    [Fact]
    public async Task Submit_CallsUpdateMetadataAsync_ThenClosesAndRaisesOnSaved()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
        var movie = new Movie { Id = 42, Code = "ABC-123" };
        var saved = false;
        var closeRequests = 0;

        var cut = Render<MetadataEditorModal>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.OnSaved, () => saved = true)
            .Add(x => x.OnClose, () => closeRequests++));
        cut.Render(p => p.Add(x => x.Show, true));

        cut.Find("#metadata-meta-title").Change("Updated Title");
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await movieService.Received(1).UpdateMetadataAsync(
            42,
            Arg.Is<MovieMetadataUpdate>(u => u.MetaTitle == "Updated Title"),
            Arg.Any<CancellationToken>());
        Assert.True(saved);
        Assert.Equal(1, closeRequests);
        cut.Render(p => p.Add(x => x.Show, false));
        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }

    [Fact]
    public async Task Show_PrefillsTheContentType_AndSaveSendsTheChoice()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
        var movie = new Movie { Id = 5, Code = "ABC-123", ContentType = MovieContentType.Uncensored };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Equal("Uncensored", cut.Find("#metadata-content-type").GetAttribute("value"));
        Assert.Equal(["Censored", "Uncensored", "Decensored"], cut.FindAll("#metadata-content-type option").Select(o => o.TextContent));

        cut.Find("#metadata-content-type").Change(MovieContentType.Decensored.ToString());
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await movieService.Received(1).UpdateMetadataAsync(
            5,
            Arg.Is<MovieMetadataUpdate>(u => u.ContentType == MovieContentType.Decensored),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_SavedButNfoNotCreated_ShowsWarningStaysOpenAndRefreshesThePage()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, "the folder isn't writable"));
        var saved = 0;
        var closeRequests = 0;

        var cut = Render<MetadataEditorModal>(p => p
            .Add(x => x.Movie, new Movie { Id = 42, Code = "ABC-123" })
            .Add(x => x.OnSaved, () => saved++)
            .Add(x => x.OnClose, () => closeRequests++));
        cut.Render(p => p.Add(x => x.Show, true));
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.Contains("Your changes were saved, but the .nfo wasn't created: the folder isn't writable", cut.Find(".alert-warning").TextContent);
        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));
        Assert.Equal(1, saved);
        Assert.Equal(0, closeRequests);
    }

    [Fact]
    public async Task Submit_Failure_ShowsErrorAndStaysOpen()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Save failed."));
        var movie = new Movie { Id = 1, Code = "ABC-123" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        cut.Render(p => p.Add(x => x.Show, true));

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));
        Assert.Contains("Save failed.", cut.Markup);
    }

    private static Movie VrMovie()
    {
        var movie = new Movie { Id = 7, Code = "SIVR-059", VrType = "VR180 SBS" };
        movie.MovieFiles.Add(new MovieFile { Id = 70, FileName = "SIVR-059.mp4", VersionTag = "Original", IsPrimary = true, VrType = "VR180 SBS" });
        movie.MovieFiles.Add(new MovieFile { Id = 71, FileName = "SIVR-059-2D.mp4", VersionTag = "2D", VrType = null, VrTypePinned = true });
        return movie;
    }

    [Fact]
    public async Task VrFormat_OffersOnePickerPerVersion_ShowingAutoOrThePinnedChoice()
    {
        SetUpMovieService();
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, VrMovie()));

        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Equal("auto", cut.Find("#metadata-vr-70").GetAttribute("value"));
        Assert.Contains("Auto (VR180 SBS)", cut.Find("#metadata-vr-70").TextContent);
        Assert.Equal("none", cut.Find("#metadata-vr-71").GetAttribute("value"));
        Assert.Contains("Auto (detect again)", cut.Find("#metadata-vr-71").TextContent);
        Assert.Equal(["Original", "2D"], cut.FindAll(".metadata-editor-vr-version").Select(l => l.TextContent));
    }

    [Fact]
    public async Task VrFormat_OnlyAChangedVersionIsSaved_AlongWithTheMetadata()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(default, default!, default).ReturnsForAnyArgs(new OperationResult(true));
        movieService.SetVrTypeAsync(default, default, default, default, default).ReturnsForAnyArgs(new OperationResult(true));
        var saved = 0;
        var cut = Render<MetadataEditorModal>(p => p
            .Add(x => x.Movie, VrMovie())
            .Add(x => x.OnSaved, () => saved++));
        cut.Render(p => p.Add(x => x.Show, true));

        cut.Find("#metadata-vr-70").Change("VR180 TB");
        cut.Find("#metadata-vr-71").Change("auto");
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await movieService.Received(1).UpdateMetadataAsync(7, Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>());
        await movieService.Received(1).SetVrTypeAsync(7, 70, "VR180 TB", false, Arg.Any<CancellationToken>());
        await movieService.Received(1).SetVrTypeAsync(7, 71, null, true, Arg.Any<CancellationToken>());
        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task VrFormat_UnchangedChoices_AreNotSaved()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(default, default!, default).ReturnsForAnyArgs(new OperationResult(true));
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, VrMovie()));
        cut.Render(p => p.Add(x => x.Show, true));

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await movieService.DidNotReceiveWithAnyArgs().SetVrTypeAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task VrFormat_AFailedChange_KeepsTheEditorOpenWithTheError()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(default, default!, default).ReturnsForAnyArgs(new OperationResult(true));
        movieService.SetVrTypeAsync(default, default, default, default, default).ReturnsForAnyArgs(new OperationResult(false, "That version no longer exists."));
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, VrMovie()));
        cut.Render(p => p.Add(x => x.Show, true));

        cut.Find("#metadata-vr-70").Change("VR");
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.Contains("That version no longer exists.", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public async Task VrFormat_IsHidden_ForAMovieWithoutAVideoFile()
    {
        SetUpMovieService();
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, new Movie { Id = 1, Code = "ABC-123" }));

        cut.Render(p => p.Add(x => x.Show, true));

        Assert.DoesNotContain("VR format", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VrFormat_WithOneVersion_HasNoVersionLabel()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "SIVR-059" };
        movie.MovieFiles.Add(new MovieFile { Id = 10, FileName = "SIVR-059.mp4", VersionTag = "Original", IsPrimary = true });
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));

        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Single(cut.FindAll("select[id^=metadata-vr-]"));
        Assert.Empty(cut.FindAll(".metadata-editor-vr-version"));
    }

    [Fact]
    public async Task WhileSaving_BackdropAndCloseButtonDoNotClose()
    {
        var movieService = SetUpMovieService();
        var pending = new TaskCompletionSource<OperationResult>();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        IRenderedComponent<MetadataEditorModal>? cut = null;
        // The page closes the dialog when it is asked to.
        cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, new Movie { Id = 1, Code = "ABC-123" })
            .Add(x => x.OnClose, () => cut!.Render(q => q.Add(x => x.Show, false))));
        cut.Render(p => p.Add(x => x.Show, true));
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        cut.Find(".modal-shell-backdrop").Click();
        Assert.True(cut.Find(".modal-shell-close-btn").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));

        pending.SetResult(OperationResult.Ok());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal-shell-backdrop")));
    }

    [Fact]
    public async Task Submit_WhenTheSaveThrows_ReleasesTheSavingFlag_SoTheDialogCanClose()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OperationResult>(new InvalidOperationException("db locked")));
        IRenderedComponent<MetadataEditorModal>? cut = null;
        // The page closes the dialog when it is asked to.
        cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, new Movie { Id = 42, Code = "ABC-123" })
            .Add(x => x.OnClose, () => cut!.Render(q => q.Add(x => x.Show, false))));
        cut.Render(p => p.Add(x => x.Show, true));

        await Record.ExceptionAsync(() => cut.InvokeAsync(() => cut.Find("form").Submit()));

        var save = cut.FindAll("button").First(b => b.TextContent.Trim() == "Save");
        Assert.False(save.HasAttribute("disabled"));
        var cancel = cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");
        await cut.InvokeAsync(() => cancel.Click());
        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }
}
