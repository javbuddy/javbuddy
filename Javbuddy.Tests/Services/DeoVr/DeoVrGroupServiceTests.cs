using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrGroupServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();

    public void Dispose() => dbFactory.Dispose();

    private DeoVrGroupService Service() => new(dbFactory);

    /// <summary>Deletes the seeded "All movies" group, for tests about groups they add themselves.</summary>
    private Task ClearAsync() => Service().DeleteAsync(DeoVrGroup.AllMovies.Id);

    [Fact]
    public async Task AFreshDatabase_StartsWithAnUnfilteredAllMoviesGroup_NewestFirst()
    {
        var group = Assert.Single(await Service().ListAsync());

        Assert.Equal(("All movies", new MovieGridSort("added", true, 0)), (group.Name, group.Sort));
        Assert.Equal(new MovieGridFilter(), group.Filter);
    }

    [Fact]
    public async Task TheAllMoviesGroup_CanBeEditedAndDeletedLikeAnyOther()
    {
        await Service().SaveAsync(DeoVrGroup.AllMovies.Id, "Everything", new MovieGridFilter(Text: "MIDE"), "title", false);
        Assert.Equal(("Everything", "MIDE"), ((await Service().ListAsync())[0].Name, (await Service().ListAsync())[0].Filter.Text));

        await Service().DeleteAsync(DeoVrGroup.AllMovies.Id);

        Assert.Empty(await Service().ListAsync());
    }

    [Fact]
    public async Task SaveAsync_AddsAtTheEnd_AndRoundTripsTheFilterAndSort()
    {
        await ClearAsync();
        var filter = new MovieGridFilter(
            Status: MovieStatus.Got,
            Resolutions: [MovieResolutionFilterOption.FourK],
            Studios: ["S1"],
            Features: [MovieFeatureFilterOption.Favorites],
            NfoDriftKinds: [NfoDriftKind.ExternalEdit],
            ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(150, 160) });

        var first = await Service().SaveAsync(0, "  4K favorites ", filter, "release", false);
        var second = await Service().SaveAsync(0, "Random", new MovieGridFilter(), "random", true);

        var groups = await Service().ListAsync();
        Assert.Equal([first, second], groups.Select(g => g.Id));
        var group = groups[0];
        Assert.Equal("4K favorites", group.Name);
        Assert.Equal(new MovieGridSort("release", false, 0), group.Sort);
        Assert.Equal(MovieStatus.Got, group.Filter.Status);
        Assert.Equal([MovieResolutionFilterOption.FourK], group.Filter.Resolutions!);
        Assert.Equal(["S1"], group.Filter.Studios!);
        Assert.Equal([MovieFeatureFilterOption.Favorites], group.Filter.Features!);
        Assert.Equal([NfoDriftKind.ExternalEdit], group.Filter.NfoDriftKinds!);
        Assert.True(group.Filter.ActorAttributes!.SameAs(filter.ActorAttributes!));
    }

    [Fact]
    public async Task TheStoredFilter_NamesEnumsRatherThanNumbering()
    {
        await ClearAsync();
        await Service().SaveAsync(0, "G", new MovieGridFilter(Features: [MovieFeatureFilterOption.HasScenes]), "added", true);

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Contains("\"HasScenes\"", Assert.Single(db.DeoVrGroups).FilterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_UpdatesAnExistingGroupInPlace()
    {
        await ClearAsync();
        var id = await Service().SaveAsync(0, "Old", new MovieGridFilter(), "added", true);

        await Service().SaveAsync(id, "New", new MovieGridFilter(Text: "MIDE"), "title", false);

        var group = Assert.Single(await Service().ListAsync());
        Assert.Equal((id, "New", "MIDE", "title"), (group.Id, group.Name, group.Filter.Text, group.Sort.Field));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_RejectsABlankName(string name) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SaveAsync(0, name, new MovieGridFilter(), "added", true));

    [Fact]
    public async Task MoveAsync_SwapsNeighbours_AndIgnoresTheEnds()
    {
        await ClearAsync();
        var a = await Service().SaveAsync(0, "A", new MovieGridFilter(), "added", true);
        var b = await Service().SaveAsync(0, "B", new MovieGridFilter(), "added", true);
        var c = await Service().SaveAsync(0, "C", new MovieGridFilter(), "added", true);

        await Service().MoveAsync(c, -1);
        await Service().MoveAsync(a, -1);
        await Service().MoveAsync(b, 1);

        Assert.Equal([a, c, b], (await Service().ListAsync()).Select(g => g.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheGroup()
    {
        await ClearAsync();
        var a = await Service().SaveAsync(0, "A", new MovieGridFilter(), "added", true);
        var b = await Service().SaveAsync(0, "B", new MovieGridFilter(), "added", true);

        await Service().DeleteAsync(a);

        Assert.Equal([b], (await Service().ListAsync()).Select(g => g.Id));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    public void DeserializingABrokenFilter_GivesNoFilter(string json) =>
        Assert.Equal(new MovieGridFilter(), DeoVrGroupFilter.Deserialize(json));

    [Fact]
    public async Task AnUnknownSortField_ReadsAsAdded()
    {
        await ClearAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.DeoVrGroups.Add(new DeoVrGroup { Name = "G", SortField = "bogus" });
            await db.SaveChangesAsync();
        }

        Assert.Equal("added", Assert.Single(await Service().ListAsync()).Sort.Field);
    }
}
