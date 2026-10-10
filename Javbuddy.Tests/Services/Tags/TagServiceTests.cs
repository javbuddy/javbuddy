using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tags;

public class TagServiceTests
{
    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public string AddMovieFolder(string folderName)
        {
            var folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Most tests below don't touch .nfo conflict detection at all, so they get a
    /// no-op fake — only the tests exercising AddTagToMovieAsync/RemoveTagFromMovieAsync's
    /// immediate conflict re-check need a real NfoSyncService (see CreateServiceWithRealNfoSync).</summary>
    private static TagService CreateService(TestDbContextFactory factory) =>
        new(factory, Substitute.For<INfoSyncService>());

    private static (TagService Service, NfoSyncService NfoSync) CreateServiceWithRealNfoSync(TestDbContextFactory factory, TempRoot root)
    {
        var inMemory = new Dictionary<string, string?> { ["LocalLibrary:RootPaths:0"] = root.Path };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var localLibraryClient = new LocalLibraryClient(
            factory,
            config,
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IMediaInfoProber>(),
            NullLogger<LocalLibraryClient>.Instance);
        var nfoSync = new NfoSyncService(factory, localLibraryClient, NullLogger<NfoSyncService>.Instance);
        return (new TagService(factory, nfoSync), nfoSync);
    }

    [Fact]
    public async Task CreateTagAsync_RejectsEmptyName()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.CreateTagAsync("   ");

        Assert.False(result.Success);
        Assert.Equal("Tag name is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateTagAsync_RejectsNameExceeding200Chars()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.CreateTagAsync(new string('a', 201));

        Assert.False(result.Success);
        Assert.Equal("Tag name cannot exceed 200 characters.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateTagAsync_RejectsDuplicateName_CaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "Cosplay" });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var result = await service.CreateTagAsync("  cosplay  ");

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateTagAsync_CreatesTag_WithTrimmedName_AndNeedsReviewFalse()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.CreateTagAsync("  Cosplay  ");

        Assert.True(result.Success);
        Assert.NotNull(result.Tag);
        Assert.Equal("Cosplay", result.Tag.Name);
        Assert.False(result.Tag.NeedsReview);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tag = await verifyDb.Tags.FirstOrDefaultAsync(t => t.Name == "Cosplay");
        Assert.NotNull(tag);
        Assert.False(tag.NeedsReview);
    }

    [Fact]
    public async Task CreateTagAsync_TagIsImmediatelyAvailableInGetTagsAsync()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.CreateTagAsync("New Custom Tag");
        Assert.True(result.Success);

        var allTags = await service.GetTagsAsync();
        var item = Assert.Single(allTags, t => t.Name == "New Custom Tag");
        Assert.False(item.NeedsReview);
        Assert.Equal(0, item.MovieCount);
    }

    [Fact]
    public async Task RenameAsync_RejectsEmptyName()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.RenameAsync(1, "   ");

        Assert.False(result.Success);
        Assert.Equal("Tag name is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task RenameAsync_RejectsDuplicateName_CaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        int tagId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "VR" });
            var solo = new Tag { Name = "Solo" };
            db.Tags.Add(solo);
            await db.SaveChangesAsync();
            tagId = solo.Id;
        }

        var service = CreateService(factory);
        var result = await service.RenameAsync(tagId, "vr");

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task RenameAsync_UpdatesLinkedMoviesMetaGenres_AndClearsNeedsReview()
    {
        using var factory = new TestDbContextFactory();
        int tagId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Solowork", NeedsReview = true };
            db.Tags.Add(tag);
            var movie = new Movie { Code = "ABC-100", MetaGenres = "Solowork" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.RenameAsync(tagId, "Solo");
        Assert.True(result.Success);
        Assert.False(result.Tag!.NeedsReview);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.Equal("Solo", reloadedMovie!.MetaGenres);
    }

    [Fact]
    public async Task MergeAsync_RejectsSelfMerge()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.MergeAsync(1, 1);

        Assert.False(result.Success);
        Assert.Equal("Cannot merge a tag into itself.", result.ErrorMessage);
    }

    [Fact]
    public async Task MergeAsync_RepointsMovies_DedupsAndDeletesSource()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId, sharedMovieId, sourceOnlyMovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Tag { Name = "Solowork" };
            var target = new Tag { Name = "Solo" };
            db.Tags.AddRange(source, target);
            var shared = new Movie { Code = "ABC-101", MetaGenres = "Solo, Solowork" };
            var sourceOnly = new Movie { Code = "ABC-102", MetaGenres = "Solowork" };
            db.Movies.AddRange(shared, sourceOnly);
            await db.SaveChangesAsync();

            db.MovieTags.AddRange(
                new MovieTag { MovieId = shared.Id, TagId = source.Id },
                new MovieTag { MovieId = shared.Id, TagId = target.Id },
                new MovieTag { MovieId = sourceOnly.Id, TagId = source.Id });
            await db.SaveChangesAsync();

            sourceId = source.Id;
            targetId = target.Id;
            sharedMovieId = shared.Id;
            sourceOnlyMovieId = sourceOnly.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeAsync(sourceId, targetId, createReplacementRule: true);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == sourceId));

        var sharedLinks = await verifyDb.MovieTags.Where(mt => mt.MovieId == sharedMovieId).ToListAsync();
        Assert.Single(sharedLinks);
        Assert.Equal(targetId, sharedLinks[0].TagId);

        var sourceOnlyLinks = await verifyDb.MovieTags.Where(mt => mt.MovieId == sourceOnlyMovieId).ToListAsync();
        Assert.Single(sourceOnlyLinks);
        Assert.Equal(targetId, sourceOnlyLinks[0].TagId);

        var sharedMovie = await verifyDb.Movies.FindAsync(sharedMovieId);
        Assert.Equal("Solo", sharedMovie!.MetaGenres);

        var rule = await verifyDb.TagReplacementRules.SingleAsync();
        Assert.Equal("Solowork", rule.SourceValue);
        Assert.Equal(targetId, rule.TargetTagId);
    }

    [Fact]
    public async Task MergeAsync_RepointsExistingRulesTargetingSource_InsteadOfCascadingDelete()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Tag { Name = "Solowork" };
            var target = new Tag { Name = "Solo" };
            db.Tags.AddRange(source, target);
            await db.SaveChangesAsync();

            // An existing rule already maps "単体作品" onto the tag we're about to merge away.
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "単体作品", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = source.Id });
            await db.SaveChangesAsync();

            sourceId = source.Id;
            targetId = target.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeAsync(sourceId, targetId);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var rule = await verifyDb.TagReplacementRules.SingleAsync(r => r.SourceValue == "単体作品");
        Assert.Equal(targetId, rule.TargetTagId);
    }

    [Fact]
    public async Task MergeManyAsync_RejectsWhenNoSourcesRemainAfterExcludingTarget()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.MergeManyAsync([2], 2);

        Assert.False(result.Success);
        Assert.Equal("Select at least one tag to merge, other than the target.", result.ErrorMessage);
    }

    [Fact]
    public async Task MergeManyAsync_RepointsMoviesFromAllSources_DedupsAndDeletesSources()
    {
        using var factory = new TestDbContextFactory();
        int sourceOneId, sourceTwoId, targetId, sharedMovieId, sourceOnlyMovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var sourceOne = new Tag { Name = "Solowork" };
            var sourceTwo = new Tag { Name = "Solo Work" };
            var target = new Tag { Name = "Solo" };
            db.Tags.AddRange(sourceOne, sourceTwo, target);
            var shared = new Movie { Code = "ABC-201", MetaGenres = "Solo, Solowork" };
            var sourceOnly = new Movie { Code = "ABC-202", MetaGenres = "Solo Work" };
            db.Movies.AddRange(shared, sourceOnly);
            await db.SaveChangesAsync();

            db.MovieTags.AddRange(
                new MovieTag { MovieId = shared.Id, TagId = sourceOne.Id },
                new MovieTag { MovieId = shared.Id, TagId = target.Id },
                new MovieTag { MovieId = sourceOnly.Id, TagId = sourceTwo.Id });
            await db.SaveChangesAsync();

            sourceOneId = sourceOne.Id;
            sourceTwoId = sourceTwo.Id;
            targetId = target.Id;
            sharedMovieId = shared.Id;
            sourceOnlyMovieId = sourceOnly.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeManyAsync([sourceOneId, sourceTwoId], targetId);
        Assert.True(result.Success);
        Assert.Equal(targetId, result.Tag!.Id);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == sourceOneId));
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == sourceTwoId));

        var sharedLinks = await verifyDb.MovieTags.Where(mt => mt.MovieId == sharedMovieId).ToListAsync();
        Assert.Single(sharedLinks);
        Assert.Equal(targetId, sharedLinks[0].TagId);

        var sourceOnlyLinks = await verifyDb.MovieTags.Where(mt => mt.MovieId == sourceOnlyMovieId).ToListAsync();
        Assert.Single(sourceOnlyLinks);
        Assert.Equal(targetId, sourceOnlyLinks[0].TagId);
    }

    [Fact]
    public async Task MergeManyAsync_WithCaseVariantDuplicateRulesAcrossSources_DedupsWhenRemappedOntoSameTarget()
    {
        using var factory = new TestDbContextFactory();
        int sourceOneId, sourceTwoId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var sourceOne = new Tag { Name = "Solowork" };
            var sourceTwo = new Tag { Name = "Solo Work" };
            var target = new Tag { Name = "Solo" };
            db.Tags.AddRange(sourceOne, sourceTwo, target);
            await db.SaveChangesAsync();

            // The (SourceValue, MatchMode) unique index uses SQLite's default case-sensitive
            // collation, so these two rows can coexist even though they're the same rule under the
            // app's case-insensitive matching — each already targets a different source tag.
            // Merging both sources onto the same target must dedup them rather than leave both
            // rows now pointing at the same target with equivalent (case-insensitive) values.
            db.TagReplacementRules.AddRange(
                new TagReplacementRule { SourceValue = "Uncensored", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = sourceOne.Id },
                new TagReplacementRule { SourceValue = "UNCENSORED", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = sourceTwo.Id });
            await db.SaveChangesAsync();

            sourceOneId = sourceOne.Id;
            sourceTwoId = sourceTwo.Id;
            targetId = target.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeManyAsync([sourceOneId, sourceTwoId], targetId);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var rules = await verifyDb.TagReplacementRules.Where(r => r.TargetTagId == targetId).ToListAsync();
        Assert.Single(rules);
    }

    [Fact]
    public async Task MergeManyAsync_KeepsCaseVariantExactRulesThatTargetTheSameSource()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, otherSourceId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Tag { Name = "Solowork" };
            var otherSource = new Tag { Name = "Solo Work" };
            var target = new Tag { Name = "Solo" };
            db.Tags.AddRange(source, otherSource, target);
            await db.SaveChangesAsync();

            // Exact rules are distinct per casing. Dedup only applies against rules already on the
            // target before a source is processed, never between one source's own rules.
            db.TagReplacementRules.AddRange(
                new TagReplacementRule { SourceValue = "solo", MatchMode = TagMatchMode.Exact, TargetTagId = source.Id },
                new TagReplacementRule { SourceValue = "SOLO", MatchMode = TagMatchMode.Exact, TargetTagId = source.Id });
            await db.SaveChangesAsync();

            sourceId = source.Id;
            otherSourceId = otherSource.Id;
            targetId = target.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeManyAsync([sourceId, otherSourceId], targetId);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var values = await verifyDb.TagReplacementRules.Where(r => r.TargetTagId == targetId).Select(r => r.SourceValue).OrderBy(v => v).ToListAsync();
        Assert.Equal(["SOLO", "solo"], values);
    }

    [Fact]
    public async Task MergeManyAsync_ReadQueryCount_StaysFlatAsSourceBatchGrows()
    {
        var small = await MergeManyWithOverlappingSourcesAsync(sourceCount: 2);
        var large = await MergeManyWithOverlappingSourcesAsync(sourceCount: 8);

        Assert.Equal(small, large);
    }

    [Fact]
    public async Task MergeManyAsync_WithCaseVariantSourceNames_CreatesOneReplacementRulePerCaseInsensitiveName()
    {
        using var factory = new TestDbContextFactory();
        int sourceOneId, sourceTwoId, sourceThreeId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Tag.Name's unique index is case-sensitive, so case-variant source tags can coexist.
            var sourceOne = new Tag { Name = "Uncensored" };
            var sourceTwo = new Tag { Name = "UNCENSORED" };
            var sourceThree = new Tag { Name = "Leaked" };
            var target = new Tag { Name = "Uncut" };
            db.Tags.AddRange(sourceOne, sourceTwo, sourceThree, target);
            await db.SaveChangesAsync();

            // A pre-existing rule for "leaked" (any target) means no new rule is created for it.
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "leaked", MatchMode = TagMatchMode.Exact, TargetTagId = target.Id });
            await db.SaveChangesAsync();

            sourceOneId = sourceOne.Id;
            sourceTwoId = sourceTwo.Id;
            sourceThreeId = sourceThree.Id;
            targetId = target.Id;
        }

        var service = CreateService(factory);
        var result = await service.MergeManyAsync([sourceOneId, sourceTwoId, sourceThreeId], targetId, createReplacementRule: true);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var rules = await verifyDb.TagReplacementRules.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal("leaked", rules[0].SourceValue);
        Assert.Equal("UNCENSORED", rules[1].SourceValue.ToUpperInvariant());
        Assert.All(rules, r => Assert.Equal(targetId, r.TargetTagId));
    }

    /// <summary>Merges <paramref name="sourceCount"/> sources into one target, where every source
    /// shares one movie with the target (duplicate links), owns one movie of its own, and carries
    /// both a unique rule and a case-variant of the same "alias" rule. Asserts the merged state and
    /// returns how many SELECTs the merge issued.</summary>
    private static async Task<int> MergeManyWithOverlappingSourcesAsync(int sourceCount)
    {
        var counter = new SelectCommandCounter();
        using var factory = new TestDbContextFactory(counter);
        var sourceIds = new List<int>();
        var ownMovieIds = new List<int>();
        int targetId, sharedMovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Target" };
            var shared = new Movie { Code = "SHR-001" };
            db.Tags.Add(target);
            db.Movies.Add(shared);
            var sources = Enumerable.Range(0, sourceCount).Select(i => new Tag { Name = $"Source {i}" }).ToList();
            var ownMovies = Enumerable.Range(0, sourceCount).Select(i => new Movie { Code = $"OWN-{i:000}" }).ToList();
            db.Tags.AddRange(sources);
            db.Movies.AddRange(ownMovies);
            await db.SaveChangesAsync();

            db.MovieTags.Add(new MovieTag { MovieId = shared.Id, TagId = target.Id });
            for (var i = 0; i < sourceCount; i++)
            {
                db.MovieTags.AddRange(
                    new MovieTag { MovieId = shared.Id, TagId = sources[i].Id },
                    new MovieTag { MovieId = ownMovies[i].Id, TagId = sources[i].Id });
                db.TagReplacementRules.AddRange(
                    new TagReplacementRule { SourceValue = CaseVariant("alias", i), MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = sources[i].Id },
                    new TagReplacementRule { SourceValue = $"Unique {i}", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = sources[i].Id });
            }
            await db.SaveChangesAsync();

            targetId = target.Id;
            sharedMovieId = shared.Id;
            sourceIds.AddRange(sources.Select(s => s.Id));
            ownMovieIds.AddRange(ownMovies.Select(m => m.Id));
        }

        var service = CreateService(factory);
        counter.Reset();
        var result = await service.MergeManyAsync(sourceIds, targetId, createReplacementRule: true);
        var selectCount = counter.Count;
        Assert.True(result.Success);
        Assert.Equal(targetId, result.Tag!.Id);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(targetId, (await verifyDb.Tags.SingleAsync()).Id);

        var sharedLink = await verifyDb.MovieTags.SingleAsync(mt => mt.MovieId == sharedMovieId);
        Assert.Equal(targetId, sharedLink.TagId);
        foreach (var movieId in ownMovieIds)
        {
            Assert.Equal(targetId, (await verifyDb.MovieTags.SingleAsync(mt => mt.MovieId == movieId)).TagId);
        }
        Assert.Equal("Target", (await verifyDb.Movies.FindAsync(sharedMovieId))!.MetaGenres);

        var rules = await verifyDb.TagReplacementRules.ToListAsync();
        Assert.All(rules, r => Assert.Equal(targetId, r.TargetTagId));
        Assert.Single(rules, r => r.SourceValue.Equals("alias", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sourceCount, rules.Count(r => r.SourceValue.StartsWith("Unique ", StringComparison.Ordinal)));
        Assert.Equal(sourceCount, rules.Count(r => r.SourceValue.StartsWith("Source ", StringComparison.Ordinal)));
        Assert.Equal(1 + sourceCount * 2, rules.Count);

        return selectCount;
    }

    /// <summary>A distinct casing of <paramref name="value"/> per <paramref name="variant"/>
    /// (bit n uppercases char n), for seeding case-variant rules the case-sensitive unique index
    /// allows side by side.</summary>
    private static string CaseVariant(string value, int variant) =>
        string.Concat(value.Select((c, i) => (variant & (1 << i)) != 0 ? char.ToUpperInvariant(c) : c));

    [Fact]
    public async Task IgnoreManyAsync_ReadQueryCount_StaysFlatAsTagBatchGrows()
    {
        var small = await IgnoreManyWithOverlappingTagsAsync(tagCount: 2);
        var large = await IgnoreManyWithOverlappingTagsAsync(tagCount: 8);

        Assert.Equal(small, large);
    }

    /// <summary>Ignores <paramref name="tagCount"/> tags that all share one movie (duplicate
    /// links) and each own one movie, one of them case-variant duplicating an already-ignored
    /// value. Asserts the resulting state and returns how many SELECTs the ignore issued.</summary>
    private static async Task<int> IgnoreManyWithOverlappingTagsAsync(int tagCount)
    {
        var counter = new SelectCommandCounter();
        using var factory = new TestDbContextFactory(counter);
        var tagIds = new List<int>();
        int keptId, sharedMovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var kept = new Tag { Name = "Kept" };
            var shared = new Movie { Code = "SHR-001", MetaGenres = "stale" };
            var tags = Enumerable.Range(0, tagCount).Select(i => new Tag { Name = i == 0 ? "SAMPLE" : $"Junk {i}" }).ToList();
            var ownMovies = Enumerable.Range(0, tagCount).Select(i => new Movie { Code = $"OWN-{i:000}", MetaGenres = "stale" }).ToList();
            db.Tags.Add(kept);
            db.Tags.AddRange(tags);
            db.Movies.Add(shared);
            db.Movies.AddRange(ownMovies);
            db.IgnoredTags.Add(new IgnoredTag { Value = "sample", MatchMode = TagMatchMode.CaseInsensitive });
            await db.SaveChangesAsync();

            db.MovieTags.Add(new MovieTag { MovieId = shared.Id, TagId = kept.Id });
            for (var i = 0; i < tagCount; i++)
            {
                db.MovieTags.AddRange(
                    new MovieTag { MovieId = shared.Id, TagId = tags[i].Id },
                    new MovieTag { MovieId = ownMovies[i].Id, TagId = tags[i].Id });
            }
            await db.SaveChangesAsync();

            keptId = kept.Id;
            sharedMovieId = shared.Id;
            tagIds.AddRange(tags.Select(t => t.Id));
        }

        var service = CreateService(factory);
        counter.Reset();
        var count = await service.IgnoreManyAsync(tagIds);
        var selectCount = counter.Count;
        Assert.Equal(tagCount, count);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(keptId, (await verifyDb.Tags.SingleAsync()).Id);
        Assert.Equal(keptId, (await verifyDb.MovieTags.SingleAsync()).TagId);

        var movies = await verifyDb.Movies.ToListAsync();
        Assert.All(movies, m => Assert.Equal(m.Id == sharedMovieId ? "Kept" : null, m.MetaGenres));

        var ignored = await verifyDb.IgnoredTags.Select(i => i.Value).ToListAsync();
        Assert.Single(ignored, v => v.Equals("sample", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(tagCount, ignored.Count);

        return selectCount;
    }

    [Fact]
    public async Task ApproveManyAsync_ClearsNeedsReviewForAllSelectedTags()
    {
        using var factory = new TestDbContextFactory();
        int firstId, secondId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var first = new Tag { Name = "First", NeedsReview = true };
            var second = new Tag { Name = "Second", NeedsReview = true };
            db.Tags.AddRange(first, second);
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        var service = CreateService(factory);
        var count = await service.ApproveManyAsync([firstId, secondId]);
        Assert.Equal(2, count);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.Where(t => t.Id == firstId || t.Id == secondId).AnyAsync(t => t.NeedsReview));
    }

    [Fact]
    public async Task ApproveManyAsync_CountsMatchedTags_IncludingApprovedAndIgnoringDuplicatesAndMissing()
    {
        using var factory = new TestDbContextFactory();
        int pendingId, approvedId, untouchedId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pending = new Tag { Name = "Pending", NeedsReview = true };
            var approved = new Tag { Name = "Approved", NeedsReview = false };
            var untouched = new Tag { Name = "Untouched", NeedsReview = true };
            db.Tags.AddRange(pending, approved, untouched);
            await db.SaveChangesAsync();
            pendingId = pending.Id;
            approvedId = approved.Id;
            untouchedId = untouched.Id;
        }

        var service = CreateService(factory);
        Assert.Equal(0, await service.ApproveManyAsync([]));
        Assert.Equal(2, await service.ApproveManyAsync([pendingId, pendingId, approvedId, 999_999]));

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False((await verifyDb.Tags.SingleAsync(t => t.Id == pendingId)).NeedsReview);
        Assert.True((await verifyDb.Tags.SingleAsync(t => t.Id == untouchedId)).NeedsReview);
    }

    [Fact]
    public async Task IgnoreManyAsync_AddsAllToIgnoreListAndDeletesTags()
    {
        using var factory = new TestDbContextFactory();
        int firstId, secondId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var first = new Tag { Name = "Sample" };
            var second = new Tag { Name = "Trailer" };
            db.Tags.AddRange(first, second);
            var movie = new Movie { Code = "ABC-203", MetaGenres = "Sample, Trailer" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.MovieTags.AddRange(
                new MovieTag { MovieId = movie.Id, TagId = first.Id },
                new MovieTag { MovieId = movie.Id, TagId = second.Id });
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var count = await service.IgnoreManyAsync([firstId, secondId]);
        Assert.Equal(2, count);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == firstId || t.Id == secondId));
        Assert.True(await verifyDb.IgnoredTags.AnyAsync(i => i.Value == "Sample"));
        Assert.True(await verifyDb.IgnoredTags.AnyAsync(i => i.Value == "Trailer"));
        Assert.Empty(await verifyDb.MovieTags.Where(mt => mt.MovieId == movieId).ToListAsync());
    }

    [Fact]
    public async Task IgnoreAsync_AddsToIgnoreListAndDeletesTag()
    {
        using var factory = new TestDbContextFactory();
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Sample" };
            db.Tags.Add(tag);
            var movie = new Movie { Code = "ABC-103", MetaGenres = "Sample" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.IgnoreAsync(tagId);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == tagId));
        Assert.True(await verifyDb.IgnoredTags.AnyAsync(i => i.Value == "Sample"));
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.Null(reloadedMovie!.MetaGenres);
    }

    [Fact]
    public async Task DeleteAsync_UnlinksWithoutAddingToIgnoreList()
    {
        using var factory = new TestDbContextFactory();
        int tagId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "OneOff" };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        }

        var service = CreateService(factory);
        await service.DeleteAsync(tagId);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == tagId));
        Assert.Equal(0, await verifyDb.IgnoredTags.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_And_IgnoreAsync_QueueTheDriftCheckForTheTagsMovies()
    {
        using var factory = new TestDbContextFactory();
        int deleteTagId, ignoreTagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "DRF-001" };
            var deleteTag = new Tag { Name = "Gone" };
            var ignoreTag = new Tag { Name = "Skipped" };
            db.AddRange(movie, deleteTag, ignoreTag);
            await db.SaveChangesAsync();
            db.MovieTags.AddRange(
                new MovieTag { MovieId = movie.Id, TagId = deleteTag.Id, IsExplicit = true },
                new MovieTag { MovieId = movie.Id, TagId = ignoreTag.Id, IsExplicit = true });
            await db.SaveChangesAsync();
            (movieId, deleteTagId, ignoreTagId) = (movie.Id, deleteTag.Id, ignoreTag.Id);
        }

        var queue = Substitute.For<INfoDriftCheckQueue>();
        var service = new TagService(factory, Substitute.For<INfoSyncService>(), queue);
        await service.DeleteAsync(deleteTagId);
        await service.IgnoreAsync(ignoreTagId);

        queue.Received(2).Enqueue(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { movieId })));
    }

    [Fact]
    public async Task RenameMergeAndSetParent_QueueTheDriftCheckForTheirMovies()
    {
        using var factory = new TestDbContextFactory();
        int a, b, c, d, parent, m1, m2;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var (ta, tb, tc, td, tp) = (new Tag { Name = "A" }, new Tag { Name = "B" }, new Tag { Name = "C" }, new Tag { Name = "D" }, new Tag { Name = "Parent" });
            var (movie1, movie2) = (new Movie { Code = "DRF-101" }, new Movie { Code = "DRF-102" });
            db.AddRange(ta, tb, tc, td, tp, movie1, movie2);
            await db.SaveChangesAsync();
            db.MovieTags.AddRange(
                new MovieTag { MovieId = movie1.Id, TagId = ta.Id, IsExplicit = true },
                new MovieTag { MovieId = movie1.Id, TagId = tb.Id, IsExplicit = true },
                new MovieTag { MovieId = movie2.Id, TagId = tc.Id, IsExplicit = true },
                new MovieTag { MovieId = movie2.Id, TagId = td.Id, IsExplicit = true });
            await db.SaveChangesAsync();
            (a, b, c, d, parent, m1, m2) = (ta.Id, tb.Id, tc.Id, td.Id, tp.Id, movie1.Id, movie2.Id);
        }

        var queue = Substitute.For<INfoDriftCheckQueue>();
        var service = new TagService(factory, Substitute.For<INfoSyncService>(), queue);
        Assert.True((await service.RenameAsync(a, "A2")).Success);
        Assert.True((await service.SetParentAsync(b, parent)).Success);
        Assert.True((await service.MergeAsync(c, d)).Success);

        queue.Received(2).Enqueue(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { m1 })));
        queue.Received(1).Enqueue(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { m2 })));
    }

    [Fact]
    public async Task AddTagToMovieAsync_LinksTagAndSyncsMetaGenres()
    {
        using var factory = new TestDbContextFactory();
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Solo" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(tag, movie);
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.AddTagToMovieAsync(movieId, tagId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.True(await verifyDb.MovieTags.AnyAsync(mt => mt.MovieId == movieId && mt.TagId == tagId));
        Assert.Equal("Solo", (await verifyDb.Movies.FindAsync(movieId))!.MetaGenres);
    }

    [Fact]
    public async Task AddTagToMovieAsync_RejectsAlreadyLinkedTag()
    {
        using var factory = new TestDbContextFactory();
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Solo" };
            var movie = new Movie { Code = "ABC-100", MetaGenres = "Solo" };
            db.AddRange(tag, movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.AddTagToMovieAsync(movieId, tagId);

        Assert.False(result.Success);
        Assert.Contains("already on this movie", result.ErrorMessage);
    }

    [Fact]
    public async Task RemoveTagFromMovieAsync_UnlinksTagAndSyncsMetaGenres()
    {
        using var factory = new TestDbContextFactory();
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var solo = new Tag { Name = "Solo" };
            var vr = new Tag { Name = "VR" };
            var movie = new Movie { Code = "ABC-100", MetaGenres = "Solo, VR" };
            db.AddRange(solo, vr, movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = solo.Id });
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = vr.Id });
            await db.SaveChangesAsync();
            tagId = solo.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.RemoveTagFromMovieAsync(movieId, tagId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.MovieTags.AnyAsync(mt => mt.MovieId == movieId && mt.TagId == tagId));
        Assert.Equal("VR", (await verifyDb.Movies.FindAsync(movieId))!.MetaGenres);
    }

    [Fact]
    public async Task AddTagToMovieAsync_ImmediatelyFlagsNfoConflict_WithoutWaitingForRescan()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("TAG-001");
        await File.WriteAllTextAsync(Path.Combine(folder, "TAG-001.nfo"), "<movie><genre>Drama</genre></movie>");

        // Movie starts with no linked tags at all — .nfo already has <genre>Drama</genre>, but
        // Javbuddy doesn't know about it yet, so there's nothing to compare and no conflict.
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Comedy" };
            var movie = new Movie { Code = "TAG-001", Status = MovieStatus.Got };
            db.AddRange(tag, movie);
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var result = await service.AddTagToMovieAsync(movieId, tagId);
        Assert.True(result.Success);

        // Linking "Comedy" makes the canonical genre set ["Comedy"] vs. the .nfo's ["Drama"] —
        // a real mismatch, expected to be flagged immediately rather than on the next rescan.

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.NotNull(reloadedMovie);
        Assert.NotEqual(NfoDriftKind.None, reloadedMovie!.NfoDriftKind);
        Assert.Contains("Genres", reloadedMovie.NfoConflictDetails);
    }

    [Fact]
    public async Task RemoveTagFromMovieAsync_ImmediatelyFlagsNfoConflict_WithoutWaitingForRescan()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("TAG-002");
        await File.WriteAllTextAsync(Path.Combine(folder, "TAG-002.nfo"), "<movie><genre>Comedy</genre><genre>Drama</genre></movie>");

        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var comedy = new Tag { Name = "Comedy" };
            var drama = new Tag { Name = "Drama" };
            var movie = new Movie { Code = "TAG-002", Status = MovieStatus.Got, MetaGenres = "Comedy, Drama" };
            db.AddRange(comedy, drama, movie);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = comedy.Id });
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = drama.Id });
            await db.SaveChangesAsync();
            tagId = comedy.Id;
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var result = await service.RemoveTagFromMovieAsync(movieId, tagId);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.NotNull(reloadedMovie);
        Assert.NotEqual(NfoDriftKind.None, reloadedMovie!.NfoDriftKind);
        Assert.Contains("Genres", reloadedMovie.NfoConflictDetails);
    }

    [Fact]
    public async Task RemoveTagFromMovieAsync_RejectsWhenNotLinked()
    {
        using var factory = new TestDbContextFactory();
        int tagId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Solo" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(tag, movie);
            await db.SaveChangesAsync();
            tagId = tag.Id;
            movieId = movie.Id;
        }

        var service = CreateService(factory);
        var result = await service.RemoveTagFromMovieAsync(movieId, tagId);

        Assert.False(result.Success);
        Assert.Equal("Tag is not on this movie.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetMergeCandidatesAsync_ExcludesSelf_AndFlagsSubstringMatchAsSuggested()
    {
        using var factory = new TestDbContextFactory();
        int soloworkId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var solowork = new Tag { Name = "Solowork" };
            db.Tags.AddRange(solowork, new Tag { Name = "Solo" }, new Tag { Name = "VR" });
            await db.SaveChangesAsync();
            soloworkId = solowork.Id;
        }

        var service = CreateService(factory);
        var candidates = await service.GetMergeCandidatesAsync(soloworkId);

        Assert.DoesNotContain(candidates, c => c.Id == soloworkId);
        Assert.Contains(candidates, c => c.Name == "Solo" && c.IsSuggested);
        Assert.Contains(candidates, c => c.Name == "VR" && !c.IsSuggested);
    }

    [Fact]
    public async Task CreateTagAsync_WithParent_Succeeds()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var parentResult = await service.CreateTagAsync("Cosplay");
        Assert.True(parentResult.Success);

        var childResult = await service.CreateTagAsync("ram", parentResult.Tag!.Id);
        Assert.True(childResult.Success);
        Assert.Equal(parentResult.Tag.Id, childResult.Tag!.ParentTagId);
    }

    [Fact]
    public async Task CreateTagAsync_WithParentThatHasParent_RejectsMaxLevels()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var root = await service.CreateTagAsync("Root");
        var child = await service.CreateTagAsync("Child", root.Tag!.Id);

        var grandchild = await service.CreateTagAsync("Grandchild", child.Tag!.Id);
        Assert.False(grandchild.Success);
        Assert.Contains("maximum 2 levels", grandchild.ErrorMessage);
    }

    [Fact]
    public async Task CreateTagAsync_WithUnapprovedParent_Fails()
    {
        using var factory = new TestDbContextFactory();
        int unapprovedParentId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var unapproved = new Tag { Name = "DiscoveredCategory", NeedsReview = true };
            db.Tags.Add(unapproved);
            await db.SaveChangesAsync();
            unapprovedParentId = unapproved.Id;
        }

        var service = CreateService(factory);
        var result = await service.CreateTagAsync("ram", unapprovedParentId);

        Assert.False(result.Success);
        Assert.Contains("unapproved", result.ErrorMessage);
    }

    [Fact]
    public async Task SetParentAsync_WithUnapprovedParent_Fails()
    {
        using var factory = new TestDbContextFactory();
        int unapprovedParentId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var unapproved = new Tag { Name = "DiscoveredCategory", NeedsReview = true };
            db.Tags.Add(unapproved);
            await db.SaveChangesAsync();
            unapprovedParentId = unapproved.Id;
        }

        var service = CreateService(factory);
        var child = await service.CreateTagAsync("ram");

        var result = await service.SetParentAsync(child.Tag!.Id, unapprovedParentId);

        Assert.False(result.Success);
        Assert.Contains("unapproved", result.ErrorMessage);
    }

    [Fact]
    public async Task SetParentAsync_AssignsAndClearsParent()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var parent = await service.CreateTagAsync("Cosplay");
        var child = await service.CreateTagAsync("ram");

        var setResult = await service.SetParentAsync(child.Tag!.Id, parent.Tag!.Id);
        Assert.True(setResult.Success);

        var tags = await service.GetTagsAsync();
        var updatedChild = tags.First(t => t.Id == child.Tag.Id);
        Assert.Equal(parent.Tag.Id, updatedChild.ParentTagId);
        Assert.Equal("Cosplay", updatedChild.ParentTagName);

        var clearResult = await service.SetParentAsync(child.Tag.Id, null);
        Assert.True(clearResult.Success);

        tags = await service.GetTagsAsync();
        updatedChild = tags.First(t => t.Id == child.Tag.Id);
        Assert.Null(updatedChild.ParentTagId);
        Assert.Null(updatedChild.ParentTagName);
    }

    [Fact]
    public async Task SetParentAsync_SelfParent_Fails()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var tag = await service.CreateTagAsync("Solo");
        var result = await service.SetParentAsync(tag.Tag!.Id, tag.Tag.Id);

        Assert.False(result.Success);
        Assert.Contains("cannot be its own parent", result.ErrorMessage);
    }

    [Fact]
    public async Task SetParentAsync_WhenTagHasSubtags_Fails()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var parent = await service.CreateTagAsync("Cosplay");
        var subtag = await service.CreateTagAsync("ram", parent.Tag!.Id);
        var other = await service.CreateTagAsync("Costume");

        // parent has subtags, so it cannot be assigned a parent
        var result = await service.SetParentAsync(parent.Tag.Id, other.Tag!.Id);
        Assert.False(result.Success);
        Assert.Contains("cannot become a subtag", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAsync_TagWithSubtags_Fails()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var parent = await service.CreateTagAsync("Cosplay");
        await service.CreateTagAsync("ram", parent.Tag!.Id);

        var result = await service.DeleteAsync(parent.Tag.Id);

        Assert.False(result.Success);
        Assert.Contains("subtags", result.ErrorMessage);
        Assert.NotNull(await service.GetByIdAsync(parent.Tag.Id));
    }

    [Fact]
    public async Task MergeAsync_ReparentsSubtagsToTarget()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var sourceParent = await service.CreateTagAsync("CosplayOld");
        var targetParent = await service.CreateTagAsync("CosplayNew");
        var subtag = await service.CreateTagAsync("ram", sourceParent.Tag!.Id);

        var mergeResult = await service.MergeAsync(sourceParent.Tag.Id, targetParent.Tag!.Id);
        Assert.True(mergeResult.Success);

        var tags = await service.GetTagsAsync();
        var updatedSubtag = tags.First(t => t.Id == subtag.Tag!.Id);
        Assert.Equal(targetParent.Tag.Id, updatedSubtag.ParentTagId);
        Assert.Equal("CosplayNew", updatedSubtag.ParentTagName);
    }

    [Fact]
    public async Task MergeAsync_IntoSubTag_Succeeds_WhenSourceIsLeaf()
    {
        // Merging a leaf tag into a sub-tag used to error or silently fail.
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var category = await service.CreateTagAsync("Category");
        var subTag = await service.CreateTagAsync("SubTag", category.Tag!.Id);
        var leaf = await service.CreateTagAsync("Leaf");

        var mergeResult = await service.MergeAsync(leaf.Tag!.Id, subTag.Tag!.Id);
        Assert.True(mergeResult.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == leaf.Tag.Id));
        Assert.True(await verifyDb.Tags.AnyAsync(t => t.Id == subTag.Tag!.Id));
    }

    [Fact]
    public async Task MergeAsync_IntoSubTag_WhenSourceIsCategory_PromotesSubtagsToRoot()
    {
        // Merging a category (source with subtags) into a sub-tag used to error.
        // Fixed: source's subtags are promoted to root level since they can't nest under the
        // target sub-tag without exceeding the 2-level limit.
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var targetCategory = await service.CreateTagAsync("TargetCategory");
        var targetSubTag = await service.CreateTagAsync("TargetSubTag", targetCategory.Tag!.Id);

        var sourceCategory = await service.CreateTagAsync("SourceCategory");
        var sourceChild = await service.CreateTagAsync("SourceChild", sourceCategory.Tag!.Id);

        var mergeResult = await service.MergeAsync(sourceCategory.Tag!.Id, targetSubTag.Tag!.Id);
        Assert.True(mergeResult.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == sourceCategory.Tag.Id));

        // SourceChild could not nest under the sub-tag — must have been promoted to root.
        var updatedChild = await verifyDb.Tags.FindAsync(sourceChild.Tag!.Id);
        Assert.NotNull(updatedChild);
        Assert.Null(updatedChild!.ParentTagId);
    }

    [Fact]
    public async Task MergeManyAsync_IntoSubTag_Succeeds_WhenSourcesAreLeaves()
    {
        // MergeManyAsync had the same guard as MergeAsync.
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var category = await service.CreateTagAsync("Category");
        var subTag = await service.CreateTagAsync("SubTag", category.Tag!.Id);
        var leaf1 = await service.CreateTagAsync("Leaf1");
        var leaf2 = await service.CreateTagAsync("Leaf2");

        var mergeResult = await service.MergeManyAsync([leaf1.Tag!.Id, leaf2.Tag!.Id], subTag.Tag!.Id);
        Assert.True(mergeResult.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == leaf1.Tag.Id));
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Id == leaf2.Tag.Id));
        Assert.True(await verifyDb.Tags.AnyAsync(t => t.Id == subTag.Tag!.Id));
    }

    [Fact]
    public async Task GetMergeCandidatesAsync_IncludesSubTags_WithParentInfo()
    {
        // Candidates must include sub-tags and expose their ParentTagName
        // so the merge modal can display parent context.
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var category = await service.CreateTagAsync("Category");
        var subTag = await service.CreateTagAsync("SubTag", category.Tag!.Id);
        var source = await service.CreateTagAsync("Source");

        var candidates = await service.GetMergeCandidatesAsync(source.Tag!.Id);

        var subTagCandidate = candidates.FirstOrDefault(c => c.Id == subTag.Tag!.Id);
        Assert.NotNull(subTagCandidate);
        Assert.Equal(category.Tag!.Id, subTagCandidate!.ParentTagId);
        Assert.Equal("Category", subTagCandidate.ParentTagName);
    }

    [Fact]
    public async Task GetTagTreeAsync_ReturnsHierarchicalNodes()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var parent = await service.CreateTagAsync("Cosplay");
        var child1 = await service.CreateTagAsync("ram", parent.Tag!.Id);
        var child2 = await service.CreateTagAsync("rem", parent.Tag!.Id);
        var rootOnly = await service.CreateTagAsync("Drama");

        var tree = await service.GetTagTreeAsync();
        Assert.Equal(2, tree.Count);

        var cosplayNode = tree.First(n => n.Name == "Cosplay");
        Assert.Equal(2, cosplayNode.Subtags.Count);
        Assert.Contains(cosplayNode.Subtags, s => s.Name == "ram");
        Assert.Contains(cosplayNode.Subtags, s => s.Name == "rem");

        var dramaNode = tree.First(n => n.Name == "Drama");
        Assert.Empty(dramaNode.Subtags);
    }

    [Fact]
    public async Task GetTagTreeAsync_CountsRootMoviesDistinctAcrossSubtags_AndFiltersBySearch()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var cosplay = new Tag { Name = "Cosplay" };
            var ram = new Tag { Name = "ram", ParentTag = cosplay };
            var rem = new Tag { Name = "Rem", ParentTag = cosplay };
            var drama = new Tag { Name = "drama" };
            var shared = new Movie { Code = "AAA-1", Status = MovieStatus.Got };
            var ramOnly = new Movie { Code = "BBB-2", Status = MovieStatus.Got };
            var other = new Movie { Code = "CCC-3", Status = MovieStatus.Got };
            db.MovieTags.AddRange(
                new MovieTag { Movie = shared, Tag = cosplay },
                new MovieTag { Movie = shared, Tag = ram },
                new MovieTag { Movie = shared, Tag = rem },
                new MovieTag { Movie = ramOnly, Tag = ram },
                new MovieTag { Movie = other, Tag = drama });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);

        var tree = await service.GetTagTreeAsync();
        Assert.Equal(["Cosplay", "drama"], tree.Select(n => n.Name));
        var cosplayNode = tree[0];
        Assert.Equal(1, cosplayNode.DirectMovieCount);
        Assert.Equal(2, cosplayNode.TotalMovieCount);
        Assert.Equal(["ram", "Rem"], cosplayNode.Subtags.Select(s => s.Name));
        Assert.Equal([2, 1], cosplayNode.Subtags.Select(s => s.MovieCount));
        Assert.All(cosplayNode.Subtags, s => Assert.Equal("Cosplay", s.ParentTagName));
        Assert.Equal(1, tree[1].TotalMovieCount);

        var searched = Assert.Single(await service.GetTagTreeAsync("REM"));
        Assert.Equal(2, searched.TotalMovieCount);
        Assert.Equal("Rem", Assert.Single(searched.Subtags).Name);
    }
}
