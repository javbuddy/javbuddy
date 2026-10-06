using Javbuddy.Components.Pages.ActorsSections;
using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Components.Pages.ActorsSections;

public class ActorListFilterTests
{
    private static readonly ActorCardModel[] Actors =
    [
        new(1, "Aoi", 3, true, HeightCm: 150, CupSize: "B", OwnedCount: 3),
        new(2, "Mana", 0, false, HeightCm: 165, CupSize: "AA", Aliases: ["Mana-chan"]),
        new(3, "Yui", 2, true, IsFavorite: true, IsRetired: true, OwnedCount: 1),
    ];

    private static ActorListFilter CreateFilter() => new() { HeightBounds = ActorCardText.HeightBounds(Actors) };

    private static List<int> Ids(ActorListFilter filter) => filter.Apply(Actors).Select(a => a.Id).ToList();

    [Fact]
    public void Text_MatchesNamesAndAliases()
    {
        var filter = CreateFilter();
        filter.Text = "chan";

        Assert.Equal([2], Ids(filter));
    }

    [Fact]
    public void CycledFilters_StepThroughTheirStates()
    {
        var filter = CreateFilter();

        filter.CycleMovies();
        Assert.Equal([1, 3], Ids(filter));
        filter.CycleMovies();
        Assert.Equal([2], Ids(filter));
        filter.CycleMovies();
        Assert.Equal(ActorMovieFilter.All, filter.Movies);

        filter.CycleStatus();
        filter.CycleStatus();
        Assert.Equal([3], Ids(filter));
        Assert.Equal("Retired only", filter.StatusFilterLabel);
    }

    [Fact]
    public void HeightRange_AtTheBoundsIsNoFilter()
    {
        var filter = CreateFilter();

        filter.SetHeightRange((150, 165));
        Assert.Null(filter.HeightMin);
        Assert.Equal(0, filter.ActiveFilterCount);

        filter.SetHeightRange((160, 165));
        Assert.Equal([2], Ids(filter));
        Assert.Equal(1, filter.ActiveFilterCount);
    }

    [Fact]
    public void CupSizeSort_RanksShorterCupsFirstAndMissingLast()
    {
        var filter = CreateFilter();
        filter.SetSort("cup_size");
        Assert.True(filter.SortDescending);

        filter.SetSort("cup_size");
        Assert.Equal([1, 2, 3], Ids(filter));
    }

    [Fact]
    public void ViewState_RoundTripsTheFilters()
    {
        var filter = CreateFilter();
        filter.Text = "a";
        filter.FavoritesOnly = true;
        filter.CupSizes.Add("B");
        filter.SetSort("age");

        var restored = CreateFilter();
        restored.Apply(filter.ToViewState());

        Assert.Equal(filter.ToViewState() with { CupSizeFilter = null }, restored.ToViewState() with { CupSizeFilter = null });
        Assert.Equal(["B"], restored.CupSizes);
    }

    [Fact]
    public void Clear_KeepsTheSort()
    {
        var filter = CreateFilter();
        filter.FavoritesOnly = true;
        filter.SetSort("height");

        filter.Clear();

        Assert.False(filter.HasActiveFilters);
        Assert.Equal("height", filter.SortField);
    }

    [Fact]
    public void AvailableCupSizes_AddsDataValuesShortestFirst()
    {
        var cups = ActorCardText.AvailableCupSizes(Actors);

        Assert.Equal("AA", cups[ActorPhysicalAttributesHelper.StandardCupSizes.Count]);
    }
}
