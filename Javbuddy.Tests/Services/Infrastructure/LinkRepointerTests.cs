using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Infrastructure;

public class LinkRepointerTests
{
    [Fact]
    public async Task RepointAsync_MovesSourceRowsToTarget_WithoutDuplicatingWhatTheTargetHas()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId;
        using (var db = factory.CreateDbContext())
        {
            var source = new Tag { Name = "Source" };
            var target = new Tag { Name = "Target" };
            var movies = new[] { new Movie { Code = "AAA-001" }, new Movie { Code = "AAA-002" }, new Movie { Code = "AAA-003" } };
            db.Tags.AddRange(source, target);
            db.Movies.AddRange(movies);
            db.SaveChanges();
            (sourceId, targetId) = (source.Id, target.Id);
            db.MovieTags.AddRange(
                new MovieTag { MovieId = movies[0].Id, TagId = sourceId },
                new MovieTag { MovieId = movies[1].Id, TagId = sourceId },
                new MovieTag { MovieId = movies[1].Id, TagId = targetId },
                new MovieTag { MovieId = movies[2].Id, TagId = targetId });
            db.SaveChanges();
        }

        using (var db = factory.CreateDbContext())
        {
            await LinkRepointer.RepointAsync(db.MovieTags, mt => mt.TagId == sourceId, mt => mt.TagId == targetId,
                mt => mt.MovieId, mt => new MovieTag { MovieId = mt.MovieId, TagId = targetId }, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        using var verify = factory.CreateDbContext();
        Assert.Empty(verify.MovieTags.Where(mt => mt.TagId == sourceId));
        var targetMovies = verify.MovieTags.Where(mt => mt.TagId == targetId).Select(mt => mt.MovieId).ToList();
        Assert.Equal(3, targetMovies.Count);
        Assert.Equal(3, targetMovies.Distinct().Count());
    }
}
