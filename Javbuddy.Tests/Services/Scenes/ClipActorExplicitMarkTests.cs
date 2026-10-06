using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The ExecuteDelete paths and "Apply Rules to Library" mark movies stale themselves.</summary>
public sealed class ClipActorExplicitMarkTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private int movieId, aikaId, sceneId, highlightId, apexId;

    public void Dispose() => factory.Dispose();

    private async Task SeedFreshAsync()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var aika = new Actor { FirstName = "Aika" };
            var movie = new Movie { Code = "EXP-001", MediaDurationSeconds = 3600 };
            db.AddRange(aika, movie);
            await db.SaveChangesAsync();
            (movieId, aikaId) = (movie.Id, aika.Id);
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = aikaId });
            var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
            var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
            var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
            db.AddRange(scene, highlight, apex);
            await db.SaveChangesAsync();
            (sceneId, highlightId, apexId) = (scene.Id, highlight.Id, apex.Id);
            db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = aikaId });
            await db.SaveChangesAsync();
        }
        await using var refresh = await factory.CreateDbContextAsync();
        await ClipActorSync.RefreshStaleAsync(refresh);
    }

    private async Task<bool> IsStaleAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync();
    }

    [Fact]
    public async Task DeleteScene_MarksStale()
    {
        await SeedFreshAsync();
        Assert.True((await new MovieSceneService(factory).DeleteSceneAsync(sceneId)).Success);
        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task RemoveSceneActor_MarksStaleThroughTheInterceptor()
    {
        await SeedFreshAsync();
        await new MovieSceneService(factory).RemoveSceneActorAsync(sceneId, aikaId);
        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task RemoveSceneActor_NotOnTheScene_IsANoOp()
    {
        await SeedFreshAsync();

        var result = await new MovieSceneService(factory).RemoveSceneActorAsync(sceneId, aikaId + 1000);

        Assert.True(result.Success);
        Assert.False(await IsStaleAsync());
    }

    [Fact]
    public async Task DeleteHighlight_MarksStale()
    {
        await SeedFreshAsync();
        Assert.True((await new MovieHighlightService(factory).DeleteHighlightAsync(highlightId)).Success);
        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task DeleteApex_MarksStale()
    {
        await SeedFreshAsync();
        Assert.True((await new MovieApexService(factory).DeleteApexAsync(apexId)).Success);
        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task ApplyRulesToLibrary_MarksEveryMovieStale_AndSignals()
    {
        await SeedFreshAsync();
        var signal = new ClipActorRefreshSignal();

        await new TagRuleService(factory, signal).ApplyReplacementRulesToLibraryAsync();

        Assert.True(await IsStaleAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await signal.WaitAsync(timeout.Token);
    }
}
