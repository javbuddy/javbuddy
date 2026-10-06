using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Movies;

public class MovieDetailQueryServiceTests
{
    [Fact]
    public async Task GetByCodeAsync_MatchesCaseInsensitively_WithFilesAndTagsAndParentTags()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var parent = new Tag { Name = "Genre" };
            var child = new Tag { Name = "Drama", ParentTag = parent };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got };
            movie.MovieFiles.Add(new MovieFile { FileName = "abc-123.mp4", VersionTag = "Original" });
            movie.MovieTags.Add(new MovieTag { Tag = child });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var found = await new MovieDetailQueryService(factory).GetByCodeAsync("abc-123");

        Assert.NotNull(found);
        Assert.Single(found.MovieFiles);
        var tag = Assert.Single(found.MovieTags).Tag;
        Assert.Equal("Drama", tag.Name);
        Assert.Equal("Genre", tag.ParentTag?.Name);
    }

    [Fact]
    public async Task GetByCodeAsync_UnknownCode_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();

        Assert.Null(await new MovieDetailQueryService(factory).GetByCodeAsync("NOPE-1"));
    }

    [Fact]
    public async Task GetMovieTagsAsync_ReturnsOnlyThatMoviesTagsWithParents()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var parent = new Tag { Name = "Genre" };
            var mine = new Tag { Name = "Mine", ParentTag = parent };
            var other = new Tag { Name = "Other" };
            var movie = new Movie { Code = "AAA-1", Status = MovieStatus.Got };
            movie.MovieTags.Add(new MovieTag { Tag = mine });
            var otherMovie = new Movie { Code = "BBB-1", Status = MovieStatus.Got };
            otherMovie.MovieTags.Add(new MovieTag { Tag = other });
            db.Movies.AddRange(movie, otherMovie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var tags = await new MovieDetailQueryService(factory).GetMovieTagsAsync(movieId);

        var link = Assert.Single(tags);
        Assert.Equal("Mine", link.Tag.Name);
        Assert.Equal("Genre", link.Tag.ParentTag?.Name);
    }

    [Fact]
    public async Task GetCastAsync_MatchesNamesByDisplayNameKanjiAndAlias_AndReportsPortraitAndAge()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var yua = new Actor { FirstName = "Yua", LastName = "Mikami", BirthDate = new DateTime(1993, 8, 16) };
            var remu = new Actor { FirstName = "Remu", JapaneseNameKanji = "鈴村あいり" };
            remu.Aliases.Add(new ActorAlias { Name = "Airi Suzumura" });
            movie = new Movie { Code = "AAA-1", Status = MovieStatus.Got, MetaActresses = "Mikami Yua, Airi Suzumura, Unknown Person", MetaReleaseDate = new DateTime(2020, 8, 16) };
            db.Movies.Add(movie);
            db.Actors.AddRange(yua, remu);
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(new MovieActor { MovieId = movie.Id, ActorId = yua.Id }, new MovieActor { MovieId = movie.Id, ActorId = remu.Id });
            db.ActorImages.Add(new ActorImage { ActorId = yua.Id, Variant = "thumb", SourceMovieCode = "X", StorageId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movie);

        Assert.Equal(["Mikami Yua", "Airi Suzumura", "Unknown Person"], cast.Select(c => c.Name));
        Assert.True(cast[0].HasImage);
        Assert.NotNull(cast[0].ImageVersion);
        Assert.Equal(27, cast[0].Age);
        Assert.NotNull(cast[1].ActorId);
        Assert.False(cast[1].HasImage);
        Assert.Null(cast[2].ActorId);
    }

    [Fact]
    public async Task GetCastAsync_AgeAtReleaseUnder18_FlagsEntryAsUnderage()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var young = new Actor { FirstName = "Young", LastName = "Actor", BirthDate = new DateTime(2000, 6, 1) };
            var adult = new Actor { FirstName = "Adult", LastName = "Actor", BirthDate = new DateTime(1999, 5, 31) };
            movie = new Movie { Code = "AAA-1", Status = MovieStatus.Got, MetaActresses = "Actor Young, Actor Adult, Unknown Person", MetaReleaseDate = new DateTime(2017, 5, 31) };
            db.Movies.Add(movie);
            db.Actors.AddRange(young, adult);
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(new MovieActor { MovieId = movie.Id, ActorId = young.Id }, new MovieActor { MovieId = movie.Id, ActorId = adult.Id });
            await db.SaveChangesAsync();
        }

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movie);

        Assert.Equal(16, cast[0].Age);
        Assert.True(cast[0].IsUnderage);
        Assert.Equal(18, cast[1].Age);
        Assert.False(cast[1].IsUnderage);
        Assert.Null(cast[2].Age);
        Assert.False(cast[2].IsUnderage);
    }

    [Fact]
    public async Task GetCastAsync_ByMovieId_ReadsTheCurrentCastText()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "AAA-1", MetaActresses = "Mikami Yua, Unknown Person" };
            db.Movies.Add(movie);
            db.Actors.Add(yua);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = yua.Id });
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movieId);

        Assert.Equal(["Mikami Yua", "Unknown Person"], cast.Select(c => c.Name));
        Assert.NotNull(cast[0].ActorId);
        Assert.Null(cast[1].ActorId);
    }

    [Fact]
    public async Task GetCastAsync_ByMovieId_UnknownMovie_ReturnsEmpty()
    {
        using var factory = new TestDbContextFactory();

        Assert.Empty(await new MovieDetailQueryService(factory).GetCastAsync(999));
    }

    [Fact]
    public async Task GetCastAsync_NoCastText_ReturnsEmpty()
    {
        using var factory = new TestDbContextFactory();

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(new Movie { Code = "AAA-1", MetaActresses = null });

        Assert.Empty(cast);
    }
}
