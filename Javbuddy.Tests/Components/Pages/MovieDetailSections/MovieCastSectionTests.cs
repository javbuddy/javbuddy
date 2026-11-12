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

    public MovieCastSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        actorTags.GetMovieTagsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        actorTags.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(actorTags);
        Services.AddSingleton(Substitute.For<IMovieService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Actors.IActorService>());
        Services.AddSingleton(Substitute.For<ITagService>());
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
}
