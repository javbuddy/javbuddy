using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Movies;

public class DeletedMovieHistoryTests
{
    [Fact]
    public async Task GetMatchesAsync_ReturnsOnlyCandidateMatchesWithoutTracking()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var code in new[] { "ABC-00123", "XYZ-999", "DEF-456" })
                await DeletedMovieHistory.RecordAsync(db, new Movie { Code = code });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var matches = await DeletedMovieHistory.GetMatchesAsync(readDb, ["abc123", "def-456", "MISSING-1"]);
        Assert.Equal(["ABC-00123", "DEF-456"], matches.Select(m => m.Code).Order().ToArray());
        Assert.Empty(readDb.ChangeTracker.Entries());
        Assert.Empty(await DeletedMovieHistory.GetMatchesAsync(readDb, []));
    }
}
