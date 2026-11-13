using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieDeleteDialogTests : BunitContext
{
    private readonly IMovieService movieService = Substitute.For<IMovieService>();
    private readonly Movie movie = new() { Id = 7, Code = "ABC-123", MetaTitle = "A Title" };

    public MovieDeleteDialogTests()
    {
        JSInterop.AllowModalShell();
        Services.AddSingleton(movieService);
        movieService.GetFolderDeletePreviewAsync(7, Arg.Any<CancellationToken>()).Returns(new MovieFolderDeletePreview(null, [], 0));
    }

    private IRenderedComponent<MovieDeleteDialog> RenderDialog(bool show = true, Func<Task>? onClose = null) =>
        Render<MovieDeleteDialog>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.Show, show)
            .Add(x => x.OnClose, () => onClose?.Invoke() ?? Task.CompletedTask));

    [Fact]
    public void WhileClosed_RendersNothing()
    {
        var cut = RenderDialog(show: false);

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void WhenOpened_AsksToConfirmAndLeavesTheFilesAloneByDefault()
    {
        var cut = RenderDialog();

        Assert.Contains("Video files on disk are left alone.", cut.Find(".delete-confirm-backdrop").TextContent);
        Assert.Contains("ABC-123", cut.Find(".modal-shell-title").TextContent);
    }

    [Fact]
    public async Task Confirming_DeletesTheMovieWithoutItsFolder_ClosesAndGoesToTheMoviesPage()
    {
        movieService.DeleteAsync(7, false, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var closed = 0;
        var cut = RenderDialog(onClose: () => { closed++; return Task.CompletedTask; });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        await cut.InvokeAsync(() => cut.Find(".delete-confirm-confirm-btn").Click());

        await movieService.Received(1).DeleteAsync(7, false, Arg.Any<CancellationToken>());
        Assert.Equal(1, closed);
        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task AFailedDelete_KeepsTheDialogOpenWithTheReason_AndStaysOnThePage()
    {
        movieService.DeleteAsync(7, false, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("Refusing to delete."));
        var closed = 0;
        var cut = RenderDialog(onClose: () => { closed++; return Task.CompletedTask; });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        await cut.InvokeAsync(() => cut.Find(".delete-confirm-confirm-btn").Click());

        Assert.Contains("Refusing to delete.", cut.Find(".delete-confirm-backdrop .alert-danger").TextContent);
        Assert.Equal(0, closed);
        Assert.EndsWith("/movies/ABC-123", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task Cancelling_AsksTheParentToClose_WithoutDeleting()
    {
        var closed = 0;
        var cut = RenderDialog(onClose: () => { closed++; return Task.CompletedTask; });

        await cut.InvokeAsync(() => cut.Find(".delete-confirm-cancel-btn").Click());

        Assert.Equal(1, closed);
        await movieService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Fact]
    public async Task ReopeningForgetsTheLastFailure()
    {
        movieService.DeleteAsync(7, false, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("Refusing to delete."));
        var cut = RenderDialog();
        await cut.InvokeAsync(() => cut.Find(".delete-confirm-confirm-btn").Click());
        Assert.NotEmpty(cut.FindAll(".alert-danger"));

        cut.Render(p => p.Add(x => x.Show, false));
        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Empty(cut.FindAll(".alert-danger"));
    }
}
