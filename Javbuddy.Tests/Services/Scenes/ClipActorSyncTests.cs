using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>ClipActorSync keeps the stored effective actors equal to ClipActors.</summary>
public sealed class ClipActorSyncTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private int movieId, aikaId, rinId, sceneId, highlightId, apexId;

    public void Dispose() => factory.Dispose();

    /// <summary>Cast Aika, Rin. Scene 0– with own actor Aika; highlight 10–100 inheriting the scene (Aika);
    /// apex 50 with own actor Rin.</summary>
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var aika = new Actor { FirstName = "Aika" };
        var rin = new Actor { FirstName = "Rin" };
        var movie = new Movie { Code = "SYNC-001", MediaDurationSeconds = 3600 };
        db.AddRange(aika, rin, movie);
        await db.SaveChangesAsync();
        (movieId, aikaId, rinId) = (movie.Id, aika.Id, rin.Id);
        db.MovieActors.AddRange(new MovieActor { MovieId = movieId, ActorId = aikaId }, new MovieActor { MovieId = movieId, ActorId = rinId });
        var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
        var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
        db.AddRange(scene, highlight, apex);
        await db.SaveChangesAsync();
        (sceneId, highlightId, apexId) = (scene.Id, highlight.Id, apex.Id);
        db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = aikaId });
        db.ApexActors.Add(new ApexActor { ApexId = apexId, MovieId = movieId, ActorId = rinId });
        await db.SaveChangesAsync();
    }

    private async Task<(List<(int, int)> Scenes, List<(int, int)> Highlights, List<(int, int)> Apexes, bool Stale)> LoadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (
            (await db.SceneEffectiveActors.Select(r => new { r.SceneId, r.ActorId }).ToListAsync()).Select(r => (r.SceneId, r.ActorId)).Order().ToList(),
            (await db.HighlightEffectiveActors.Select(r => new { r.HighlightId, r.ActorId }).ToListAsync()).Select(r => (r.HighlightId, r.ActorId)).Order().ToList(),
            (await db.ApexEffectiveActors.Select(r => new { r.ApexId, r.ActorId }).ToListAsync()).Select(r => (r.ApexId, r.ActorId)).Order().ToList(),
            await db.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync());
    }

    private async Task<int> RefreshStaleAsync(Func<AppDbContext, int, CancellationToken, Task<bool>>? refresh = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await ClipActorSync.RefreshStaleAsync(db, refresh: refresh);
    }

    [Fact]
    public async Task RefreshStale_StoresWhatClipActorsComputes_AndClearsTheFlag()
    {
        await SeedAsync();

        Assert.Equal(1, await RefreshStaleAsync());

        var (scenes, highlights, apexes, stale) = await LoadAsync();
        Assert.Equal([(sceneId, aikaId)], scenes);
        Assert.Equal([(highlightId, aikaId)], highlights);
        Assert.Equal([(apexId, rinId)], apexes);
        Assert.False(stale);
    }

    [Fact]
    public async Task RefreshStale_SecondCall_ChangesNothing()
    {
        await SeedAsync();
        await RefreshStaleAsync();
        var before = await LoadAsync();

        Assert.Equal(0, await RefreshStaleAsync());

        var after = await LoadAsync();
        Assert.Equal(before.Scenes, after.Scenes);
        Assert.Equal(before.Highlights, after.Highlights);
        Assert.Equal(before.Apexes, after.Apexes);
    }

    [Fact]
    public async Task Refresh_AppliesOnlyTheDifference_AfterAMark()
    {
        await SeedAsync();
        await RefreshStaleAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // The scene goes back to inheriting the cast: it and the highlight now have both actors.
            await db.SceneActors.Where(sa => sa.SceneId == sceneId).ExecuteDeleteAsync();
            await ClipActorStale.MarkAsync(db, movieId, default);
        }

        Assert.Equal(1, await RefreshStaleAsync());

        var (scenes, highlights, apexes, stale) = await LoadAsync();
        Assert.Equal([(sceneId, aikaId), (sceneId, rinId)], scenes);
        Assert.Equal([(highlightId, aikaId), (highlightId, rinId)], highlights);
        Assert.Equal([(apexId, rinId)], apexes);
        Assert.False(stale);
    }

    [Fact]
    public async Task Refresh_MissingMovie_ReturnsFalse()
    {
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await ClipActorSync.RefreshAsync(db, 9999, default));
    }

    [Fact]
    public async Task RefreshStale_MovieThatThrows_StaysStale_AndOthersAreRefreshed()
    {
        await SeedAsync();
        int otherId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var other = new Movie { Code = "SYNC-002" };
            db.Movies.Add(other);
            await db.SaveChangesAsync();
            otherId = other.Id;
        }

        var refreshed = await RefreshStaleAsync((db, id, ct) =>
            id == movieId ? throw new InvalidOperationException("boom") : ClipActorSync.RefreshAsync(db, id, ct));

        Assert.Equal(1, refreshed);
        await using var check = await factory.CreateDbContextAsync();
        Assert.True(await check.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync());
        Assert.False(await check.Movies.Where(m => m.Id == otherId).Select(m => m.ClipActorsStale).SingleAsync());
    }

    [Fact]
    public async Task EditDuringPass_IsRefreshedAgainInSamePass()
    {
        await SeedAsync();
        var calls = 0;

        var refreshed = await RefreshStaleAsync(async (db, id, ct) =>
        {
            var done = await ClipActorSync.RefreshAsync(db, id, ct);
            if (++calls == 1)
            {
                // An edit lands right after the first refresh committed.
                await using var other = await factory.CreateDbContextAsync(ct);
                await other.SceneActors.Where(sa => sa.SceneId == sceneId).ExecuteDeleteAsync(ct);
                await ClipActorStale.MarkAsync(other, movieId, ct);
            }
            return done;
        });

        Assert.Equal(2, refreshed);
        var (scenes, _, _, stale) = await LoadAsync();
        Assert.Equal([(sceneId, aikaId), (sceneId, rinId)], scenes);
        Assert.False(stale);
    }
}
