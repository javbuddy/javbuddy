using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tags;

/// <summary>Tag merges and library normalization keep clip tags and the movie's FromClips flags in step
///.</summary>
public class ClipTagMaintenanceTests
{
    private sealed record Seeded(int MovieId, int SceneId, int HighlightId, int ApexId, Dictionary<string, int> TagIds);

    private static async Task<Seeded> SeedAsync(TestDbContextFactory factory, params string[] tagNames)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-561", MediaDurationSeconds = 900 };
        db.Movies.Add(movie);
        var tags = tagNames.ToDictionary(n => n, n => new Tag { Name = n });
        db.Tags.AddRange(tags.Values);
        await db.SaveChangesAsync();
        var scene = new Scene { MovieId = movie.Id, StartSeconds = 0, EndSeconds = 300 };
        var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = 10, EndSeconds = 50 };
        var apex = new MovieApex { MovieId = movie.Id, Seconds = 20 };
        db.AddRange(scene, highlight, apex);
        await db.SaveChangesAsync();
        return new Seeded(movie.Id, scene.Id, highlight.Id, apex.Id, tags.ToDictionary(kv => kv.Key, kv => kv.Value.Id));
    }

    private static async Task ExecAsync(TestDbContextFactory factory, Action<Javbuddy.Data.AppDbContext> action)
    {
        await using var db = await factory.CreateDbContextAsync();
        action(db);
        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, (bool IsExplicit, bool FromClips)>> MovieTagsAsync(TestDbContextFactory factory, int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.MovieTags.Where(mt => mt.MovieId == movieId).Select(mt => new { mt.Tag.Name, mt.IsExplicit, mt.FromClips }).ToListAsync())
            .ToDictionary(x => x.Name, x => (x.IsExplicit, x.FromClips));
    }

    private static TagService Service(TestDbContextFactory factory) => new(factory, Substitute.For<INfoSyncService>());

    [Fact]
    public async Task Merge_MovesHighlightAndApexTagsToTheTarget_AndTheMovieKeepsItThroughItsClips()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory, "Shiofuki", "Squirt");
        await ExecAsync(factory, db =>
        {
            db.ApexTags.Add(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds["Shiofuki"] });
            db.HighlightTags.Add(new HighlightTag { HighlightId = s.HighlightId, TagId = s.TagIds["Shiofuki"] });
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds["Shiofuki"], IsExplicit = false, FromClips = true });
        });

        Assert.True((await Service(factory).MergeAsync(s.TagIds["Shiofuki"], s.TagIds["Squirt"])).Success);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal([s.TagIds["Squirt"]], await db.ApexTags.Select(at => at.TagId).ToListAsync());
        Assert.Equal([s.TagIds["Squirt"]], await db.HighlightTags.Select(ht => ht.TagId).ToListAsync());
        Assert.Equal(new Dictionary<string, (bool, bool)> { ["Squirt"] = (false, true) }, await MovieTagsAsync(factory, s.MovieId));
    }

    [Fact]
    public async Task MergeMany_MovesHighlightAndApexTags_DedupingWhereTheTargetIsAlreadyThere()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory, "A", "B", "Target");
        await ExecAsync(factory, db =>
        {
            db.ApexTags.AddRange(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds["A"] }, new ApexTag { ApexId = s.ApexId, TagId = s.TagIds["Target"] });
            db.HighlightTags.Add(new HighlightTag { HighlightId = s.HighlightId, TagId = s.TagIds["B"] });
        });

        Assert.True((await Service(factory).MergeManyAsync([s.TagIds["A"], s.TagIds["B"]], s.TagIds["Target"])).Success);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal([s.TagIds["Target"]], await db.ApexTags.Select(at => at.TagId).ToListAsync());
        Assert.Equal([s.TagIds["Target"]], await db.HighlightTags.Select(ht => ht.TagId).ToListAsync());
    }

    [Fact]
    public async Task ApplyToLibrary_AnIgnoredSceneTag_AlsoDropsTheClipOnlyMovieTag()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory, "Solo", "Sample");
        await ExecAsync(factory, db =>
        {
            db.SceneTags.Add(new SceneTag { SceneId = s.SceneId, TagId = s.TagIds["Sample"] });
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds["Solo"] });
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds["Sample"], IsExplicit = false, FromClips = true });
            db.Movies.Single(m => m.Id == s.MovieId).MetaGenres = "Sample, Solo";
            db.IgnoredTags.Add(new IgnoredTag { Value = "Sample", MatchMode = TagMatchMode.CaseInsensitive });
        });

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        Assert.Equal(new Dictionary<string, (bool, bool)> { ["Solo"] = (true, false) }, await MovieTagsAsync(factory, s.MovieId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal("Solo", (await verify.Movies.SingleAsync(m => m.Id == s.MovieId)).MetaGenres);
    }

    [Fact]
    public async Task ApplyToLibrary_HealsStaleClipFlags()
    {
        using var factory = new TestDbContextFactory();
        var s = await SeedAsync(factory, "Solo", "Stale", "Missing");
        await ExecAsync(factory, db =>
        {
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds["Solo"] });
            // A clip-only row no clip carries any more, and an apex tag the movie never got.
            db.MovieTags.Add(new MovieTag { MovieId = s.MovieId, TagId = s.TagIds["Stale"], IsExplicit = false, FromClips = true });
            db.ApexTags.Add(new ApexTag { ApexId = s.ApexId, TagId = s.TagIds["Missing"] });
            db.Movies.Single(m => m.Id == s.MovieId).MetaGenres = "Solo, Stale";
        });

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        Assert.Equal(new Dictionary<string, (bool, bool)> { ["Solo"] = (true, false), ["Missing"] = (false, true) }, await MovieTagsAsync(factory, s.MovieId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal("Missing, Solo", (await verify.Movies.SingleAsync(m => m.Id == s.MovieId)).MetaGenres);
    }
}
