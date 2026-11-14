using Bunit;
using Javbuddy.Components.Pages.ActorDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.ActorDetailSections;

public class ActorMoviesSectionTests : InteractiveBunitContext
{
    private const string ScrollModule = "./Components/Pages/ActorDetailSections/ActorMoviesSection.razor.js";

    private readonly IMovieService movieService = Substitute.For<IMovieService>();
    private readonly BunitJSModuleInterop module;

    public ActorMoviesSectionTests()
    {
        Services.AddSingleton(movieService);
        Services.AddSingleton(TimeProvider.System);
        module = JSInterop.SetupModule(ScrollModule);
        module.Mode = JSRuntimeMode.Loose;
    }

    private static Movie MovieOf(int id, MovieStatus status = MovieStatus.Got, string? studio = null) =>
        new() { Id = id, Code = $"AAA-{id:000}", Status = status, MetaStudio = studio };

    private IRenderedComponent<ActorMoviesSection> RenderSection(List<Movie> movies, string routeName = "Mikami Yua", int refreshToken = 0) =>
        Render<ActorMoviesSection>(p => p
            .Add(c => c.RouteName, routeName)
            .Add(c => c.Movies, movies)
            .Add(c => c.RefreshToken, refreshToken));

    private static void ShowMissing(IRenderedComponent<ActorMoviesSection> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (")).Click();

    [Fact]
    public void WithoutMovies_KeepsTheCardWithItsEmptyState_AndNoToolbar()
    {
        var cut = RenderSection([]);

        Assert.Equal("Movies (0)", cut.Find("section.actor-detail-movies h2.card-title").TextContent);
        Assert.Contains("No movies in your library are matched to this actor yet.", cut.Markup);
        Assert.Empty(cut.FindAll(".poster-card"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Missing ("));
    }

    [Fact]
    public void ShowsEveryMovieNewestFirst_LinkingBackToTheActor()
    {
        var older = MovieOf(1);
        older.MetaReleaseDate = new DateTime(2020, 1, 1);
        var newer = MovieOf(2);
        newer.MetaReleaseDate = new DateTime(2024, 1, 1);

        var cut = RenderSection([older, newer]);

        var links = cut.FindAll(".poster-title").Select(t => t.Closest("a")!.GetAttribute("href")).ToList();
        Assert.Equal(["/movies/AAA-002?actor=Mikami%20Yua", "/movies/AAA-001?actor=Mikami%20Yua"], links);
    }

    [Fact]
    public void AStatusFilterNarrowsTheGrid_ButTheHeadingKeepsTheTotal()
    {
        var cut = RenderSection([MovieOf(1, MovieStatus.Missing), MovieOf(2)]);

        ShowMissing(cut);

        Assert.Single(cut.FindAll(".poster-card"));
        Assert.Equal("Movies (2)", cut.Find("h2.card-title").TextContent);
    }

    [Fact]
    public void WhenNoMovieMatchesTheFilters_OffersToResetThem()
    {
        var cut = RenderSection([MovieOf(1, MovieStatus.Got), MovieOf(2)]);
        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (")).Click();
        Assert.Contains("No movies match the active filters.", cut.Markup);

        cut.Find("button.btn-link").Click();

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
    }

    [Fact]
    public void ANewRouteName_ClearsTheFilters()
    {
        var movies = new List<Movie> { MovieOf(1, MovieStatus.Missing), MovieOf(2) };
        var cut = RenderSection(movies);
        ShowMissing(cut);
        Assert.Single(cut.FindAll(".poster-card"));

        cut.Render(p => p.Add(c => c.RouteName, "Other Actress").Add(c => c.Movies, movies));

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
    }

    [Fact]
    public void ANewMovieList_KeepsTheFilters_AndShowsTheNewMovies()
    {
        var cut = RenderSection([MovieOf(1, MovieStatus.Missing), MovieOf(2)]);
        ShowMissing(cut);

        cut.Render(p => p.Add(c => c.RouteName, "Mikami Yua").Add(c => c.Movies, new List<Movie> { MovieOf(3, MovieStatus.Missing), MovieOf(4, MovieStatus.Missing), MovieOf(5) }));

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
        Assert.Equal("Movies (3)", cut.Find("h2.card-title").TextContent);
    }

    [Fact]
    public void ChangingAMovieInPlace_ShowsAfterTheRefreshTokenChanges()
    {
        var movies = new List<Movie> { MovieOf(1, MovieStatus.Missing), MovieOf(2) };
        var cut = RenderSection(movies);
        ShowMissing(cut);
        Assert.Single(cut.FindAll(".poster-card"));

        movies[1].Status = MovieStatus.Missing;
        cut.Render(p => p.Add(c => c.RouteName, "Mikami Yua").Add(c => c.Movies, movies).Add(c => c.RefreshToken, 1));

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
    }

    [Fact]
    public void TogglingAHeart_StoresItThroughTheMovieService_AndShowsTheReturnedState()
    {
        var movie = MovieOf(1);
        movieService.ToggleFavoriteAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderSection([movie]);

        cut.Find(".poster-favorite").Click();

        cut.WaitForAssertion(() => Assert.True(movie.IsFavorite));
        Assert.NotNull(movie.FavoritedAt);
    }

    [Fact]
    public void TracksTheScrollPositionPerActor_OnceTheFilmographyRenders()
    {
        var cut = RenderSection([MovieOf(1)]);

        cut.WaitForAssertion(() => Assert.Contains(module.Invocations, i => i.Identifier == "trackScroll"));
        Assert.Equal("javbuddy-actor-detail-scroll-Mikami Yua", Assert.Single(module.Invocations, i => i.Identifier == "trackScroll").Arguments[0]);
    }

    [Fact]
    public void WhenNotInteractive_ShowsTheSkeletonInsteadOfThePosters()
    {
        Interactive = false;

        var cut = RenderSection([MovieOf(1), MovieOf(2)]);

        Assert.Equal("Movies (2)", cut.Find("section.actor-detail-movies h2.card-title").TextContent);
        Assert.NotEmpty(cut.FindAll(".skeleton-card"));
        Assert.Empty(cut.FindAll(".poster-card"));
    }
}
