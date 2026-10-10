using AngleSharp.Dom;
using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieGenresSectionTests : BunitContext
{
    private const int MovieId = 5, Explicit = 1, Implied = 2, LeftoverActorTag = 3;

    private readonly ITagService tags = Substitute.For<ITagService>();

    public MovieGenresSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        tags.RemoveTagFromMovieAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        tags.RemoveTagFromMovieAndClipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        Services.AddSingleton(tags);
        Services.AddSingleton(new MovieFilterNavigationState());
    }

    private IRenderedComponent<MovieGenresSection> RenderEditing()
    {
        var movie = new Movie { Id = MovieId, Code = "ABC-123" };
        movie.MovieTags.Add(new MovieTag { TagId = Explicit, Tag = new Tag { Id = Explicit, Name = "Solo" }, IsExplicit = true });
        movie.MovieTags.Add(new MovieTag { TagId = Implied, Tag = new Tag { Id = Implied, Name = "Squirt" }, IsExplicit = false, FromClips = true });
        movie.MovieTags.Add(new MovieTag { TagId = LeftoverActorTag, Tag = new Tag { Id = LeftoverActorTag, Name = "Blonde", IsActorTag = true }, IsExplicit = false, FromClips = true });
        var cut = Render<MovieGenresSection>(p => p.Add(x => x.Movie, movie));
        cut.Find(".movie-detail-edit-tags-btn").Click();
        return cut;
    }

    private static IElement Chip(IRenderedComponent<MovieGenresSection> cut, string name) =>
        cut.FindAll(".movie-detail-tag-item").Single(e => e.TextContent.Contains(name));

    [Fact]
    public void WhileEditing_MarksOnlyClipDerivedTagsImplied_WithTheExplanationOnTheChip()
    {
        var cut = RenderEditing();

        var implied = Chip(cut, "Squirt");
        Assert.Contains("movie-detail-tag-item-implied", implied.ClassList);
        Assert.Contains("removing it removes it from those too", implied.GetAttribute("title"));
        Assert.Null(implied.QuerySelector(".movie-detail-badge")!.GetAttribute("title"));

        Assert.DoesNotContain("movie-detail-tag-item-implied", Chip(cut, "Solo").ClassList);
        Assert.DoesNotContain("movie-detail-tag-item-implied", Chip(cut, "Blonde").ClassList);
    }

    [Fact]
    public void RemovingAnImpliedTag_AsksFirst_ThenRemovesItFromTheClips()
    {
        var cut = RenderEditing();

        Chip(cut, "Squirt").QuerySelector(".movie-detail-tag-remove-btn")!.Click();
        tags.DidNotReceiveWithAnyArgs().RemoveTagFromMovieAndClipsAsync(default, default);

        cut.Find(".delete-confirm-confirm-btn").Click();
        tags.Received(1).RemoveTagFromMovieAndClipsAsync(MovieId, Implied, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));
    }

    [Fact]
    public void RemovingALeftoverActorTagLink_RemovesItDirectly()
    {
        var cut = RenderEditing();

        Chip(cut, "Blonde").QuerySelector(".movie-detail-tag-remove-btn")!.Click();

        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));
        tags.Received(1).RemoveTagFromMovieAsync(MovieId, LeftoverActorTag, Arg.Any<CancellationToken>());
        tags.DidNotReceiveWithAnyArgs().RemoveTagFromMovieAndClipsAsync(default, default);
    }
}
