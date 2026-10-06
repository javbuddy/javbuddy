using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Which tracked changes mark a movie's stored effective actors stale.</summary>
public sealed class ClipActorStaleInterceptorTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private int movieId, aikaId, rinId, sceneId, highlightId, apexId;

    public void Dispose() => factory.Dispose();

    /// <summary>Cast Aika (also on the scene and the apex); Rin exists but isn't in the cast. Ends fresh.</summary>
    private async Task SeedFreshAsync()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var aika = new Actor { FirstName = "Aika" };
            var rin = new Actor { FirstName = "Rin" };
            var movie = new Movie { Code = "MARK-001", MediaDurationSeconds = 3600 };
            db.AddRange(aika, rin, movie);
            await db.SaveChangesAsync();
            (movieId, aikaId, rinId) = (movie.Id, aika.Id, rin.Id);
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = aikaId });
            var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
            var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
            var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
            db.AddRange(scene, highlight, apex);
            await db.SaveChangesAsync();
            (sceneId, highlightId, apexId) = (scene.Id, highlight.Id, apex.Id);
            db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = aikaId });
            db.ApexActors.Add(new ApexActor { ApexId = apexId, MovieId = movieId, ActorId = aikaId });
            await db.SaveChangesAsync();
        }
        await using var refresh = await factory.CreateDbContextAsync();
        await ClipActorSync.RefreshStaleAsync(refresh);
        Assert.False(await IsStaleAsync());
    }

    private async Task<bool> IsStaleAsync(int? id = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Movies.Where(m => m.Id == (id ?? movieId)).Select(m => m.ClipActorsStale).SingleAsync();
    }

    private async Task ChangeAsync(Func<AppDbContext, Task> change)
    {
        await using var db = await factory.CreateDbContextAsync();
        await change(db);
        await db.SaveChangesAsync();
    }

    public static TheoryData<string> MarkingChanges() =>
    [
        "scene added", "scene start", "scene end", "scene deleted",
        "highlight added", "highlight start", "highlight end", "highlight deleted",
        "apex added", "apex moved", "apex deleted",
        "scene actor added", "scene actor removed", "highlight actor added", "apex actor removed",
        "cast added", "cast removed", "duration changed", "actor deleted",
    ];

    [Theory]
    [MemberData(nameof(MarkingChanges))]
    public async Task Change_MarksMovieStale(string change)
    {
        await SeedFreshAsync();

        await ChangeAsync(async db =>
        {
            switch (change)
            {
                case "scene added": db.Scenes.Add(new Scene { MovieId = movieId, StartSeconds = 600 }); break;
                case "scene start": (await db.Scenes.FindAsync(sceneId))!.StartSeconds = 5; break;
                case "scene end": (await db.Scenes.FindAsync(sceneId))!.EndSeconds = 300; break;
                case "scene deleted": db.Scenes.Remove((await db.Scenes.FindAsync(sceneId))!); break;
                case "highlight added": db.MovieHighlights.Add(new MovieHighlight { MovieId = movieId, StartSeconds = 200, EndSeconds = 300 }); break;
                case "highlight start": (await db.MovieHighlights.FindAsync(highlightId))!.StartSeconds = 20; break;
                case "highlight end": (await db.MovieHighlights.FindAsync(highlightId))!.EndSeconds = 40; break;
                case "highlight deleted": db.MovieHighlights.Remove((await db.MovieHighlights.FindAsync(highlightId))!); break;
                case "apex added": db.MovieApexes.Add(new MovieApex { MovieId = movieId, Seconds = 900 }); break;
                case "apex moved": (await db.MovieApexes.FindAsync(apexId))!.Seconds = 150; break;
                case "apex deleted": db.MovieApexes.Remove((await db.MovieApexes.FindAsync(apexId))!); break;
                case "scene actor added":
                    db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = rinId });
                    db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = rinId });
                    break;
                case "scene actor removed": db.SceneActors.Remove(await db.SceneActors.SingleAsync(sa => sa.SceneId == sceneId)); break;
                case "highlight actor added": db.HighlightActors.Add(new HighlightActor { HighlightId = highlightId, MovieId = movieId, ActorId = aikaId }); break;
                case "apex actor removed": db.ApexActors.Remove(await db.ApexActors.SingleAsync(aa => aa.ApexId == apexId)); break;
                case "cast added": db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = rinId }); break;
                case "cast removed": db.MovieActors.Remove(await db.MovieActors.SingleAsync(ma => ma.MovieId == movieId && ma.ActorId == aikaId)); break;
                case "duration changed": (await db.Movies.FindAsync(movieId))!.MediaDurationSeconds = 1800; break;
                // Only the actor is loaded: its cast links go by the database cascade, unseen by the tracker.
                case "actor deleted": db.Actors.Remove((await db.Actors.FindAsync(aikaId))!); break;
                default: throw new ArgumentOutOfRangeException(nameof(change), change, null);
            }
        });

        Assert.True(await IsStaleAsync());
    }

    public static TheoryData<string> NeutralChanges() =>
        ["scene title", "scene tag", "scene favorite", "scene hidden", "same duration", "highlight title", "apex favorite"];

    [Theory]
    [MemberData(nameof(NeutralChanges))]
    public async Task Change_LeavesMovieFresh(string change)
    {
        await SeedFreshAsync();

        await ChangeAsync(async db =>
        {
            switch (change)
            {
                case "scene title": (await db.Scenes.FindAsync(sceneId))!.Title = "Intro"; break;
                case "scene tag":
                    var tag = new Tag { Name = "Bath" };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync();
                    db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = tag.Id });
                    break;
                case "scene favorite": (await db.Scenes.FindAsync(sceneId))!.IsFavorite = true; break;
                case "scene hidden": (await db.Scenes.FindAsync(sceneId))!.IsHiddenFromOverview = true; break;
                case "same duration": (await db.Movies.FindAsync(movieId))!.MediaDurationSeconds = 3600; break;
                case "highlight title": (await db.MovieHighlights.FindAsync(highlightId))!.Title = "Kiss"; break;
                case "apex favorite": (await db.MovieApexes.FindAsync(apexId))!.IsFavorite = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(change), change, null);
            }
        });

        Assert.False(await IsStaleAsync());
    }

    [Fact]
    public async Task AddingSeveralMovies_WithACastChangeInTheSameSave_MarksTheExistingMovie()
    {
        await SeedFreshAsync();
        await using var db = await factory.CreateDbContextAsync();
        // New movies are tracked with Id 0 until saved, so two of them share that key.
        var first = new Movie { Code = "NEW-001" };
        var second = new Movie { Code = "NEW-002" };
        db.Movies.AddRange(first, second);
        db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = rinId });

        await db.SaveChangesAsync();

        Assert.True(await IsStaleAsync());
        Assert.True(await IsStaleAsync(first.Id));
        Assert.True(await IsStaleAsync(second.Id));
    }

    [Fact]
    public async Task AddingAMovieWithAnExplicitId_AndItsCast_InOneSave_Works()
    {
        await SeedFreshAsync();
        await using var db = await factory.CreateDbContextAsync();
        db.Movies.Add(new Movie { Id = 500, Code = "NEW-500" });
        db.MovieActors.Add(new MovieActor { MovieId = 500, ActorId = aikaId });

        await db.SaveChangesAsync();

        Assert.True(await IsStaleAsync(500));
    }

    [Fact]
    public async Task UntrackedMovie_IsMarked_AndTheEditedClipKeepsItsRealMovie()
    {
        await SeedFreshAsync();
        await using var db = await factory.CreateDbContextAsync();
        var scene = (await db.Scenes.FindAsync(sceneId))!;
        scene.StartSeconds = 5;

        await db.SaveChangesAsync();

        Assert.True(await IsStaleAsync());
        Assert.Equal(EntityState.Unchanged, db.Entry(scene).State);
        Assert.Equal("MARK-001", scene.Movie.Code);
    }

    [Fact]
    public async Task UntrackedMovie_IsMarked_OnASynchronousSave()
    {
        await SeedFreshAsync();
        using var db = factory.CreateDbContext();
        db.Scenes.Find(sceneId)!.StartSeconds = 5;

        db.SaveChanges();

        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task ClipEditedAgain_AfterAMarkingSave_IsSaved()
    {
        await SeedFreshAsync();
        await using var db = await factory.CreateDbContextAsync();
        var scene = (await db.Scenes.FindAsync(sceneId))!;
        scene.StartSeconds = 5;
        await db.SaveChangesAsync();

        scene.Title = "Intro";
        await db.SaveChangesAsync();

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("Intro", (await check.Scenes.FindAsync(sceneId))!.Title);
    }

    [Fact]
    public async Task ClipUpdatedInAnotherContext_AfterAMarkingSave_KeepsTheMovieRow()
    {
        await SeedFreshAsync();
        Scene scene;
        await using (var db = await factory.CreateDbContextAsync())
        {
            scene = (await db.Scenes.FindAsync(sceneId))!;
            scene.StartSeconds = 5;
            await db.SaveChangesAsync();
        }

        await using (var other = await factory.CreateDbContextAsync())
        {
            scene.Title = "Intro";
            other.Update(scene);
            await other.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("MARK-001", (await check.Movies.FindAsync(movieId))!.Code);
    }

    [Fact]
    public async Task TrackedMovieWithStaleSnapshot_IsStillMarked()
    {
        await SeedFreshAsync();
        await ChangeAsync(db => ClipActorStale.MarkAsync(db, movieId, default));
        await using var longLived = await factory.CreateDbContextAsync();
        var movie = (await longLived.Movies.FindAsync(movieId))!; // snapshot: stale
        await using (var worker = await factory.CreateDbContextAsync())
        {
            await ClipActorSync.RefreshStaleAsync(worker); // the worker clears it meanwhile
        }

        movie.MediaDurationSeconds = 1800;
        await longLived.SaveChangesAsync();

        Assert.True(await IsStaleAsync());
    }

    [Fact]
    public async Task BulkCastSync_MarksEveryChangedMovie()
    {
        await SeedFreshAsync();
        int otherId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var other = new Movie { Code = "MARK-002", MetaActresses = "Aika" };
            db.Movies.Add(other);
            await db.SaveChangesAsync();
            otherId = other.Id;
            (await db.Movies.FindAsync(movieId))!.MetaActresses = "Rin";
            await db.SaveChangesAsync();
            await ClipActorSync.RefreshStaleAsync(db);
        }
        Assert.False(await IsStaleAsync(movieId));
        Assert.False(await IsStaleAsync(otherId));

        await ChangeAsync(db => MovieActorAssociation.SynchronizeAllAsync(db));

        Assert.True(await IsStaleAsync(movieId));
        Assert.True(await IsStaleAsync(otherId));
    }

    [Fact]
    public async Task MarkingSave_SignalsTheWorker()
    {
        await SeedFreshAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await DrainAsync(factory.ClipActorSignal);

        await ChangeAsync(async db => (await db.MovieApexes.FindAsync(apexId))!.Seconds = 150);

        await factory.ClipActorSignal.WaitAsync(timeout.Token);
    }

    // Seeding already signalled; consume that so the assertion sees only the change's signal.
    private static async Task DrainAsync(ClipActorRefreshSignal signal)
    {
        using var quick = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await signal.WaitAsync(quick.Token); } catch (OperationCanceledException) { }
    }
}
