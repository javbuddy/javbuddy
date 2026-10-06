using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Movies;

public class DeletedMovieServiceTests
{
    [Fact]
    public async Task GetPageAsync_ReturnsNewestFirstWithBoundedPages()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DeletedMovies.AddRange(Enumerable.Range(1, 51).Select(i => new DeletedMovie
            {
                Code = $"ABC-{i}",
                NormalizedCode = $"ABC-{i}",
                CanonicalKey = $"ABC{i}",
                DeletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            }));
            await db.SaveChangesAsync();
        }
        var service = new DeletedMovieService(factory, new MovieChangeNotifier());
        var first = await service.GetPageAsync(0);
        var second = await service.GetPageAsync(1);
        Assert.Equal(51, first.TotalCount);
        Assert.Equal(50, first.Movies.Count);
        Assert.Equal("ABC-51", first.Movies[0].Code);
        Assert.Equal("ABC-1", Assert.Single(second.Movies).Code);
    }

    [Fact]
    public async Task PurgeAsync_RemovesSelectedHistoryThenAllHistory_WithoutChangingActiveMovies()
    {
        using var factory = new TestDbContextFactory();
        int id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new DeletedMovie { Code = "ABC-1", NormalizedCode = "ABC-1", CanonicalKey = "ABC1" };
            db.DeletedMovies.AddRange(movie, new DeletedMovie { Code = "ABC-2", NormalizedCode = "ABC-2", CanonicalKey = "ABC2" });
            db.Movies.Add(new Movie { Code = "ABC-3" });
            await db.SaveChangesAsync();
            id = movie.Id;
        }
        var notifier = new MovieChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;
        var service = new DeletedMovieService(factory, notifier);
        await service.PurgeAsync(id);
        Assert.Equal("ABC-2", Assert.Single((await service.GetPageAsync(0)).Movies).Code);
        await service.PurgeAsync(null);
        Assert.Equal(0, (await service.GetPageAsync(0)).TotalCount);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal("ABC-3", (await verify.Movies.SingleAsync()).Code);
        Assert.Equal(2, notifications);
    }
}
