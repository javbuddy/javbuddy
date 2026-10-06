using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Pages.MoviesSections;

public class MovieGridWindowTests
{
    private readonly List<(int Skip, int Take)> fetches = [];
    private int idOffset;

    private MovieGridWindow CreateWindow(int filteredCount) => new(FetchAsync) { FilteredCount = filteredCount };

    // Movie at offset i has Id i + idOffset, so shifting idOffset simulates rows inserted ahead.
    private Task<List<Movie>> FetchAsync(int skip, int take)
    {
        fetches.Add((skip, take));
        return Task.FromResult(Enumerable.Range(skip, take).Select(i => new Movie { Id = i + idOffset }).ToList());
    }

    [Fact]
    public async Task EnsureAsync_RangeAlreadyLoaded_DoesNothing()
    {
        var window = CreateWindow(100);
        await window.EnsureAsync(0, 20);
        fetches.Clear();

        var changed = await window.EnsureAsync(5, 10);

        Assert.False(changed);
        Assert.Empty(fetches);
    }

    [Fact]
    public async Task EnsureAsync_OverlappingRange_FetchesOnlyTheMissingPartAndReusesInstances()
    {
        var window = CreateWindow(100);
        await window.EnsureAsync(0, 20);
        var reused = window.Visible[10];
        fetches.Clear();

        var changed = await window.EnsureAsync(10, 20);

        Assert.True(changed);
        Assert.Equal([(20, 10)], fetches);
        Assert.Equal(10, window.Start);
        Assert.Same(reused, window.Visible[0]);
        Assert.Equal(Enumerable.Range(10, 20), window.Visible.Select(m => m.Id));
    }

    [Fact]
    public async Task EnsureAsync_AfterDataChanged_RefetchesTheWholeRange()
    {
        var window = CreateWindow(100);
        await window.EnsureAsync(0, 20);
        window.MarkDataChanged();
        fetches.Clear();

        await window.EnsureAsync(10, 20);

        Assert.Equal([(10, 20)], fetches);
    }

    [Fact]
    public async Task EnsureAsync_MergeWithDuplicateIds_RefetchesTheWholeRange()
    {
        var window = CreateWindow(100);
        await window.EnsureAsync(0, 20);
        // Rows inserted ahead without a change notification: offsets 20.. now hold Ids 15..
        idOffset = -5;
        fetches.Clear();

        await window.EnsureAsync(10, 20);

        Assert.Equal([(20, 10), (10, 20)], fetches);
        Assert.Equal(window.Visible.Count, window.Visible.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public async Task EnsureAsync_AlignsStartToRowAndClampsToFilteredCount()
    {
        var window = CreateWindow(50);
        window.Columns = 6;

        await window.EnsureAsync(45, 20);

        Assert.Equal(42, window.Start);
        Assert.Equal(8, window.Visible.Count);
    }

    [Fact]
    public async Task EnsureAsync_WhenReplacedMidFetch_DropsTheStaleResult()
    {
        MovieGridWindow? window = null;
        window = new MovieGridWindow((skip, take) =>
        {
            window!.BeginReplace();
            return FetchAsync(skip, take);
        })
        { FilteredCount = 100 };

        var changed = await window.EnsureAsync(0, 20);

        Assert.False(changed);
        Assert.Empty(window.Visible);
    }

    [Fact]
    public async Task EnsureAsync_EmptyList_ClearsTheWindow()
    {
        var window = CreateWindow(100);
        await window.EnsureAsync(0, 20);
        window.FilteredCount = 0;

        Assert.True(await window.EnsureAsync(0, 20));
        Assert.Empty(window.Visible);
        Assert.Equal(0, window.Start);
        Assert.False(await window.EnsureAsync(0, 20));
    }
}
