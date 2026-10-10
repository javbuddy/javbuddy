using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Data;

public class SqliteConstraintErrorsTests
{
    [Fact]
    public async Task IsDuplicateKey_TrueForADuplicateCompositePrimaryKey()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "DUP-001" };
        var tag = new Tag { Name = "Dup" };
        db.AddRange(movie, tag);
        await db.SaveChangesAsync();
        db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
        await db.SaveChangesAsync();

        await using var other = await factory.CreateDbContextAsync();
        other.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.True(SqliteConstraintErrors.IsDuplicateKey(ex));
    }

    [Fact]
    public async Task IsDuplicateKey_TrueForADuplicateUniqueIndexValue()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.Actors.Add(new Actor { FirstName = "Mei", LastName = "Ito" });
        await db.SaveChangesAsync();

        await using var other = await factory.CreateDbContextAsync();
        other.Actors.Add(new Actor { FirstName = "Mei", LastName = "Ito" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.True(SqliteConstraintErrors.IsDuplicateKey(ex));
    }

    [Fact]
    public async Task IsDuplicateKey_FalseForAForeignKeyViolation()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.MovieTags.Add(new MovieTag { MovieId = 999, TagId = 999 });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.False(SqliteConstraintErrors.IsDuplicateKey(ex));
    }
}
