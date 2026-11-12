using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class RangeFilterRowsTests : BunitContext
{
    private sealed record Sel(IntRange A, IntRange B);

    private static readonly IReadOnlyList<RangeRow<Sel>> Rows =
    [
        new("Alpha", "kg", new RangeBounds(0, 10), new IntRange(2, null), (s, r) => s with { A = r }),
        new("Hidden", "", null, IntRange.None, (s, _) => s),
        new("Beta", "", new RangeBounds(5, 9), IntRange.None, (s, r) => s with { B = r }),
    ];

    [Fact]
    public void RendersASliderPerRowWithBounds_ShowingTheAppliedRange()
    {
        var cut = Render<RangeFilterRows<Sel>>(p => p.Add(x => x.Rows, Rows).Add(x => x.Selection, new Sel(new IntRange(2, null), IntRange.None)));

        var headers = cut.FindAll("button.sort-dropdown-item").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(2, headers.Count);
        Assert.Contains("Alpha (2–10 kg)", headers[0]);
        Assert.Equal("Beta", headers[1].Replace("▼", "").Trim());
    }

    [Fact]
    public void CommittingARange_RaisesAModifiedCopy_WithEndStopsAsOpenEnds()
    {
        Sel? changed = null;
        var original = new Sel(IntRange.None, IntRange.None);
        var cut = Render<RangeFilterRows<Sel>>(p => p
            .Add(x => x.Rows, Rows)
            .Add(x => x.Selection, original)
            .Add(x => x.SelectionChanged, EventCallback.Factory.Create<Sel>(this, s => changed = s)));

        cut.FindAll("button.sort-dropdown-item")[1].Click();
        var thumbs = cut.FindAll("input[type=range]");
        thumbs[0].Change("5");
        thumbs[1].Change("7");

        Assert.NotNull(changed);
        Assert.Equal(new IntRange(null, 7), changed.B);
        Assert.Equal(IntRange.None, changed.A);
    }

    [Fact]
    public void ToIntRange_TurnsAThumbAtItsEndStopIntoAnOpenEnd()
    {
        var bounds = new RangeBounds(10, 20);

        Assert.Equal(new IntRange(null, null), RangeCommit.ToIntRange((10, 20), bounds));
        Assert.Equal(new IntRange(12, 15), RangeCommit.ToIntRange((12, 15), bounds));
        Assert.Equal(new IntRange(null, 15), RangeCommit.ToIntRange((8, 15), bounds));
        Assert.Equal(new IntRange(12, null), RangeCommit.ToIntRange((12, 25), bounds));
    }
}
