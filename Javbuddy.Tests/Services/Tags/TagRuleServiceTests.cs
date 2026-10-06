using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Tags;

public class TagRuleServiceTests
{
    [Fact]
    public async Task AddReplacementRuleAsync_RejectsCaseInsensitiveDuplicate()
    {
        using var factory = new TestDbContextFactory();
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Solo" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }

        var service = new TagRuleService(factory);
        var first = await service.AddReplacementRuleAsync("Solowork", TagMatchMode.CaseInsensitive, targetId);
        Assert.True(first.Success);

        var duplicate = await service.AddReplacementRuleAsync("solowork", TagMatchMode.CaseInsensitive, targetId);
        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task ApplyReplacementRulesToLibraryAsync_MergesExistingTagMatchingRule()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-104", MetaGenres = "Solowork" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            await TagNormalization.ApplyToMovieAsync(db, movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new TagRuleService(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Solo" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = target.Id });
            await db.SaveChangesAsync();
        }

        await service.ApplyReplacementRulesToLibraryAsync();

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.Equal("Solo", reloadedMovie!.MetaGenres);
        Assert.False(await verifyDb.Tags.AnyAsync(t => t.Name == "Solowork"));
    }

    [Fact]
    public async Task AutoIgnoreNonLatin_RoundTrips()
    {
        using var factory = new TestDbContextFactory();
        var service = new TagRuleService(factory);

        Assert.False(await service.GetAutoIgnoreNonLatinAsync());

        await service.SetAutoIgnoreNonLatinAsync(true);
        Assert.True(await service.GetAutoIgnoreNonLatinAsync());

        await service.SetAutoIgnoreNonLatinAsync(false);
        Assert.False(await service.GetAutoIgnoreNonLatinAsync());
    }

    [Fact]
    public async Task IgnoredTags_AddDuplicateRejected_AndRemoveDeletesIt()
    {
        using var factory = new TestDbContextFactory();
        var service = new TagRuleService(factory);

        Assert.True((await service.AddIgnoredTagAsync("Sample", TagMatchMode.CaseInsensitive)).Success);
        Assert.False((await service.AddIgnoredTagAsync("sample", TagMatchMode.CaseInsensitive)).Success);

        var ignored = Assert.Single(await service.GetIgnoredTagsAsync());
        await service.RemoveIgnoredTagAsync(ignored.Id);
        Assert.Empty(await service.GetIgnoredTagsAsync());
    }

    [Fact]
    public async Task GetReplacementRulesAsync_ReturnsRulesWithTargetNamesOrderedBySource()
    {
        using var factory = new TestDbContextFactory();
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Solo" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }
        var service = new TagRuleService(factory);
        await service.AddReplacementRuleAsync("Zeta", TagMatchMode.CaseInsensitive, targetId);
        await service.AddReplacementRuleAsync("Alpha", TagMatchMode.CaseInsensitive, targetId);

        var rules = await service.GetReplacementRulesAsync();

        Assert.Equal(["Alpha", "Zeta"], rules.Select(r => r.SourceValue));
        Assert.All(rules, r => Assert.Equal("Solo", r.TargetTagName));
    }
}
