using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Tags;

public class TagNormalizationTests
{
    [Fact]
    public async Task ApplyToMovieAsync_CreatesNewTagsAndSetsNeedsReview()
    {
        using var factory = new TestDbContextFactory();
        var movie = new Movie { Code = "ABC-001", MetaGenres = "VR, Solowork" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.OrderBy(t => t.Name).ToListAsync();
        Assert.Equal(["Solowork", "VR"], tags.Select(t => t.Name).ToArray());
        Assert.All(tags, t => Assert.True(t.NeedsReview));

        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("Solowork, VR", reloaded!.MetaGenres);

        var links = await verifyDb.MovieTags.Where(mt => mt.MovieId == movie.Id).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task ApplyToMovieAsync_WithRawGenreList_KeepsGenreNameContainingCommaAsOneTag()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // MetaGenres already holds the comma-joined display text a mapper would have written
            // (MovieMetadataMapper.FormatGenres) — indistinguishable, once joined, from three plain
            // genres. The raw list passed alongside it is what still knows "Nasty, hardcore" was one
            // genre's own name.
            movie = new Movie { Code = "ABC-011", MetaGenres = "Nasty, hardcore, VR" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!, ["Nasty, hardcore", "VR"]);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.OrderBy(t => t.Name).ToListAsync();
        Assert.Equal(["Nasty, hardcore", "VR"], tags.Select(t => t.Name).ToArray());

        var links = await verifyDb.MovieTags.Where(mt => mt.MovieId == movie.Id).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task ApplyToMovieAsync_ConcurrentImportsDiscoveringSameNewGenre_DoNotRaceOnUniqueIndex()
    {
        using var factory = new TestDbContextFactory();
        int movie1Id, movie2Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie1 = new Movie { Code = "ABC-009", MetaGenres = "BrandNewGenre" };
            var movie2 = new Movie { Code = "ABC-010", MetaGenres = "BrandNewGenre" };
            db.Movies.AddRange(movie1, movie2);
            await db.SaveChangesAsync();
            movie1Id = movie1.Id;
            movie2Id = movie2.Id;
        }

        // Simulates LibraryRescanTask.ImportNewMoviesAsync running several ImportOneAsync calls
        // concurrently (each on its own AppDbContext) and discovering the same never-seen-before
        // genre value at the same time — this used to race on the Tags.Name unique index.
        async Task ApplyAsync(int movieId)
        {
            await using var db = await factory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            await TagNormalization.ApplyToMovieAsync(db, movie!);
        }

        await Task.WhenAll(ApplyAsync(movie1Id), ApplyAsync(movie2Id));

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.Where(t => t.Name == "BrandNewGenre").ToListAsync();
        Assert.Single(tags);

        var links = await verifyDb.MovieTags.Where(mt => mt.TagId == tags[0].Id).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task ApplyToMovieAsync_ReusesExistingTag_CaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "Solo" });
            movie = new Movie { Code = "ABC-002", MetaGenres = "solo" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verifyDb.Tags.CountAsync());
    }

    [Fact]
    public async Task ApplyToMovieAsync_StripsIgnoredValues_WithoutCreatingATag()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.IgnoredTags.Add(new IgnoredTag { Value = "Sample", MatchMode = TagMatchMode.CaseInsensitive });
            movie = new Movie { Code = "ABC-003", MetaGenres = "sample, VR" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.ToListAsync();
        Assert.Single(tags);
        Assert.Equal("VR", tags[0].Name);

        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("VR", reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ApplyToMovieAsync_ResolvesReplacementRule_InsteadOfCreatingSourceTag()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Solo" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = target.Id });
            movie = new Movie { Code = "ABC-004", MetaGenres = "Solowork" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.ToListAsync();
        Assert.Single(tags);
        Assert.Equal("Solo", tags[0].Name);

        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("Solo", reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ApplyToMovieAsync_RemovesStaleLinks_WhenReapplied()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            movie = new Movie { Code = "ABC-005", MetaGenres = "VR, Solo" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            await TagNormalization.ApplyToMovieAsync(db, movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            tracked!.MetaGenres = "VR";
            await TagNormalization.ApplyToMovieAsync(db, tracked);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var links = await verifyDb.MovieTags.Where(mt => mt.MovieId == movie.Id).ToListAsync();
        Assert.Single(links);
    }

    [Fact]
    public async Task NormalizeRawValuesAsync_DoesNotCreateAnyTagRows()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var solo = new Tag { Name = "Solo" };
        db.Tags.Add(solo);
        await db.SaveChangesAsync();
        db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = solo.Id });
        await db.SaveChangesAsync();

        var normalized = await TagNormalization.NormalizeRawValuesAsync(db, ["Solowork", "BrandNewGenre"]);

        Assert.Equal(["Solo", "BrandNewGenre"], normalized);
        Assert.Equal(1, await db.Tags.CountAsync());
    }

    [Fact]
    public async Task ApplyToLibraryAsync_DeletesOrphanedTagsMatchingIgnoreList()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            movie = new Movie { Code = "ABC-006", MetaGenres = "Sample" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            await TagNormalization.ApplyToMovieAsync(db, movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Tags.CountAsync());
            db.IgnoredTags.Add(new IgnoredTag { Value = "Sample", MatchMode = TagMatchMode.CaseInsensitive });
            await db.SaveChangesAsync();

            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(0, await verifyDb.Tags.CountAsync());
        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Null(reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ApplyToMovieAsync_AutoIgnoresNonLatinScript_WhenSettingEnabled()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TagSettings.Add(new TagSettings { AutoIgnoreNonLatinTags = true });
            movie = new Movie { Code = "ABC-007", MetaGenres = "単体作品, VR" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.ToListAsync();
        Assert.Single(tags);
        Assert.Equal("VR", tags[0].Name);
    }

    [Fact]
    public async Task SyncMetaGenresAsync_RebuildsTextFromCurrentLinks()
    {
        using var factory = new TestDbContextFactory();
        Movie movie;
        Tag tag;
        await using (var db = await factory.CreateDbContextAsync())
        {
            tag = new Tag { Name = "Solo" };
            db.Tags.Add(tag);
            movie = new Movie { Code = "ABC-008", MetaGenres = "stale text" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.SyncMetaGenresAsync(db, [movie.Id]);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("Solo", reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ApplyToMovieAsync_HierarchicalString_CreatesParentAndChildTags()
    {
        using var factory = new TestDbContextFactory();
        var movie = new Movie { Code = "ABC-009", MetaGenres = "Cosplay##ram" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var parentTag = await verifyDb.Tags.FirstOrDefaultAsync(t => t.Name == "Cosplay");
        var childTag = await verifyDb.Tags.Include(t => t.ParentTag).FirstOrDefaultAsync(t => t.Name == "ram");

        Assert.NotNull(parentTag);
        Assert.NotNull(childTag);
        Assert.Equal(parentTag.Id, childTag.ParentTagId);
        Assert.True(parentTag.NeedsReview);
        Assert.True(childTag.NeedsReview);

        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("Cosplay##ram", reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ApplyToMovieAsync_DeduplicatesParent_WhenSubtagIsPresent()
    {
        using var factory = new TestDbContextFactory();
        var movie = new Movie { Code = "ABC-010" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var parent = new Tag { Name = "Cosplay" };
            var child = new Tag { Name = "ram", ParentTag = parent };
            db.Tags.AddRange(parent, child);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movie.Id);
            await TagNormalization.ApplyToMovieAsync(db, tracked!, ["Cosplay", "ram"]);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var links = await verifyDb.MovieTags.Include(mt => mt.Tag).Where(mt => mt.MovieId == movie.Id).ToListAsync();
        Assert.Single(links);
        Assert.Equal("ram", links[0].Tag.Name);

        var reloaded = await verifyDb.Movies.FindAsync(movie.Id);
        Assert.Equal("Cosplay##ram", reloaded!.MetaGenres);
    }

    [Fact]
    public async Task ResolveRawValuesAsync_ReportsRenameIgnoreKnownAndNewPerValue()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var solo = new Tag { Name = "Solo" };
        var vr = new Tag { Name = "VR" };
        db.Tags.AddRange(solo, vr);
        await db.SaveChangesAsync();
        db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = solo.Id });
        db.IgnoredTags.Add(new IgnoredTag { Value = "Sample", MatchMode = TagMatchMode.CaseInsensitive });
        await db.SaveChangesAsync();

        var resolved = await TagNormalization.ResolveRawValuesAsync(db, ["Solowork", "Sample", "vr", "BrandNew", "  "]);

        Assert.Equal(4, resolved.Count);
        Assert.Equal(new GenreResolution("Solowork", "Solo", true), resolved[0]);
        Assert.True(resolved[0].IsRenamed);
        Assert.True(resolved[1].IsIgnored);
        Assert.Equal(new GenreResolution("vr", "vr", true), resolved[2]);
        Assert.False(resolved[2].IsRenamed);
        Assert.Equal(new GenreResolution("BrandNew", "BrandNew", false), resolved[3]);
    }
}
