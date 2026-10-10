using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Actor tags: the interceptor marks the movie stale, ClipActorSync stores the effective rows, and ClipTagSync
/// gives the movie the plain tag.</summary>
public sealed class ClipActorTagSyncTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private int movieId, meiId, sceneId, highlightId, blondeId, brunetteId;

    public void Dispose() => factory.Dispose();

    // Cast Mei. Scene from 0, highlight 10–100 starting in it.
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var mei = new Actor { FirstName = "Mei" };
        var movie = new Movie { Code = "ATAG-001", MediaDurationSeconds = 3600 };
        var blonde = new Tag { Name = "Blonde", IsActorTag = true };
        var brunette = new Tag { Name = "Brunette", IsActorTag = true };
        db.AddRange(mei, movie, blonde, brunette);
        await db.SaveChangesAsync();
        (movieId, meiId, blondeId, brunetteId) = (movie.Id, mei.Id, blonde.Id, brunette.Id);
        db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = meiId });
        var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
        db.AddRange(scene, highlight);
        await db.SaveChangesAsync();
        (sceneId, highlightId) = (scene.Id, highlight.Id);
    }

    private async Task RefreshAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await ClipActorSync.RefreshAsync(db, movieId, CancellationToken.None);
        await ClipTagSync.RefreshAsync(db, movieId, CancellationToken.None);
    }

    [Fact]
    public async Task AddingAnActorTag_MarksTheMovieStale()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await ClipActorSync.RefreshAsync(db, movieId, CancellationToken.None);
            db.MovieActorTags.Add(new MovieActorTag { MovieId = movieId, ActorId = meiId, TagId = blondeId });
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.True(await check.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync());
    }

    [Fact]
    public async Task Refresh_StoresEffectiveAndRolledUpActorTags_AndGivesTheMovieThePlainTags()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieActorTags.Add(new MovieActorTag { MovieId = movieId, ActorId = meiId, TagId = blondeId });
            db.HighlightActorTags.Add(new HighlightActorTag { HighlightId = highlightId, MovieId = movieId, ActorId = meiId, TagId = brunetteId });
            await db.SaveChangesAsync();
        }

        await RefreshAsync();

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(
            [(blondeId, false), (brunetteId, true)],
            (await check.SceneEffectiveActorTags.Where(r => r.SceneId == sceneId).Select(r => new { r.TagId, r.IsRolledUp }).ToListAsync())
                .Select(r => (r.TagId, r.IsRolledUp)).Order().ToList());
        Assert.Equal(
            [brunetteId],
            await check.HighlightEffectiveActorTags.Where(r => r.HighlightId == highlightId).Select(r => r.TagId).ToListAsync());
        Assert.Equal(
            [blondeId, brunetteId],
            (await check.MovieTags.Where(mt => mt.MovieId == movieId && mt.FromClips && !mt.IsExplicit).Select(mt => mt.TagId).ToListAsync()).Order());
    }

    [Fact]
    public async Task RemovingTheActorFromTheCast_DropsTheirActorTags()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SceneActorTags.Add(new SceneActorTag { SceneId = sceneId, MovieId = movieId, ActorId = meiId, TagId = blondeId });
            await db.SaveChangesAsync();
            await db.MovieActors.Where(ma => ma.MovieId == movieId).ExecuteDeleteAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.SceneActorTags.ToListAsync());
    }

    [Fact]
    public async Task Refresh_AfterTheActorLeavesTheCast_RemovesThePlainTagTheirTagsGave()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SceneActorTags.Add(new SceneActorTag { SceneId = sceneId, MovieId = movieId, ActorId = meiId, TagId = blondeId });
            await db.SaveChangesAsync();
        }
        await RefreshAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Single(await db.MovieTags.ToListAsync());
            db.MovieActors.RemoveRange(await db.MovieActors.ToListAsync());
            await db.SaveChangesAsync();
        }

        var tagsChanged = new List<int>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await ClipActorSync.RefreshStaleAsync(db, onMovieTagsChanged: (id, _) => { tagsChanged.Add(id); return Task.CompletedTask; }));
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.MovieTags.ToListAsync());
        Assert.Empty(await check.SceneEffectiveActorTags.ToListAsync());
        // The worker's .nfo drift check runs for it.
        Assert.Equal([movieId], tagsChanged);
    }
}
