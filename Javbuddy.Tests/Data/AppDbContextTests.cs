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
}
