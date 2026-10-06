using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>MovieTag.FromClips follows the explicit tags of the movie's scenes, highlights and apexes.</summary>
public class ClipTagSyncTests
{
    private sealed record Seeded(int MovieId, int OtherMovieId, int SceneId, int HighlightId, int ApexId, int[] TagIds);

    private static async Task<Seeded> SeedAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-561", MediaDurationSeconds = 900 };
        var other = new Movie { Code = "ABC-562", MediaDurationSeconds = 900 };
        var tags = new[] { new Tag { Name = "A" }, new Tag { Name = "B" }, new Tag { Name = "C" } };
        db.AddRange(movie, other);
        db.Tags.AddRange(tags);
        await db.SaveChangesAsync();
        var scene = new Scene { MovieId = movie.Id, StartSeconds = 0, EndSeconds = 300 };
        var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = 10, EndSeconds = 50 };
        var apex = new MovieApex { MovieId = movie.Id, Seconds = 20 };
        var otherApex = new MovieApex { MovieId = other.Id, Seconds = 20 };
        db.AddRange(scene, highlight, apex, otherApex);
        await db.SaveChangesAsync();
        // Another movie's clip tags never reach this movie.
        db.ApexTags.Add(new ApexTag { ApexId = otherApex.Id, TagId = tags[2].Id });
        await db.SaveChangesAsync();
        return new Seeded(movie.Id, other.Id, scene.Id, highlight.Id, apex.Id, tags.Select(t => t.Id).ToArray());
    }

    private static async Task<bool> RefreshAsync(TestDbContextFactory factory, int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await ClipTagSync.RefreshAsync(db, movieId, CancellationToken.None);
    }

    private static async Task<Dictionary<int, (bool IsExplicit, bool FromClips)>> LinksAsync(TestDbContextFactory factory, int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.MovieTags.Where(mt => mt.MovieId == movieId).ToListAsync())
            .ToDictionary(mt => mt.TagId, mt => (mt.IsExplicit, mt.FromClips));
    }

    private static async Task ExecAsync(TestDbContextFactory factory, Func<Javbuddy.Data.AppDbContext, Task> action)
    {
        await using var db = await factory.CreateDbContextAsync();
        await action(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ApexTag_CreatesAClipOnlyMovieRow()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        await ExecAsync(factory, db => { db.ApexTags.Add(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds[0] }); return Task.CompletedTask; });

        Assert.True(await RefreshAsync(factory, s.MovieId));

        Assert.Equal(new Dictionary<int, (bool, bool)> { [s.TagIds[0]] = (false, true) }, await LinksAsync(factory, s.MovieId));
        Assert.Empty(await LinksAsync(factory, s.OtherMovieId));
    }

    [Fact]
    public async Task ExplicitRow_AlsoOnAClip_GainsTheFlag()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        await ExecAsync(factory, db =>
        {
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds[0] });
            db.SceneTags.Add(new SceneTag { SceneId = s.SceneId, TagId = s.TagIds[0] });
            return Task.CompletedTask;
        });

        Assert.True(await RefreshAsync(factory, s.MovieId));

        Assert.Equal((true, true), (await LinksAsync(factory, s.MovieId))[s.TagIds[0]]);
    }

    [Fact]
    public async Task ClipTagRemoved_DropsTheClipOnlyRow_AndClearsTheFlagOnAnExplicitOne()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        await ExecAsync(factory, db =>
        {
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds[0], IsExplicit = true, FromClips = true });
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds[1], IsExplicit = false, FromClips = true });
            return Task.CompletedTask;
        });

        Assert.True(await RefreshAsync(factory, s.MovieId));

        Assert.Equal(new Dictionary<int, (bool, bool)> { [s.TagIds[0]] = (true, false) }, await LinksAsync(factory, s.MovieId));
    }

    [Fact]
    public async Task SameTagOnSceneHighlightAndApex_IsOneRow()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        await ExecAsync(factory, db =>
        {
            db.SceneTags.Add(new SceneTag { SceneId = s.SceneId, TagId = s.TagIds[1] });
            db.HighlightTags.Add(new HighlightTag { HighlightId = s.HighlightId, TagId = s.TagIds[1] });
            db.ApexTags.Add(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds[1] });
            return Task.CompletedTask;
        });

        await RefreshAsync(factory, s.MovieId);

        Assert.Equal(new Dictionary<int, (bool, bool)> { [s.TagIds[1]] = (false, true) }, await LinksAsync(factory, s.MovieId));
    }

    [Fact]
    public async Task NothingChanged_ReturnsFalse()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        await ExecAsync(factory, db => { db.HighlightTags.Add(new HighlightTag { HighlightId = s.HighlightId, TagId = s.TagIds[0] }); return Task.CompletedTask; });
        Assert.True(await RefreshAsync(factory, s.MovieId));

        Assert.False(await RefreshAsync(factory, s.MovieId));
    }

    [Fact]
    public async Task Service_SyncsMetaGenresAndRechecksTheNfo_OnlyWhenSomethingChanged()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory);
        var nfo = Substitute.For<INfoSyncService>();
        var service = new ClipTagSyncService(factory, nfo);
        await ExecAsync(factory, db => { db.ApexTags.Add(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds[0] }); return Task.CompletedTask; });

        Assert.True(await service.RefreshAsync(s.MovieId));
        Assert.False(await service.RefreshAsync(s.MovieId));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("A", (await db.Movies.SingleAsync(m => m.Id == s.MovieId)).MetaGenres);
        await nfo.Received(1).CheckMovieNfoConflictAsync(s.MovieId, Arg.Any<CancellationToken>());
    }
}
