using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tags;

/// <summary>MovieTag's IsExplicit/FromClips flags: a row is the movie's effective tag when
/// either is set; metadata ingestion never turns a clip-only row explicit. Every assertion re-reads
/// through a fresh context so the stored value is checked, not the tracked one.</summary>
public class MovieTagFlagsTests
{
    private static TagService CreateService(TestDbContextFactory factory) => new(factory, Substitute.For<INfoSyncService>());

    private static async Task<(int MovieId, Dictionary<string, int> TagIds)> SeedAsync(TestDbContextFactory factory, string? metaGenres, params (string Tag, bool IsExplicit, bool FromClips)[] links)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-561", MetaGenres = metaGenres };
        db.Movies.Add(movie);
        var tags = links.Select(l => l.Tag).Concat(metaGenres?.Split(", ") ?? []).Distinct()
            .ToDictionary(name => name, name => new Tag { Name = name });
        db.Tags.AddRange(tags.Values);
        await db.SaveChangesAsync();
        foreach (var (tag, isExplicit, fromClips) in links)
        {
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tags[tag].Id, IsExplicit = isExplicit, FromClips = fromClips });
        }
        await db.SaveChangesAsync();
        return (movie.Id, tags.ToDictionary(kv => kv.Key, kv => kv.Value.Id));
    }

    private static async Task<Dictionary<string, (bool IsExplicit, bool FromClips)>> LinksAsync(TestDbContextFactory factory, int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.MovieTags.Where(mt => mt.MovieId == movieId)
                .Select(mt => new { mt.Tag.Name, mt.IsExplicit, mt.FromClips })
                .ToListAsync())
            .ToDictionary(mt => mt.Name, mt => (mt.IsExplicit, mt.FromClips));
    }

    [Fact]
    public async Task ClipOnlyRow_RoundTripsAsNotExplicit()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, _) = await SeedAsync(factory, null, ("Squirt", false, true));

        Assert.Equal((false, true), (await LinksAsync(factory, movieId))["Squirt"]);
    }

    [Fact]
    public async Task NewRow_DefaultsToExplicit()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "Solo" });
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = db.Tags.Single(t => t.Name == "Solo").Id });
            await db.SaveChangesAsync();
        }

        Assert.Equal((true, false), (await LinksAsync(factory, movieId))["Solo"]);
    }

    [Fact]
    public async Task AddTagToMovie_OnAClipOnlyRow_MakesItExplicit_WithoutDuplicateRow()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Squirt", ("Squirt", false, true));

        var result = await CreateService(factory).AddTagToMovieAsync(movieId, tagIds["Squirt"]);

        Assert.True(result.Success, result.ErrorMessage);
        var links = await LinksAsync(factory, movieId);
        Assert.Single(links);
        Assert.Equal((true, true), links["Squirt"]);
    }

    [Fact]
    public async Task AddTagToMovie_OnAnExplicitRow_StillFails()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Squirt", ("Squirt", true, true));

        var result = await CreateService(factory).AddTagToMovieAsync(movieId, tagIds["Squirt"]);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RemoveTagFromMovie_WhenAlsoFromClips_KeepsRowAsClipOnly()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Squirt", ("Squirt", true, true));

        var result = await CreateService(factory).RemoveTagFromMovieAsync(movieId, tagIds["Squirt"]);

        Assert.True(result.Success);
        Assert.Equal((false, true), (await LinksAsync(factory, movieId))["Squirt"]);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("Squirt", (await db.Movies.SingleAsync(m => m.Id == movieId)).MetaGenres);
    }

    [Fact]
    public async Task RemoveTagFromMovieAndClips_StripsTheTagFromScenesHighlightsAndApexes()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Squirt", ("Squirt", false, true));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var scene = new Scene { MovieId = movieId, Title = "S", StartSeconds = 0, EndSeconds = 10 };
            var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 1, EndSeconds = 2 };
            db.Scenes.Add(scene);
            db.MovieHighlights.Add(highlight);
            await db.SaveChangesAsync();
            db.SceneTags.Add(new SceneTag { SceneId = scene.Id, TagId = tagIds["Squirt"] });
            db.HighlightTags.Add(new HighlightTag { HighlightId = highlight.Id, TagId = tagIds["Squirt"] });
            await db.SaveChangesAsync();
        }

        var result = await CreateService(factory).RemoveTagFromMovieAndClipsAsync(movieId, tagIds["Squirt"]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(await LinksAsync(factory, movieId));
        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.SceneTags.ToListAsync());
        Assert.Empty(await check.HighlightTags.ToListAsync());
        Assert.Null((await check.Movies.SingleAsync(m => m.Id == movieId)).MetaGenres);
    }

    [Fact]
    public async Task RemoveTagFromMovie_WhenExplicitOnly_DeletesRow()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Squirt", ("Squirt", true, false));

        await CreateService(factory).RemoveTagFromMovieAsync(movieId, tagIds["Squirt"]);

        Assert.Empty(await LinksAsync(factory, movieId));
    }

    [Fact]
    public async Task Normalization_Apply_KeepsClipOnlyRows_AndDoesNotPromoteThem()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, _) = await SeedAsync(factory, "Solo", ("Solo", true, false), ("Squirt", false, true), ("Creampie", false, true));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = await db.Movies.SingleAsync(m => m.Id == movieId);
            // "Squirt" comes back as metadata, e.g. read from an .nfo that lists clip tags.
            await TagNormalization.ApplyToMovieAsync(db, movie, ["Solo", "Squirt"]);
        }

        var links = await LinksAsync(factory, movieId);
        Assert.Equal((true, false), links["Solo"]);
        Assert.Equal((false, true), links["Squirt"]);
        Assert.Equal((false, true), links["Creampie"]);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal("Creampie, Solo, Squirt", (await verifyDb.Movies.SingleAsync(m => m.Id == movieId)).MetaGenres);
    }

    [Fact]
    public async Task Normalization_Apply_ClearsExplicitWhenTagDropsFromMetadata()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, _) = await SeedAsync(factory, "Solo, Squirt", ("Solo", true, false), ("Squirt", true, true));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = await db.Movies.SingleAsync(m => m.Id == movieId);
            await TagNormalization.ApplyToMovieAsync(db, movie, ["Other"]);
        }

        var links = await LinksAsync(factory, movieId);
        Assert.False(links.ContainsKey("Solo"));
        Assert.Equal((false, true), links["Squirt"]);
        Assert.Equal((true, false), links["Other"]);
    }

    [Fact]
    public async Task ApplyToLibrary_DoesNotPromoteClipOnlyRows()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, null, ("Solo", true, false), ("Squirt", false, true));
        await using (var db = await factory.CreateDbContextAsync())
        {
            // An apex really carries it, so the library pass's clip refresh keeps the row.
            var apex = new MovieApex { MovieId = movieId, Seconds = 10 };
            apex.ApexTags.Add(new ApexTag { TagId = tagIds["Squirt"] });
            db.MovieApexes.Add(apex);
            await db.SaveChangesAsync();
            await TagNormalization.SyncMetaGenresAsync(db, [movieId]);
            await db.SaveChangesAsync();
            Assert.Equal("Solo, Squirt", (await db.Movies.SingleAsync(m => m.Id == movieId)).MetaGenres);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        Assert.Equal((false, true), (await LinksAsync(factory, movieId))["Squirt"]);
    }

    [Fact]
    public async Task MergeTags_CombinesFlags()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, "Solowork", ("Solowork", true, false), ("Solo", false, true));

        var result = await CreateService(factory).MergeAsync(tagIds["Solowork"], tagIds["Solo"]);

        Assert.True(result.Success, result.ErrorMessage);
        var links = await LinksAsync(factory, movieId);
        Assert.Single(links);
        Assert.Equal((true, true), links["Solo"]);
    }

    [Fact]
    public async Task MergeManyTags_CombinesFlags_AndCopiesThemOntoANewTargetRow()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, tagIds) = await SeedAsync(factory, null, ("A", false, true), ("B", true, false), ("Target", false, false));
        await using (var db = await factory.CreateDbContextAsync())
        {
            // "Target" was only seeded to create the tag; drop its link so the merge must create it.
            db.MovieTags.RemoveRange(db.MovieTags.Where(mt => mt.TagId == tagIds["Target"]));
            await db.SaveChangesAsync();
        }

        var result = await CreateService(factory).MergeManyAsync([tagIds["A"], tagIds["B"]], tagIds["Target"]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal((true, true), (await LinksAsync(factory, movieId))["Target"]);
    }
}
