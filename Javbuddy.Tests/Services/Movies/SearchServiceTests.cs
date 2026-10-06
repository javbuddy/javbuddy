using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Movies;

public class SearchServiceTests
{
    [Fact]
    public async Task SearchAsync_MatchesMovieByCodeTitleOrMetaTitle()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "SIVR-505" },
                new Movie { Code = "ABC-001", MetaTitle = "A Movie With SIVR In The Title" },
                new Movie { Code = "XYZ-002", Title = "Manual title mentions SIVR too" },
                new Movie { Code = "NOMATCH-1" });
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory);
        var result = await service.SearchAsync("SIVR");

        Assert.Equal(3, result.Movies.Count);
        Assert.DoesNotContain(result.Movies, m => m.Code == "NOMATCH-1");
    }

    // A crop rewrites the movie's cached poster, so its thumbnail URL must change with it, and match
    // the Movies grid's so both share one cached copy.
    [Fact]
    public async Task SearchAsync_PosterVersionFollowsCachedPosterAndMatchesGrid()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaCoverUrl = "https://example.com/a.jpg" });
            db.CachedImages.Add(new CachedImage { Code = "SIVR-505", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) });
            await db.SaveChangesAsync();
        }
        var service = new SearchService(factory);

        var before = Assert.Single((await service.SearchAsync("SIVR")).Movies).PosterVersion;
        var grid = await new MovieGridQueryService(factory).GetRangeAsync(new MovieGridFilter(), new MovieGridSort("title", false, 1), 0, 10);
        Assert.Equal(grid[0].PosterVersion, before);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Single().UpdatedAt = new DateTime(2026, 2, 1);
            await db.SaveChangesAsync();
        }

        Assert.NotEqual(before, Assert.Single((await service.SearchAsync("SIVR")).Movies).PosterVersion);
    }

    [Fact]
    public async Task SearchAsync_RanksPrefixMatchesBeforeSubstringMatches()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "XSIVR-001" }, // SIVR appears mid-string
                new Movie { Code = "SIVR-002" });  // SIVR is a prefix
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory);
        var result = await service.SearchAsync("SIVR");

        Assert.Equal("SIVR-002", result.Movies[0].Code);
        Assert.Equal("XSIVR-001", result.Movies[1].Code);
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 10; i++)
            {
                db.Movies.Add(new Movie { Code = $"AAA-{i:000}" });
            }
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory);
        var result = await service.SearchAsync("AAA", limit: 3);

        Assert.Equal(3, result.Movies.Count);
    }

    [Fact]
    public async Task SearchAsync_MatchesActorsByName()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Hatano", LastName = "Yui" },
                new Actor { FirstName = "Match", LastName = "No" });
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory);
        var result = await service.SearchAsync("Hatano");

        var actor = Assert.Single(result.Actors);
        Assert.Equal("Yui Hatano", actor.DisplayName);
    }

    [Fact]
    public async Task SearchAsync_ReportsWhetherMatchedActorsHaveCachedThumbnails()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actorWithImage = new Actor { FirstName = "Hatano", LastName = "Yui" };
            var actorWithoutImage = new Actor { FirstName = "Hatano", LastName = "NoImage" };
            db.Actors.AddRange(actorWithImage, actorWithoutImage);
            await db.SaveChangesAsync();

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorWithImage.Id,
                SourceMovieCode = "AAA-001",
                Variant = "thumb",
                StorageId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var service = new SearchService(factory);
        var result = await service.SearchAsync("Hatano");

        Assert.True(result.Actors.Single(a => a.DisplayName == "Yui Hatano").HasImage);
        Assert.False(result.Actors.Single(a => a.DisplayName == "NoImage Hatano").HasImage);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmptyLists()
    {
        using var factory = new TestDbContextFactory();
        var service = new SearchService(factory);

        var result = await service.SearchAsync("nothing-matches-this");

        Assert.Empty(result.Movies);
        Assert.Empty(result.Actors);
    }
}
