using Javbuddy.Models;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The stored effective actors' schema: new movies start stale, and rows cascade away
/// with their clip and with the cast link they point at.</summary>
public sealed class ClipEffectiveActorModelTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<(int MovieId, int ActorId, int SceneId)> SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Aika" };
        var movie = new Movie { Code = "EFF-001", MediaDurationSeconds = 3600 };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
        var scene = new Scene { MovieId = movie.Id, StartSeconds = 0 };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        db.SceneEffectiveActors.Add(new SceneEffectiveActor { SceneId = scene.Id, MovieId = movie.Id, ActorId = actor.Id });
        await db.SaveChangesAsync();
        return (movie.Id, actor.Id, scene.Id);
    }

    [Fact]
    public async Task NewMovie_StartsStale()
    {
        var (movieId, _, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync());
    }

    [Fact]
    public async Task DeletingScene_CascadesItsEffectiveActors()
    {
        var (_, _, sceneId) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.Scenes.Where(s => s.Id == sceneId).ExecuteDeleteAsync();
        Assert.Empty(await db.SceneEffectiveActors.ToListAsync());
    }

    [Fact]
    public async Task LeavingCast_CascadesEffectiveActors()
    {
        var (movieId, actorId, _) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.MovieActors.Where(ma => ma.MovieId == movieId && ma.ActorId == actorId).ExecuteDeleteAsync();
        Assert.Empty(await db.SceneEffectiveActors.ToListAsync());
    }
}
