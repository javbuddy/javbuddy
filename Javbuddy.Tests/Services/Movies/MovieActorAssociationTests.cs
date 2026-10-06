using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Movies;

public class MovieActorAssociationTests
{
    [Fact]
    public async Task SynchronizeAsync_LinksOnlyTrackedActorsNamedInTheMovieCast()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var linkedActor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var unlinkedActor = new Actor { FirstName = "Noa", LastName = "Araki" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui, Untracked Actor" };
        db.AddRange(linkedActor, unlinkedActor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(movie.Id, link.MovieId);
        Assert.Equal(linkedActor.Id, link.ActorId);
    }

    [Fact]
    public async Task SynchronizeAsync_ReplacesLinksWhenTheMovieCastChanges()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var previousActor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var currentActor = new Actor { FirstName = "Noa", LastName = "Araki" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui" };
        db.AddRange(previousActor, currentActor, movie);
        await db.SaveChangesAsync();
        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        movie.MetaActresses = "Araki Noa";
        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(currentActor.Id, link.ActorId);
    }

    [Fact]
    public async Task DeletingActor_CascadesToMovieAssociation()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
        await db.SaveChangesAsync();

        db.Actors.Remove(actor);
        await db.SaveChangesAsync();

        Assert.False(await db.MovieActors.AnyAsync());
    }

    [Fact]
    public async Task DeletingMovie_CascadesToMovieAssociation()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
        await db.SaveChangesAsync();

        db.Movies.Remove(movie);
        await db.SaveChangesAsync();

        Assert.False(await db.MovieActors.AnyAsync());
    }

    [Fact]
    public async Task SynchronizeAllAsync_SynchronizesMultipleMoviesAcrossTrackedActors()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor1 = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var actor2 = new Actor { FirstName = "Noa", LastName = "Araki" };
        var movie1 = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui" };
        var movie2 = new Movie { Code = "DEF-456", MetaActresses = "Hatano Yui, Araki Noa" };
        var movie3 = new Movie { Code = "GHI-789", MetaActresses = "Untracked Person" };
        db.AddRange(actor1, actor2, movie1, movie2, movie3);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAllAsync(db);
        await db.SaveChangesAsync();

        var links = await db.MovieActors.ToListAsync();
        Assert.Equal(3, links.Count);
        Assert.Contains(links, l => l.MovieId == movie1.Id && l.ActorId == actor1.Id);
        Assert.Contains(links, l => l.MovieId == movie2.Id && l.ActorId == actor1.Id);
        Assert.Contains(links, l => l.MovieId == movie2.Id && l.ActorId == actor2.Id);
    }

    [Fact]
    public async Task SynchronizeAllAsync_UpdatesUnmatchedFlagsOnMoviesTheContextDoesNotTrack()
    {
        using var factory = new TestDbContextFactory();
        int partlyMatchedId, nowMatchedId, unchangedId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var partlyMatched = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui, Untracked Person" };
            var nowMatched = new Movie { Code = "DEF-456", MetaActresses = "Hatano Yui", HasUnmatchedActors = true, UnmatchedActorNames = "Hatano Yui" };
            var unchanged = new Movie { Code = "GHI-789", MetaActresses = "Other Person", HasUnmatchedActors = true, UnmatchedActorNames = "Other Person" };
            seed.AddRange(new Actor { FirstName = "Yui", LastName = "Hatano" }, partlyMatched, nowMatched, unchanged);
            await seed.SaveChangesAsync();
            (partlyMatchedId, nowMatchedId, unchangedId) = (partlyMatched.Id, nowMatched.Id, unchanged.Id);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MovieActorAssociation.SynchronizeAllAsync(db);
            // Only the movies whose flags change get loaded as tracked entities.
            Assert.Equal(
                new[] { partlyMatchedId, nowMatchedId }.Order(),
                db.ChangeTracker.Entries<Movie>().Select(entry => entry.Entity.Id).Order());
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var movies = await verify.Movies.ToDictionaryAsync(movie => movie.Id);
        Assert.True(movies[partlyMatchedId].HasUnmatchedActors);
        Assert.Equal("Untracked Person", movies[partlyMatchedId].UnmatchedActorNames);
        Assert.False(movies[nowMatchedId].HasUnmatchedActors);
        Assert.Null(movies[nowMatchedId].UnmatchedActorNames);
        Assert.True(movies[unchangedId].HasUnmatchedActors);
        Assert.Equal("Other Person", movies[unchangedId].UnmatchedActorNames);
        Assert.Equal(2, await verify.MovieActors.CountAsync());
    }

    [Fact]
    public async Task SynchronizeAllAsync_UsesUnsavedCastOfAMovieTheContextAlreadyTracks()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Untracked Person" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        movie.MetaActresses = "Hatano Yui";
        await MovieActorAssociation.SynchronizeAllAsync(db);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(actor.Id, link.ActorId);
        Assert.False(movie.HasUnmatchedActors);
        Assert.Null(movie.UnmatchedActorNames);
    }

    [Fact]
    public async Task SynchronizeAllAsync_UsesUnsavedClearedCastOfAMovieTheContextAlreadyTracks()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Untracked Person" };
        db.AddRange(new Actor { FirstName = "Yui", LastName = "Hatano" }, movie);
        await db.SaveChangesAsync();
        await MovieActorAssociation.SynchronizeAllAsync(db);
        Assert.True(movie.HasUnmatchedActors);

        movie.MetaActresses = null;
        await MovieActorAssociation.SynchronizeAllAsync(db);

        Assert.False(movie.HasUnmatchedActors);
        Assert.Null(movie.UnmatchedActorNames);
    }

    [Fact]
    public async Task SynchronizeAsync_ClearsLinksWhenCastBecomesNullOrEmpty()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();
        Assert.True(await db.MovieActors.AnyAsync());

        movie.MetaActresses = null;
        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        Assert.False(await db.MovieActors.AnyAsync());
    }

    [Fact]
    public async Task SynchronizeAsync_MatchesActorByJapaneseNameKanji()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano", JapaneseNameKanji = "波多野結衣" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "波多野結衣" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(actor.Id, link.ActorId);
    }

    [Fact]
    public async Task SynchronizeAsync_MatchesActorByReversedNameOrder()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Yui Hatano" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(actor.Id, link.ActorId);
    }

    [Fact]
    public async Task SynchronizeAsync_MatchesActorByJapaneseKanjiIgnoringInternalSpaces()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Noa", LastName = "Araki", JapaneseNameKanji = "新木希空" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "新木 希空" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(actor.Id, link.ActorId);
    }

    [Fact]
    public async Task SynchronizeAsync_FlagsUnmatchedActorNames()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var linkedActor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Hatano Yui, Untracked Actor" };
        db.AddRange(linkedActor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        Assert.True(movie.HasUnmatchedActors);
        Assert.Equal("Untracked Actor", movie.UnmatchedActorNames);
    }

    [Fact]
    public async Task SynchronizeAsync_ClearsUnmatchedActorsWhenCastFullyMatches()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Untracked Actor", HasUnmatchedActors = true, UnmatchedActorNames = "Untracked Actor" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        movie.MetaActresses = "Hatano Yui";
        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        Assert.False(movie.HasUnmatchedActors);
        Assert.Null(movie.UnmatchedActorNames);
    }

    [Fact]
    public async Task SynchronizeAsync_LeavesUnmatchedActorsFalseWhenCastIsEmpty()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MetaActresses = null };
        db.Add(movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        Assert.False(movie.HasUnmatchedActors);
        Assert.Null(movie.UnmatchedActorNames);
    }

    [Fact]
    public async Task SynchronizeAsync_MatchesActorByAlias()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
        actor.Aliases.Add(new ActorAlias { Name = "Alternate Stage Name" });
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Alternate Stage Name" };
        db.AddRange(actor, movie);
        await db.SaveChangesAsync();

        await MovieActorAssociation.SynchronizeAsync(db, movie);
        await db.SaveChangesAsync();

        var link = await db.MovieActors.SingleAsync();
        Assert.Equal(actor.Id, link.ActorId);
    }
}
