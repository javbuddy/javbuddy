using System.Text.Json;
using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Pages.MoviesSections;

public class MovieGridFilterStateTests
{
    [Fact]
    public void ViewState_RoundTripsTheSelection()
    {
        var state = new MovieGridFilterState { Status = MovieStatus.Got, Library = "Main", Text = "abc", SortField = "title", SortDescending = false };
        state.Studios.Add("S1");
        state.Features.Add(MovieFeatureFilterOption.Favorites);
        state.ActorIds.Add(7);

        var restored = new MovieGridFilterState();
        restored.Apply(state.ToViewState());

        Assert.Equal(JsonSerializer.Serialize(state.ToViewState()), JsonSerializer.Serialize(restored.ToViewState()));
    }

    [Fact]
    public void Apply_KeepsListsAnOlderCookieDoesNotCarry()
    {
        var state = new MovieGridFilterState();
        state.Genres.Add("Drama");

        state.Apply(new MoviesViewState(GenreFilter: null));

        Assert.Equal(["Drama"], state.Genres);
    }

    [Fact]
    public void ClearBesidesNavigationTargets_KeepsTextStudioGenreAndPrefix()
    {
        var state = new MovieGridFilterState { Status = MovieStatus.Missing, Text = "abc", CodePrefix = "ABC" };
        state.Studios.Add("S1");
        state.Genres.Add("G1");
        state.Codecs.Add("hevc");

        Assert.True(state.ClearBesidesNavigationTargets());
        Assert.Null(state.Status);
        Assert.Empty(state.Codecs);
        Assert.Equal(("abc", "ABC"), (state.Text, state.CodePrefix));
        Assert.NotEmpty(state.Studios);
        Assert.NotEmpty(state.Genres);
        Assert.False(state.ClearBesidesNavigationTargets());
    }

    [Fact]
    public void ClearBesidesNavigationTargets_WithAnActorTagTarget_LeavesJustThatActorTag()
    {
        var state = new MovieGridFilterState();
        state.ActorTagIds.Add(3);

        Assert.True(state.ClearBesidesNavigationTargets(actorTagTarget: 7));
        Assert.Equal([7], state.ActorTagIds);
        // Already exactly that target (the prerendered pass applied it): nothing changes.
        Assert.False(state.ClearBesidesNavigationTargets(actorTagTarget: 7));
        Assert.True(state.ClearBesidesNavigationTargets());
        Assert.Empty(state.ActorTagIds);
    }

    [Fact]
    public void Clear_ResetsEveryFilterButKeepsTheSort()
    {
        var state = new MovieGridFilterState { Text = "abc", CodePrefix = "ABC", SortField = "title" };
        state.Studios.Add("S1");
        Assert.True(state.HasActiveFilters);

        state.Clear();

        Assert.False(state.HasActiveFilters);
        Assert.Equal("title", state.SortField);
    }

    [Fact]
    public void ViewState_CatalogFilter_RoundTripsThroughTheCookieJson()
    {
        var state = new MoviesViewState(BrowseReleases: true, CatalogFilter: new Javbuddy.Services.R18Dev.R18DevCatalogFilter
        {
            Genres = ["Creampie"],
            MatchAllGenres = true,
            YearFrom = 2020,
            Vr = Javbuddy.Services.R18Dev.R18DevCatalogFlag.Exclude,
            CastSizes = [Javbuddy.Services.R18Dev.R18DevCastSize.Solo],
            Label = new Javbuddy.Services.R18Dev.R18DevCatalogRef(10, "Label B"),
            Status = Javbuddy.Services.R18Dev.R18DevCatalogStatus.Got,
            HidePreviouslyDeleted = false,
        });

        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var restored = System.Text.Json.JsonSerializer.Deserialize<MoviesViewState>(json)!;

        Assert.True(restored.BrowseReleases);
        Assert.Equal(state.CatalogFilter!.Key, restored.CatalogFilter!.Key);
        Assert.DoesNotContain("\"Key\"", json);
    }
}
