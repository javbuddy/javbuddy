using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MetadataEditorModalTests : BunitContext
{
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

        Assert.Empty(cut.FindAll(".metadata-editor-modal-backdrop"));
    }

    [Fact]
    public async Task OpenAsync_PrefillsFieldsFromMovie()
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
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.NotEmpty(cut.FindAll(".metadata-editor-modal-backdrop"));
        Assert.Equal("Display Title", cut.Find("#metadata-meta-title").GetAttribute("value"));
        Assert.Equal("Studio Name", cut.Find("#metadata-studio").GetAttribute("value"));
        Assert.Equal("115", cut.Find("#metadata-runtime").GetAttribute("value"));
    }

    [Fact]
    public async Task OpenAsync_HasNoPersonalNotesField()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123", Notes = "Some notes" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Empty(cut.FindAll("#metadata-notes"));
        Assert.DoesNotContain("Personal Notes", cut.Markup);
    }

    [Fact]
    public async Task Cancel_ClosesWithoutSaving()
    {
        var movieService = SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "ABC-123" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        var cancelBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");
        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.Empty(cut.FindAll(".metadata-editor-modal-backdrop"));
        await movieService.DidNotReceive().UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_CallsUpdateMetadataAsync_ThenClosesAndRaisesOnSaved()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
        var movie = new Movie { Id = 42, Code = "ABC-123" };
        var saved = false;

        var cut = Render<MetadataEditorModal>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.OnSaved, () => saved = true));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        cut.Find("#metadata-meta-title").Change("Updated Title");
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await movieService.Received(1).UpdateMetadataAsync(
            42,
            Arg.Is<MovieMetadataUpdate>(u => u.MetaTitle == "Updated Title"),
            Arg.Any<CancellationToken>());
        Assert.True(saved);
        Assert.Empty(cut.FindAll(".metadata-editor-modal-backdrop"));
    }

    [Fact]
    public async Task Submit_Failure_ShowsErrorAndStaysOpen()
    {
        var movieService = SetUpMovieService();
        movieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Runtime must be zero or greater."));
        var movie = new Movie { Id = 1, Code = "ABC-123" };

        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.NotEmpty(cut.FindAll(".metadata-editor-modal-backdrop"));
        Assert.Contains("Runtime must be zero or greater.", cut.Markup);
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

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

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
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

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
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

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
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        cut.Find("#metadata-vr-70").Change("VR");
        await cut.InvokeAsync(() => cut.Find("form").Submit());

        Assert.Contains("That version no longer exists.", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public async Task VrFormat_IsHidden_ForAMovieWithoutAVideoFile()
    {
        SetUpMovieService();
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, new Movie { Id = 1, Code = "ABC-123" }));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.DoesNotContain("VR format", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VrFormat_WithOneVersion_HasNoVersionLabel()
    {
        SetUpMovieService();
        var movie = new Movie { Id = 1, Code = "SIVR-059" };
        movie.MovieFiles.Add(new MovieFile { Id = 10, FileName = "SIVR-059.mp4", VersionTag = "Original", IsPrimary = true });
        var cut = Render<MetadataEditorModal>(p => p.Add(x => x.Movie, movie));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Single(cut.FindAll("select[id^=metadata-vr-]"));
        Assert.Empty(cut.FindAll(".metadata-editor-vr-version"));
    }
}
