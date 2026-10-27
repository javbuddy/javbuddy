using Javbuddy.Models;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task SavedMovie_CanBeReadBack()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var movie = await readDb.Movies.SingleAsync(m => m.Code == "ABC-123");

        Assert.Equal(MovieStatus.Missing, movie.Status);
    }

    [Fact]
    public async Task DuplicateMovieCode_ViolatesUniqueIndex()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.Movies.Add(new Movie { Code = "ABC-123" });
        await db.SaveChangesAsync();

        db.Movies.Add(new Movie { Code = "ABC-123" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ActorSchema_PersistsTheAgreedIdentityAndMetadataFields()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜",
                JapaneseNameKana = "みかみゆあ",
                JellyfinPersonId = "person-12",
                R18DevId = 34,
                R18DevName = "Yua Mikami"
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var actor = await readDb.Actors.SingleAsync();

        Assert.Equal("Mikami Yua", actor.DisplayName);
        Assert.Equal("三上悠亜", actor.JapaneseNameKanji);
        Assert.Equal("person-12", actor.JellyfinPersonId);
        Assert.Equal(34, actor.R18DevId);
        Assert.Equal("Yua Mikami", actor.R18DevName);
    }

    [Fact]
    public async Task MovieSchema_PersistsCleanupBlacklistedAndSnoozedUntil()
    {
        using var factory = new TestDbContextFactory();
        var snoozedUntil = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        int defaultMovieId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", CleanupBlacklisted = true, CleanupSnoozedUntil = snoozedUntil });
            var defaultMovie = new Movie { Code = "DEF-456" };
            db.Movies.Add(defaultMovie);
            await db.SaveChangesAsync();
            defaultMovieId = defaultMovie.Id;
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var movie = await readDb.Movies.SingleAsync(m => m.Code == "ABC-123");
        Assert.True(movie.CleanupBlacklisted);
        Assert.Equal(snoozedUntil, movie.CleanupSnoozedUntil);

        var defaultMovie2 = await readDb.Movies.SingleAsync(m => m.Id == defaultMovieId);
        Assert.False(defaultMovie2.CleanupBlacklisted);
        Assert.Null(defaultMovie2.CleanupSnoozedUntil);
    }

    [Fact]
    public async Task MovieCodeColumns_CompareIgnoringCase()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "XYZ-001" };
            var actor = new Actor { FirstName = "Test" };
            db.AddRange(movie, actor);
            await db.SaveChangesAsync();
            db.CachedImages.Add(new CachedImage { Code = "abc-123", Role = "poster", Variant = "thumb", StorageId = Guid.NewGuid() });
            db.TorrentDownloads.Add(new TorrentDownload { MovieId = movie.Id, MovieCode = "abc-123", SavePath = "/downloads/abc-123" });
            db.ActorImages.Add(new ActorImage { ActorId = actor.Id, Variant = "thumb", SourceMovieCode = "abc-123" });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await readDb.CachedImages.AnyAsync(c => c.Code == "ABC-123"));
        Assert.True(await readDb.TorrentDownloads.AnyAsync(t => t.MovieCode == "ABC-123"));
        Assert.True(await readDb.ActorImages.AnyAsync(a => a.SourceMovieCode == "ABC-123"));
    }

    [Fact]
    public async Task CachedImageCode_DifferingOnlyInCase_ViolatesUniqueIndex()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.CachedImages.Add(new CachedImage { Code = "abc-123", Role = "poster", Variant = "thumb", StorageId = Guid.NewGuid() });
        db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = "poster", Variant = "thumb", StorageId = Guid.NewGuid() });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PosterVersion_IsFoundForACodeCachedInAnotherCase()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var cachedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        db.CachedImages.Add(new CachedImage { Code = "abc-123", Role = "poster", Index = 0, Variant = "thumb", StorageId = Guid.NewGuid(), UpdatedAt = cachedAt });
        await db.SaveChangesAsync();
        var movie = new Movie { Code = "ABC-123" };

        await Javbuddy.Services.Images.PosterVersion.ApplyAsync(db, [movie]);

        Assert.Equal(Javbuddy.Services.Images.PosterVersion.Compute(cachedAt, null), movie.PosterVersion);
    }
}
