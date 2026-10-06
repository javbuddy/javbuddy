using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Actors;

public class ActorDetailQueryServiceTests
{
    [Fact]
    public async Task GetMoviesAsync_ReturnsOnlyThisActorsMoviesNewestFirst_WithTagsAndScenes()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var otherActor = new Actor { FirstName = "Remu" };
            var tag = new Tag { Name = "Drama", ParentTag = new Tag { Name = "Genre" } };
            var older = new Movie { Code = "OLD-1", Status = MovieStatus.Got, CreatedAt = new DateTime(2026, 1, 1) };
            var newer = new Movie { Code = "NEW-1", Status = MovieStatus.Got, CreatedAt = new DateTime(2026, 6, 1) };
            var notHers = new Movie { Code = "OTH-1", Status = MovieStatus.Got };
            newer.MovieTags.Add(new MovieTag { Tag = tag });
            db.Movies.AddRange(older, newer, notHers);
            db.Actors.AddRange(actor, otherActor);
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(
                new MovieActor { MovieId = older.Id, ActorId = actor.Id },
                new MovieActor { MovieId = newer.Id, ActorId = actor.Id },
                new MovieActor { MovieId = notHers.Id, ActorId = otherActor.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var movies = await new ActorDetailQueryService(factory).GetMoviesAsync(actorId);

        Assert.Equal(["NEW-1", "OLD-1"], movies.Select(m => m.Code));
        var tagLink = Assert.Single(movies[0].MovieTags);
        Assert.Equal("Genre", tagLink.Tag.ParentTag?.Name);
    }

    // A crop rewrites the movie's cached poster, so its thumbnail URL must change with it.
    [Fact]
    public async Task GetMoviesAsync_PosterVersionChangesWhenCachedPosterIsRewritten()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua" };
            var cropped = new Movie { Code = "CRP-1", Status = MovieStatus.Got, CreatedAt = new DateTime(2026, 6, 1) };
            var untouched = new Movie { Code = "UNT-1", Status = MovieStatus.Got, CreatedAt = new DateTime(2026, 1, 1) };
            db.Movies.AddRange(cropped, untouched);
            db.Actors.Add(actor);
            db.CachedImages.AddRange(
                new CachedImage { Code = "CRP-1", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) },
                new CachedImage { Code = "UNT-1", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) });
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(
                new MovieActor { MovieId = cropped.Id, ActorId = actor.Id },
                new MovieActor { MovieId = untouched.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }
        var service = new ActorDetailQueryService(factory);

        var before = await service.GetMoviesAsync(actorId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Single(c => c.Code == "CRP-1").UpdatedAt = new DateTime(2026, 2, 1);
            await db.SaveChangesAsync();
        }
        var after = await service.GetMoviesAsync(actorId);

        Assert.NotNull(before[0].PosterVersion);
        Assert.NotEqual(before[0].PosterVersion, after[0].PosterVersion);
        Assert.Equal(before[1].PosterVersion, after[1].PosterVersion);
    }

    [Fact]
    public async Task GetPhotoCountAsync_CountsOnlyThisActorsPhotos()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua" };
            var other = new Actor { FirstName = "Remu" };
            db.Actors.AddRange(actor, other);
            await db.SaveChangesAsync();
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actor.Id },
                new ActorPhoto { ActorId = actor.Id },
                new ActorPhoto { ActorId = other.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        Assert.Equal(2, await new ActorDetailQueryService(factory).GetPhotoCountAsync(actorId));
    }

    [Fact]
    public async Task GetDownloadingCodesAsync_ReturnsCodesWithAnActiveOrFinishedTorrent()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "DL-1", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.TorrentDownloads.AddRange(
                new TorrentDownload { MovieId = movie.Id, MovieCode = "DL-1", Status = TorrentDownloadStatus.Downloading },
                new TorrentDownload { MovieId = movie.Id, MovieCode = "ERR-1", Status = TorrentDownloadStatus.Error });
            await db.SaveChangesAsync();
        }

        var codes = await new ActorDetailQueryService(factory).GetDownloadingCodesAsync();

        Assert.Equal(["DL-1"], codes);
        Assert.Contains("dl-1", codes);
    }
}
