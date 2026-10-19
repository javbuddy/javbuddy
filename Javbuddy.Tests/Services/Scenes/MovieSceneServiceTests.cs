using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

public class MovieSceneServiceTests
{
    private static async Task<int> SeedMovieAsync(TestDbContextFactory factory, double? durationSeconds = 900, string code = "ABC-123")
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, MediaDurationSeconds = durationSeconds };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task AddScene_PersistsTrimmedTitleAndReturnsResolvedList()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);

        var second = await service.AddSceneAsync(movieId, 300, null, "  Bath  ");
        var first = await service.AddSceneAsync(movieId, 0, 120, "   ");

        Assert.True(second.Success);
        Assert.True(first.Success);
        var scenes = await service.GetScenesAsync(movieId);
        Assert.Collection(scenes,
            s =>
            {
                Assert.Equal(first.SceneId, s.Id);
                Assert.Null(s.Title);
                Assert.Equal("Scene 1", s.DisplayTitle);
                Assert.Equal(120d, s.EffectiveEndSeconds);
            },
            s =>
            {
                Assert.Equal("Bath", s.Title);
                Assert.Equal(2, s.Position);
                Assert.Null(s.EndSeconds);
                Assert.Equal(900d, s.EffectiveEndSeconds);
            });
    }

    [Fact]
    public async Task AddScene_RejectsOverlapAndOutOfBounds_WithoutSaving()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);
        await service.AddSceneAsync(movieId, 0, 200, null);

        var overlap = await service.AddSceneAsync(movieId, 100, null, null);
        var pastEnd = await service.AddSceneAsync(movieId, 800, 901, null);

        Assert.False(overlap.Success);
        Assert.Equal("Overlaps \"Scene 1\".", overlap.ErrorMessage);
        Assert.False(pastEnd.Success);
        Assert.Single(await service.GetScenesAsync(movieId));
    }

    [Fact]
    public async Task AddScene_RejectsUnknownMovieAndOverlongTitle()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);

        Assert.Equal("Movie not found.", (await service.AddSceneAsync(movieId + 1, 0, null, null)).ErrorMessage);
        Assert.False((await service.AddSceneAsync(movieId, 0, null, new string('x', 201))).Success);
    }

    [Fact]
    public async Task UpdateScene_IgnoresItsOwnOldRangeButRejectsNeighbourOverlap()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);
        var scene = (await service.AddSceneAsync(movieId, 0, 100, null)).SceneId!.Value;
        await service.AddSceneAsync(movieId, 300, null, "Next");

        var widened = await service.UpdateSceneAsync(scene, 0, 250, "Opening");
        var overlapping = await service.UpdateSceneAsync(scene, 0, 350, null);

        Assert.True(widened.Success);
        Assert.Equal("Overlaps \"Next\".", overlapping.ErrorMessage);
        var stored = (await service.GetScenesAsync(movieId))[0];
        Assert.Equal("Opening", stored.Title);
        Assert.Equal(250d, stored.EndSeconds);
    }

    [Fact]
    public async Task UpdateAndDelete_UnknownScene_Fail()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieSceneService(factory);

        Assert.Equal("Scene not found.", (await service.UpdateSceneAsync(42, 0, null, null)).ErrorMessage);
        Assert.Equal("Scene not found.", (await service.DeleteSceneAsync(42)).ErrorMessage);
    }

    [Fact]
    public async Task DeleteScene_RemovesOnlyThatScene()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);
        var first = (await service.AddSceneAsync(movieId, 0, null, null)).SceneId!.Value;
        await service.AddSceneAsync(movieId, 300, null, null);

        Assert.True((await service.DeleteSceneAsync(first)).Success);

        var remaining = Assert.Single(await service.GetScenesAsync(movieId));
        Assert.Equal(300d, remaining.StartSeconds);
        Assert.Equal("Scene 1", remaining.DisplayTitle);
    }

    [Fact]
    public async Task DeletingMovie_CascadesToItsScenes()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var otherMovieId = await SeedMovieAsync(factory, code: "XYZ-001");
        var service = new MovieSceneService(factory);
        await service.AddSceneAsync(movieId, 0, null, null);
        await service.AddSceneAsync(otherMovieId, 0, null, null);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Remove(await db.Movies.SingleAsync(m => m.Id == movieId));
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.Equal([otherMovieId], await readDb.Scenes.Select(s => s.MovieId).ToListAsync());
    }

    [Fact]
    public async Task UnknownDuration_LastSceneHasNoEffectiveEnd()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory, durationSeconds: null);
        var service = new MovieSceneService(factory);

        Assert.True((await service.AddSceneAsync(movieId, 5000, null, null)).Success);

        Assert.Null(Assert.Single(await service.GetScenesAsync(movieId)).EffectiveEndSeconds);
    }

    [Fact]
    public async Task ToggleFavorite_SetsAndClearsFavoritedAt()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);
        var sceneId = (await service.AddSceneAsync(movieId, 0, null, null)).SceneId!.Value;

        Assert.True(await service.ToggleFavoriteAsync(sceneId));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.NotNull((await db.Scenes.SingleAsync()).FavoritedAt);
        }
        Assert.True((await service.GetScenesAsync(movieId))[0].IsFavorite);

        Assert.False(await service.ToggleFavoriteAsync(sceneId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Null((await verify.Scenes.SingleAsync()).FavoritedAt);
        Assert.False(await service.ToggleFavoriteAsync(9999));
    }

    [Fact]
    public async Task HasFavoriteSceneFilter_MatchesMoviesWithAFavoriteScene()
    {
        using var factory = new TestDbContextFactory();
        var withFavorite = await SeedMovieAsync(factory, code: "FAV-001");
        var without = await SeedMovieAsync(factory, code: "NOF-001");
        var service = new MovieSceneService(factory);
        await service.ToggleFavoriteAsync((await service.AddSceneAsync(withFavorite, 0, null, null)).SceneId!.Value);
        await service.AddSceneAsync(without, 0, null, null);

        var grid = new MovieGridQueryService(factory);
        var result = await grid.GetRangeAsync(
            new MovieGridFilter(Features: [Javbuddy.Components.Shared.MovieFeatureFilterOption.HasFavoriteScene]),
            new MovieGridSort("title", false, 1), 0, 10);

        Assert.Equal(["FAV-001"], result.Select(m => m.Code));
    }

    // Movies: ONE has 1 scene, TWO has 3 scenes + 1 apex, NONE has nothing, HIGH has 2 highlights.
    private static async Task<MovieGridQueryService> SeedCountsAsync(TestDbContextFactory factory)
    {
        var scenes = new MovieSceneService(factory);
        var one = await SeedMovieAsync(factory, code: "ONE-001");
        var two = await SeedMovieAsync(factory, code: "TWO-001");
        await SeedMovieAsync(factory, code: "NONE-001");
        var high = await SeedMovieAsync(factory, code: "HIGH-001");
        await scenes.AddSceneAsync(one, 0, null, null);
        foreach (var start in new[] { 0, 100, 200 }) await scenes.AddSceneAsync(two, start, null, null);

        await using var db = await factory.CreateDbContextAsync();
        db.MovieApexes.Add(new MovieApex { MovieId = two, Seconds = 50 });
        db.MovieHighlights.AddRange(
            new MovieHighlight { MovieId = high, StartSeconds = 10, EndSeconds = 20 },
            new MovieHighlight { MovieId = high, StartSeconds = 30, EndSeconds = 40 });
        await db.SaveChangesAsync();
        return new MovieGridQueryService(factory);
    }

    private static async Task<string[]> CodesAsync(MovieGridQueryService grid, MovieCountSelection counts) =>
        [.. (await grid.GetRangeAsync(new MovieGridFilter(Counts: counts), new MovieGridSort("title", false, 1), 0, 10)).Select(m => m.Code!)];

    [Theory]
    [InlineData(null, null, new[] { "HIGH-001", "NONE-001", "ONE-001", "TWO-001" })]
    [InlineData(1, null, new[] { "ONE-001", "TWO-001" })]
    [InlineData(0, 0, new[] { "HIGH-001", "NONE-001" })]
    [InlineData(null, 1, new[] { "HIGH-001", "NONE-001", "ONE-001" })]
    [InlineData(2, 3, new[] { "TWO-001" })]
    public async Task SceneCountFilter_KeepsMoviesWithinTheRange(int? min, int? max, string[] expected)
    {
        using var factory = new TestDbContextFactory();
        var grid = await SeedCountsAsync(factory);

        // A range whose open end is null is what the slider's end stop saves.
        Assert.Equal(expected, await CodesAsync(grid, new MovieCountSelection { Scenes = new IntRange(min, max) }));
    }

    [Fact]
    public async Task HighlightAndApexCountFilters_AreAppliedTogether()
    {
        using var factory = new TestDbContextFactory();
        var grid = await SeedCountsAsync(factory);

        Assert.Equal(["HIGH-001"], await CodesAsync(grid, new MovieCountSelection { Highlights = new IntRange(2, null) }));
        Assert.Equal(["TWO-001"], await CodesAsync(grid, new MovieCountSelection { Apexes = new IntRange(1, null) }));
        Assert.Empty(await CodesAsync(grid, new MovieCountSelection { Highlights = new IntRange(1, null), Apexes = new IntRange(1, null) }));
    }

    [Fact]
    public async Task Summary_OffersZeroToTheMostAnyMovieHas()
    {
        using var factory = new TestDbContextFactory();
        var grid = await SeedCountsAsync(factory);

        var counts = (await grid.GetSummaryAsync(new MovieGridFilter())).Counts;

        Assert.Equal(new RangeBounds(0, 3), counts.Scenes);
        Assert.Equal(new RangeBounds(0, 2), counts.Highlights);
        Assert.Equal(new RangeBounds(0, 1), counts.Apexes);
    }

    [Fact]
    public async Task Summary_OffersNoCountRangesForAnEmptyLibrary()
    {
        using var factory = new TestDbContextFactory();

        Assert.False((await new MovieGridQueryService(factory).GetSummaryAsync(new MovieGridFilter())).Counts.HasAny);
    }

    [Fact]
    public async Task SetHiddenFromOverview_IsStoredAndReported()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieSceneService(factory);
        var sceneId = (await service.AddSceneAsync(movieId, 0, null, "Intro")).SceneId!.Value;
        Assert.False((await service.GetScenesAsync(movieId))[0].IsHiddenFromOverview);

        Assert.True((await service.SetHiddenFromOverviewAsync(sceneId, true)).Success);
        Assert.True((await service.GetScenesAsync(movieId))[0].IsHiddenFromOverview);

        Assert.True((await service.SetHiddenFromOverviewAsync(sceneId, false)).Success);
        Assert.False((await service.GetScenesAsync(movieId))[0].IsHiddenFromOverview);
        Assert.Equal("Scene not found.", (await service.SetHiddenFromOverviewAsync(9999, true)).ErrorMessage);
    }

    [Fact]
    public async Task SceneCountFilter_CountsHiddenScenes()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory, code: "HID-001");
        var service = new MovieSceneService(factory);
        await service.SetHiddenFromOverviewAsync((await service.AddSceneAsync(movieId, 0, null, null)).SceneId!.Value, true);

        var result = await new MovieGridQueryService(factory).GetRangeAsync(
            new MovieGridFilter(Counts: new MovieCountSelection { Scenes = new IntRange(1, null) }),
            new MovieGridSort("title", false, 1), 0, 10);

        Assert.Equal(["HID-001"], result.Select(m => m.Code));
    }
}
