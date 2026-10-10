using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

/// <summary>The Delete dialog's "Delete files from disk" toggle only appears when there is something to delete.</summary>
public class MovieDeleteFilesOptionTests : BunitContext
{
    private readonly IMovieService movieService = Substitute.For<IMovieService>();

    public MovieDeleteFilesOptionTests() => Services.AddSingleton(movieService);

    private IRenderedComponent<MovieDeleteFilesOption> RenderWith(MovieFolderDeletePreview preview)
    {
        movieService.GetFolderDeletePreviewAsync(7, Arg.Any<CancellationToken>()).Returns(preview);
        return Render<MovieDeleteFilesOption>(p => p.Add(x => x.MovieId, 7));
    }

    [Fact]
    public void NoFolder_HidesTheOption()
    {
        var cut = RenderWith(new MovieFolderDeletePreview(null, [], 0));

        Assert.DoesNotContain("Delete files from disk", cut.Markup);
    }

    [Fact]
    public void EmptyFolder_HidesTheOption()
    {
        var cut = RenderWith(new MovieFolderDeletePreview("/media/ABC-123", [], 0));

        Assert.DoesNotContain("Delete files from disk", cut.Markup);
    }

    [Fact]
    public void FolderWithFiles_ShowsTheOption()
    {
        var cut = RenderWith(new MovieFolderDeletePreview("/media/ABC-123", [new MovieFolderFile("ABC-123.mp4", 10)], 10));

        Assert.Contains("Delete files from disk", cut.Markup);
    }
}
