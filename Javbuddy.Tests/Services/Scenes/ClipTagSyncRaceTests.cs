using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Two refreshes of one movie (the stored-actor worker's and an editor's) can both find the MovieTag link missing and
/// both add it; the unique key refuses the second, which must start over rather than fail.</summary>
public sealed class ClipTagSyncRaceTests : IDisposable
{
    private readonly TestDbContextFactory factory;
    private readonly RacingRefresh racing = new();

    public ClipTagSyncRaceTests()
    {
        factory = new TestDbContextFactory(racing);
        racing.OtherContext = factory.CreateDbContext;
    }

    public void Dispose() => factory.Dispose();

    /// <summary>Adds the movie's tag link through another context the first time a save is about to, as a concurrent refresh would.</summary>
    private sealed class RacingRefresh : SaveChangesInterceptor
    {
        private bool armed;

        public Func<AppDbContext> OtherContext { get; set; } = null!;
        public int MovieId { get; set; }
        public int TagId { get; set; }

        public void Arm() => armed = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!armed) return ValueTask.FromResult(result);
            armed = false;
            using var other = OtherContext();
            other.MovieTags.Add(new MovieTag { MovieId = MovieId, TagId = TagId, FromClips = true });
            other.SaveChanges();
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task WhenAnotherRefreshAddedTheLinkFirst_StartsOverInsteadOfFailing()
    {
        int movieId, tagId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "RACE-001" };
            var actor = new Actor { FirstName = "Mei" };
            var tag = new Tag { Name = "Blonde", IsActorTag = true };
            db.AddRange(movie, actor, tag);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
            db.MovieActorTags.Add(new MovieActorTag { MovieId = movie.Id, ActorId = actor.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
            (movieId, tagId) = (movie.Id, tag.Id);
        }
        (racing.MovieId, racing.TagId) = (movieId, tagId);
        racing.Arm();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // Whoever won saved the same row as FromClips; there is nothing left for the loser to change.
            Assert.False(await ClipTagSync.RefreshAsync(db, movieId, CancellationToken.None));
        }

        await using var check = await factory.CreateDbContextAsync();
        var link = await check.MovieTags.SingleAsync();
        Assert.True(link.FromClips);
    }
}
