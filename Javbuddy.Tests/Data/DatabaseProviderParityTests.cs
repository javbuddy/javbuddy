using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Data;

/// <summary>Behaviour the app relies on that differs between SQLite and Postgres out of the box. These run on whichever
/// provider the suite is using (see <see cref="PostgresTestDatabase"/>), so each one pins the same result on both.</summary>
public class DatabaseProviderParityTests
{
    [Fact]
    public async Task MovieCode_EqualityIsCaseInsensitive()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.Movies.Add(new Movie { Code = "ABC-001" });
        await db.SaveChangesAsync();

        var found = await db.Movies.AsNoTracking().Where(m => m.Code == "abc-001").Select(m => m.Code).ToListAsync();

        Assert.Equal(["ABC-001"], found);
    }

    [Fact]
    public async Task MovieCode_UniqueIndexIsCaseInsensitive()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.Movies.Add(new Movie { Code = "ABC-001" });
        await db.SaveChangesAsync();

        await using var other = await factory.CreateDbContextAsync();
        other.Movies.Add(new Movie { Code = "abc-001" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.True(DbConstraintErrors.IsDuplicateKey(ex));
    }

    [Fact]
    public async Task Search_MatchesMovieCodeTitleAndActorIgnoringCase()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", Title = "The Quiet Garden" },
                new Movie { Code = "XYZ-002", MetaTitle = "LOUD NEIGHBOURS" });
            db.Actors.Add(new Actor { FirstName = "Mei", LastName = "ITO" });
            await db.SaveChangesAsync();
        }

        var search = new SearchService(factory);

        Assert.Equal(["ABC-001"], (await search.SearchAsync("abc-0")).Movies.Select(m => m.Code));
        Assert.Equal(["ABC-001"], (await search.SearchAsync("QUIET")).Movies.Select(m => m.Code));
        Assert.Equal(["XYZ-002"], (await search.SearchAsync("loud neigh")).Movies.Select(m => m.Code));
        Assert.Equal(["Mei"], (await search.SearchAsync("ito mei")).Actors.Select(a => a.FirstName));
    }

    // ILIKE refuses a nondeterministic collation anywhere in its operand, so these once threw on Postgres only.
    [Fact]
    public async Task CaseInsensitiveSearch_OverCollatedCodeColumnsAndExpressions_MatchesIgnoringCase()
    {
        using var factory = new TestDbContextFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.Movies.AddRange(
                new Movie { Code = "ABC-001", Title = "Quiet Garden" },
                new Movie { Code = "XYZ-002", Title = "Loud Street" });
            seed.CachedImages.Add(new CachedImage { Code = "ABC-001", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) });
            seed.CachedImages.Add(new CachedImage { Code = "XYZ-002", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) });
            await seed.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();

        var images = await db.CachedImages.AsNoTracking().Where(db.ContainsAny<CachedImage>("abc-0", c => c.Code)).Select(c => c.Code).ToListAsync();
        var prefixed = await db.CachedImages.AsNoTracking().Where(db.StartsWithAny<CachedImage>("xyz", c => c.Code)).Select(c => c.Code).ToListAsync();
        var concatenated = await db.Movies.AsNoTracking().Where(db.ContainsAny<Movie>("c-001 quiet", m => m.Code + " " + m.Title)).Select(m => m.Code).ToListAsync();
        var trimmed = await db.Movies.AsNoTracking().Where(db.ContainsAny<Movie>("YZ-00", m => m.Code.Trim())).Select(m => m.Code).ToListAsync();
        var plain = await db.Movies.AsNoTracking().Where(db.ContainsAny<Movie>("GARDEN", m => m.Title)).Select(m => m.Code).ToListAsync();

        Assert.Equal(["ABC-001"], images);
        Assert.Equal(["XYZ-002"], prefixed);
        Assert.Equal(["ABC-001"], concatenated);
        Assert.Equal(["XYZ-002"], trimmed);
        Assert.Equal(["ABC-001"], plain);
    }

    [Fact]
    public async Task ActorSearch_MatchesTheFullNameOnlyWhenThereIsALastName()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Mei", LastName = "Ito" },
                new Actor { FirstName = "Ito" },
                new Actor { FirstName = "Rin", LastName = "Sato" });
            await db.SaveChangesAsync();
        }
        var search = new SearchService(factory);

        // "Ito Mei" is the full name of the first only; the single-name "Ito" doesn't match it, and ranks by first name.
        Assert.Equal(["Mei"], (await search.SearchAsync("ito m")).Actors.Select(a => a.FirstName));
        Assert.Equal(["Ito", "Mei"], (await search.SearchAsync("ITO")).Actors.Select(a => a.FirstName));
        Assert.Equal(["Rin"], (await search.SearchAsync("sato ri")).Actors.Select(a => a.FirstName));
    }

    [Fact]
    public async Task DateTime_RoundTripsWhateverItsKind_AsUnspecified()
    {
        using var factory = new TestDbContextFactory();
        var unspecified = new DateTime(2024, 3, 5, 6, 7, 8, DateTimeKind.Unspecified).AddTicks(1230);
        var utc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(450);
        int unspecifiedId, utcId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var first = new Movie { Code = "KIND-001", MetaReleaseDate = unspecified };
            var second = new Movie { Code = "KIND-002", MetaReleaseDate = utc };
            db.Movies.AddRange(first, second);
            await db.SaveChangesAsync();
            (unspecifiedId, utcId) = (first.Id, second.Id);
        }

        await using var read = await factory.CreateDbContextAsync();
        var movies = await read.Movies.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.MetaReleaseDate);

        Assert.Equal(unspecified.Ticks, movies[unspecifiedId]!.Value.Ticks);
        Assert.Equal(utc.Ticks, movies[utcId]!.Value.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, movies[utcId]!.Value.Kind);
    }

    [Fact]
    public async Task DateAdded_IsFileAddedAtOtherwiseCreatedAt_AndOrdersTheQuery()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "ADD-001", CreatedAt = new DateTime(2024, 1, 1), FileAddedAt = new DateTime(2024, 6, 1) },
                new Movie { Code = "ADD-002", CreatedAt = new DateTime(2024, 3, 1) },
                new Movie { Code = "ADD-003", CreatedAt = new DateTime(2024, 9, 1), FileAddedAt = new DateTime(2023, 1, 1) });
            await db.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        var newestFirst = await read.Movies.AsNoTracking()
            .OrderByDescending(m => EF.Property<DateTime>(m, Movie.DateAddedColumn))
            .Select(m => m.Code)
            .ToListAsync();

        Assert.Equal(["ADD-001", "ADD-002", "ADD-003"], newestFirst);
    }

    [Fact]
    public async Task DeoVrGroup_AddedAfterTheSeedRow_GetsItsOwnId()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var group = new DeoVrGroup { Name = "Favourites", Position = 1 };
        db.DeoVrGroups.Add(group);

        await db.SaveChangesAsync();

        Assert.NotEqual(DeoVrGroup.AllMovies.Id, group.Id);
        Assert.Equal(2, await db.DeoVrGroups.CountAsync());
    }

    [Fact]
    public void SqliteModel_HasNoPendingChanges()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PostgresModel_HasNoPendingChanges()
    {
        // No server needed: the check compares the model with the migrations' snapshot.
        using var db = new PostgresAppDbContext(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=unused").Options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task PostgresMigrations_AreAllApplied_ToTheTestDatabase()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
    }
}
