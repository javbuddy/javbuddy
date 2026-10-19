using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class MovieCountFiltersTests : BunitContext
{
    private static readonly MovieCountOptions Full = MovieCountOptions.FromMaximums(maxScenes: 6, maxHighlights: 4, maxApexes: 3);

    private IRenderedComponent<MovieCountFilters> RenderFilters(MovieCountOptions options, MovieCountSelection selection,
        Action<MovieCountSelection>? onChanged = null) =>
        Render<MovieCountFilters>(p => p
            .Add(x => x.Options, options)
            .Add(x => x.Selection, selection)
            .Add(x => x.SelectionChanged, EventCallback.Factory.Create<MovieCountSelection>(this, s => onChanged?.Invoke(s))));

    private static void Expand(IRenderedComponent<MovieCountFilters> cut, string label) =>
        cut.FindAll("button.sort-dropdown-item").First(b => b.TextContent.StartsWith(label, StringComparison.Ordinal)).Click();

    [Fact]
    public void RendersOnlyTheKindsSomeMovieHas()
    {
        var cut = RenderFilters(MovieCountOptions.FromMaximums(maxScenes: 2, maxHighlights: 0, maxApexes: 1), MovieCountSelection.Empty);

        var headers = cut.FindAll("button.sort-dropdown-item").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(2, headers.Count);
        Assert.StartsWith("Scenes", headers[0]);
        Assert.StartsWith("Apexes", headers[1]);
    }

    [Fact]
    public void ShowsANarrowedRangeInItsHeader_WithAnOpenEndAtTheBound()
    {
        var cut = RenderFilters(Full, new MovieCountSelection { Scenes = new IntRange(2, null), Apexes = new IntRange(null, 0) });

        Assert.Contains("Scenes (2–6)", cut.Markup);
        Assert.Contains("Apexes (0)", cut.Markup);
    }

    [Theory]
    [InlineData(1, "6", 2, null)] // top thumb to the end stop: "or more"
    [InlineData(0, "0", null, 4)] // bottom thumb to the end stop: no minimum
    [InlineData(1, "5", 2, 5)]
    public void ThumbsAtTheEndStops_CommitAsNoLimit(int thumb, string value, int? expectedMin, int? expectedMax)
    {
        MovieCountSelection? emitted = null;
        var cut = RenderFilters(Full, new MovieCountSelection { Scenes = new IntRange(2, 4) }, s => emitted = s);
        Expand(cut, "Scenes");

        cut.FindAll(".range-thumb-input")[thumb].Change(value);

        Assert.Equal(new IntRange(expectedMin, expectedMax), emitted!.Scenes);
    }

    [Fact]
    public void EditingOneKind_LeavesTheOthersAsSelected()
    {
        MovieCountSelection? emitted = null;
        var cut = RenderFilters(Full, new MovieCountSelection { Scenes = new IntRange(1, null) }, s => emitted = s);
        Expand(cut, "Highlights");

        cut.FindAll(".range-thumb-input")[1].Change("2");

        Assert.Equal(new IntRange(1, null), emitted!.Scenes);
        Assert.Equal(new IntRange(null, 2), emitted.Highlights);
        Assert.Equal(IntRange.None, emitted.Apexes);
    }
}
