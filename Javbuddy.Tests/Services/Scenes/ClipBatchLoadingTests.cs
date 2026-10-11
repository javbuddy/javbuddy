using System.Text.Json;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The scene wall loads a whole page's movies through the *ForMoviesAsync methods; each movie's
/// result must be exactly what the single-movie method returns, since Movie Detail still uses it.</summary>
public sealed class ClipBatchLoadingTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly MovieSceneService scenes;
    private readonly MovieHighlightService highlights;
    private readonly MovieApexService apexes;

    public ClipBatchLoadingTests()
    {
        var clipTags = new ClipTagSyncService(factory, Substitute.For<INfoSyncService>());
        scenes = new MovieSceneService(factory, clipTags: clipTags);
        highlights = new MovieHighlightService(factory, clipTags: clipTags);
        apexes = new MovieApexService(factory, clipTags: clipTags);
    }

    public void Dispose() => factory.Dispose();

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    /// <summary>Three movies with different casts, clips, own actors and tags, plus one with no clips at all.</summary>
    private async Task<int[]> SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
        var aika = new Actor { FirstName = "Aika" };
        var mei = new Actor { FirstName = "Mei" };
        var tagA = new Tag { Name = "Alpha" };
        var tagB = new Tag { Name = "Beta" };
        var movies = new[]
        {
            new Movie { Code = "AAA-001", MediaDurationSeconds = 3600 },
            new Movie { Code = "BBB-002", MediaDurationSeconds = 1800 },
            new Movie { Code = "CCC-003", MediaDurationSeconds = 900 },
            new Movie { Code = "DDD-004", MediaDurationSeconds = 600 },
        };
        db.AddRange(yua, aika, mei, tagA, tagB);
        db.AddRange(movies);
        await db.SaveChangesAsync();
        db.MovieActors.AddRange(
            new MovieActor { MovieId = movies[0].Id, ActorId = yua.Id }, new MovieActor { MovieId = movies[0].Id, ActorId = aika.Id },
            new MovieActor { MovieId = movies[1].Id, ActorId = mei.Id },
            new MovieActor { MovieId = movies[2].Id, ActorId = aika.Id });
        await db.SaveChangesAsync();
        var ids = movies.Select(m => m.Id).ToArray();

        // Movie 0: a scene with its own actor and tag, two overlapping highlights, apexes inside them.
        var s0 = await scenes.AddSceneAsync(ids[0], 0, 1200, "Opening");
        await scenes.AddSceneAsync(ids[0], 1200, null, null);
        await scenes.AddSceneActorAsync(s0.SceneId!.Value, yua.Id);
        await scenes.AddSceneTagAsync(s0.SceneId!.Value, tagA.Id);
        await highlights.AddHighlightAsync(ids[0], 100, 400, "Peak", [tagB.Id], [aika.Id]);
        await highlights.AddHighlightAsync(ids[0], 300, 500, null);
        await apexes.AddApexAsync(ids[0], 350, [tagA.Id], [yua.Id]);
        await apexes.AddApexAsync(ids[0], 1500, []);

        // Movie 1: apexes only, a solo cast.
        await apexes.AddApexAsync(ids[1], 60, [tagB.Id]);

        // Movie 2: scenes and a highlight inheriting the cast.
        await scenes.AddSceneAsync(ids[2], 0, 400, "Only");
        await highlights.AddHighlightAsync(ids[2], 50, 90, null);

        // Movie 3: nothing.
        return ids;
    }

    [Fact]
    public async Task GetScenesForMovies_MatchesTheSingleMovieResultForEveryMovie()
    {
        var ids = await SeedAsync();

        var batch = await scenes.GetScenesForMoviesAsync(ids);

        Assert.Equal(ids.Order(), batch.Keys.Order());
        foreach (var id in ids)
        {
            Assert.Equal(Json(await scenes.GetScenesAsync(id)), Json(batch[id]));
        }
        Assert.NotEmpty(batch[ids[0]]);
        Assert.Empty(batch[ids[3]]);
    }

    [Fact]
    public async Task GetHighlightsForMovies_MatchesTheSingleMovieResultForEveryMovie()
    {
        var ids = await SeedAsync();

        var batch = await highlights.GetHighlightsForMoviesAsync(ids);

        Assert.Equal(ids.Order(), batch.Keys.Order());
        foreach (var id in ids)
        {
            Assert.Equal(Json(await highlights.GetHighlightsAsync(id)), Json(batch[id]));
        }
        Assert.Equal(2, batch[ids[0]].Count);
        Assert.Empty(batch[ids[1]]);
    }

    [Fact]
    public async Task GetApexesForMovies_MatchesTheSingleMovieResultForEveryMovie()
    {
        var ids = await SeedAsync();

        var batch = await apexes.GetApexesForMoviesAsync(ids);

        Assert.Equal(ids.Order(), batch.Keys.Order());
        foreach (var id in ids)
        {
            Assert.Equal(Json(await apexes.GetApexesAsync(id)), Json(batch[id]));
        }
        Assert.Equal(2, batch[ids[0]].Count);
        Assert.Empty(batch[ids[3]]);
    }

    [Fact]
    public async Task ForMovies_KeepsEachMoviesPositionsAndCastCountSeparate()
    {
        var ids = await SeedAsync();

        // The second movie's only apex is its own "Apex 1" with a one-actor cast, not movie 0's third or its two-actor cast.
        var apexBatch = await apexes.GetApexesForMoviesAsync(ids);
        var solo = Assert.Single(apexBatch[ids[1]]);
        Assert.Equal(1, solo.Position);
        Assert.Equal(1, solo.CastCount);
        Assert.All(apexBatch[ids[0]], a => Assert.Equal(2, a.CastCount));

        var sceneBatch = await scenes.GetScenesForMoviesAsync(ids);
        Assert.Equal([1, 2], sceneBatch[ids[0]].Select(s => s.Position));
        Assert.Equal([1], sceneBatch[ids[2]].Select(s => s.Position));
    }

    [Fact]
    public async Task ForMovies_AcceptsDuplicateAndUnknownIds_AndNoIds()
    {
        var ids = await SeedAsync();
        const int unknown = 9999;

        var sceneBatch = await scenes.GetScenesForMoviesAsync([ids[0], ids[0], unknown]);
        var highlightBatch = await highlights.GetHighlightsForMoviesAsync([ids[0], ids[0], unknown]);
        var apexBatch = await apexes.GetApexesForMoviesAsync([ids[0], ids[0], unknown]);

        Assert.Equal([ids[0], unknown], sceneBatch.Keys.Order());
        Assert.Empty(sceneBatch[unknown]);
        Assert.Equal([ids[0], unknown], highlightBatch.Keys.Order());
        Assert.Empty(highlightBatch[unknown]);
        Assert.Equal([ids[0], unknown], apexBatch.Keys.Order());
        Assert.Empty(apexBatch[unknown]);
        Assert.Empty(await scenes.GetScenesForMoviesAsync([]));
        Assert.Empty(await highlights.GetHighlightsForMoviesAsync([]));
        Assert.Empty(await apexes.GetApexesForMoviesAsync([]));
    }
}
