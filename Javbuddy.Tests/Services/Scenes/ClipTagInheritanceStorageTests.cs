using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Inherited tags as stored by ClipActorSync, kept current through the stale flag, and what reads them:
/// the wall's filters and cards, tag merges and the library rules pass.</summary>
public sealed class ClipTagInheritanceStorageTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ClipTagInheritanceService service;
    private readonly ISceneWallQueryService wall;
    private int movieId, sceneId, highlightId, apexId, outdoorId, nightId;

    public ClipTagInheritanceStorageTests()
    {
        service = new ClipTagInheritanceService(factory);
        wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory)));
    }

    public void Dispose() => factory.Dispose();

    /// <summary>Scene 0–600 with "Outdoor" (not passed down yet), highlight 10–100, apex 50; tag "Night" on the
    /// movie, explicit, not passed down.</summary>
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "PASS-001", MediaDurationSeconds = 3600, LocalFileSizeBytes = 1 };
        var outdoor = new Tag { Name = "Outdoor" };
        var night = new Tag { Name = "Night" };
        db.AddRange(movie, outdoor, night);
        await db.SaveChangesAsync();
        (movieId, outdoorId, nightId) = (movie.Id, outdoor.Id, night.Id);
        var scene = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 600 };
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
        var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
        db.AddRange(scene, highlight, apex);
        await db.SaveChangesAsync();
        (sceneId, highlightId, apexId) = (scene.Id, highlight.Id, apex.Id);
        db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = outdoorId });
        db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = nightId });
        await db.SaveChangesAsync();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await ClipActorSync.RefreshStaleAsync(db);
    }

    private async Task<(List<(int, int)> Scenes, List<(int, int)> Highlights, List<(int, int)> Apexes)> StoredAsync()
    {
        await RefreshAsync();
        await using var db = await factory.CreateDbContextAsync();
        return (
            (await db.SceneInheritedTags.Select(r => new { r.SceneId, r.TagId }).ToListAsync()).Select(r => (r.SceneId, r.TagId)).Order().ToList(),
            (await db.HighlightInheritedTags.Select(r => new { r.HighlightId, r.TagId }).ToListAsync()).Select(r => (r.HighlightId, r.TagId)).Order().ToList(),
            (await db.ApexInheritedTags.Select(r => new { r.ApexId, r.TagId }).ToListAsync()).Select(r => (r.ApexId, r.TagId)).Order().ToList());
    }

    private static void AssertNone((List<(int, int)> Scenes, List<(int, int)> Highlights, List<(int, int)> Apexes) stored)
    {
        Assert.Empty(stored.Scenes);
        Assert.Empty(stored.Highlights);
        Assert.Empty(stored.Apexes);
    }

    private async Task<bool> StaleAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync();
    }

    [Fact]
    public async Task NothingIsInherited_UntilATagIsPassedDown()
    {
        await SeedAsync();

        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task PassingASceneTagDown_MarksTheMovieStale_AndStoresItOnItsClips()
    {
        await SeedAsync();

        Assert.True((await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true)).Success);

        Assert.True(await StaleAsync());
        var (scenes, highlights, apexes) = await StoredAsync();
        Assert.Empty(scenes);
        Assert.Equal([(highlightId, outdoorId)], highlights);
        Assert.Equal([(apexId, outdoorId)], apexes);
    }

    [Fact]
    public async Task TurningTheToggleOff_RemovesTheStoredRows()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await StoredAsync();

        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, false);

        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task RemovingAPassedDownSceneTag_RemovesTheStoredRows()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await StoredAsync();

        await new MovieSceneService(factory).RemoveSceneTagAsync(sceneId, outdoorId);

        Assert.True(await StaleAsync());
        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task AMovieTag_PassesDownToEveryClip_OnlyWhileExplicit()
    {
        await SeedAsync();
        var tags = new TagService(factory, Substitute.For<INfoSyncService>());

        Assert.True((await tags.SetMovieTagPropagatesDownAsync(movieId, nightId, true)).Success);
        var (scenes, highlights, apexes) = await StoredAsync();
        Assert.Equal([(sceneId, nightId)], scenes);
        Assert.Equal([(highlightId, nightId)], highlights);
        Assert.Equal([(apexId, nightId)], apexes);

        // Removing it from the movie (a clip still carries it, so the link stays clip-only) stops it passing down.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ApexTags.Add(new ApexTag { ApexId = apexId, TagId = nightId });
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            await ClipTagSync.RefreshAsync(db, movieId, CancellationToken.None);
        }
        Assert.True((await tags.RemoveTagFromMovieAsync(movieId, nightId)).Success);
        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task AClipOnlyMovieTag_CannotBePassedDown()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieTags.Single(mt => mt.MovieId == movieId && mt.TagId == nightId).IsExplicit = false;
            await db.SaveChangesAsync();
        }

        var result = await new TagService(factory, Substitute.For<INfoSyncService>()).SetMovieTagPropagatesDownAsync(movieId, nightId, true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AnExclusion_KeepsTheTagOffThatClipOnly_AndRestoringBringsItBack()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);

        Assert.True((await service.SetExcludedAsync(ClipKind.Highlight, highlightId, outdoorId, true)).Success);
        var (_, highlights, apexes) = await StoredAsync();
        Assert.Empty(highlights);
        // The apex inside the highlight still gets it from the scene.
        Assert.Equal([(apexId, outdoorId)], apexes);

        Assert.True((await service.SetExcludedAsync(ClipKind.Highlight, highlightId, outdoorId, false)).Success);
        Assert.Equal([(highlightId, outdoorId)], (await StoredAsync()).Highlights);
    }

    [Fact]
    public async Task AHighlightTagSavedWithTheToggle_PassesDownToItsApexes()
    {
        await SeedAsync();
        var highlights = new MovieHighlightService(factory);

        Assert.True((await highlights.UpdateHighlightAsync(highlightId, 10, 100, null, tagIds: [nightId], propagatingTagIds: [nightId])).Success);
        Assert.Equal([(apexId, nightId)], (await StoredAsync()).Apexes);

        // Saving without saying leaves it on; saving with none turns it off.
        Assert.True((await highlights.UpdateHighlightAsync(highlightId, 10, 100, "Renamed", tagIds: [nightId])).Success);
        Assert.Equal([(apexId, nightId)], (await StoredAsync()).Apexes);
        Assert.True((await highlights.UpdateHighlightAsync(highlightId, 10, 100, null, tagIds: [nightId], propagatingTagIds: [])).Success);
        Assert.Empty((await StoredAsync()).Apexes);
    }

    [Fact]
    public async Task MovingAnApexOutOfTheScene_DropsWhatItInherited()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await StoredAsync();

        await new MovieApexService(factory).UpdateApexAsync(apexId, 900, []);

        Assert.Empty((await StoredAsync()).Apexes);
    }

    [Fact]
    public async Task Wall_ApexTagFilterAndOptions_IncludeInheritedTags_AndCardsShowThem()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);

        var page = await wall.GetApexPageAsync(new SceneWallFilter(ApexTagIds: [outdoorId]), SceneWallSort.ReleaseDate, 0, 0, 48);
        var card = Assert.Single(page.Cards);
        Assert.Equal(apexId, card.ApexId);
        Assert.Equal([outdoorId], card.InheritedTags.Select(t => t.TagId));
        Assert.Empty(card.Tags);

        Assert.Contains((await wall.GetApexOptionsAsync()).ApexTags, o => o.Id == outdoorId);

        var highlightPage = await wall.GetHighlightPageAsync(new SceneWallFilter(TagIds: [outdoorId]), SceneWallSort.ReleaseDate, 0, 0, 48);
        Assert.Equal([highlightId], highlightPage.Cards.Select(c => c.HighlightId));
        Assert.Equal([outdoorId], highlightPage.Cards.Single().InheritedTags.Select(t => t.TagId));
    }

    [Fact]
    public async Task Merge_KeepsTheToggle_AndMovesExclusions()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await service.SetExcludedAsync(ClipKind.Apex, apexId, outdoorId, true);
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Outside" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
            // The scene already has the target, without the toggle: the merge ORs it in.
            db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = targetId });
            await db.SaveChangesAsync();
        }

        Assert.True((await new TagService(factory, Substitute.For<INfoSyncService>()).MergeAsync(outdoorId, targetId)).Success);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True((await db.SceneTags.SingleAsync(st => st.SceneId == sceneId)).PropagatesDown);
            Assert.Equal(targetId, (await db.ApexTagExclusions.SingleAsync()).TagId);
        }
        var (_, highlights, apexes) = await StoredAsync();
        Assert.Equal([(highlightId, targetId)], highlights);
        Assert.Empty(apexes);
    }

    [Fact]
    public async Task LibraryRules_RepointingAMovieTag_KeepsItsToggle()
    {
        await SeedAsync();
        await new TagService(factory, Substitute.For<INfoSyncService>()).SetMovieTagPropagatesDownAsync(movieId, nightId, true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Evening" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Night", TargetTagId = target.Id });
            db.Movies.Single(m => m.Id == movieId).MetaGenres = "Night";
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        // The scene's "Outdoor" is a clip-only row; the movie's own tag is now "Evening", still passed down.
        var link = await verify.MovieTags.Include(mt => mt.Tag).SingleAsync(mt => mt.MovieId == movieId && mt.IsExplicit);
        Assert.True(link.PropagatesDown);
    }

    [Fact]
    public async Task LibraryRules_RepointingASceneTag_KeepsItsToggle()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Tag { Name = "Outside" };
            db.Tags.Add(target);
            await db.SaveChangesAsync();
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Outdoor", TargetTagId = target.Id });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var link = await verify.SceneTags.Include(st => st.Tag).SingleAsync(st => st.SceneId == sceneId);
        Assert.Equal("Outside", link.Tag.Name);
        Assert.True(link.PropagatesDown);
    }

    [Fact]
    public async Task MergeMany_TwoSourcesOnOneClip_KeepsTheToggleOfEither()
    {
        await SeedAsync();
        int otherId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // "Outdoor" (lower id, toggle off) is read first; the toggle is on the later "Outdoors" link.
            var other = new Tag { Name = "Outdoors" };
            var target = new Tag { Name = "Outside" };
            db.Tags.AddRange(other, target);
            await db.SaveChangesAsync();
            (otherId, targetId) = (other.Id, target.Id);
            db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = otherId, PropagatesDown = true });
            db.HighlightTags.Add(new HighlightTag { HighlightId = highlightId, TagId = outdoorId });
            db.HighlightTags.Add(new HighlightTag { HighlightId = highlightId, TagId = otherId, PropagatesDown = true });
            await db.SaveChangesAsync();
        }

        Assert.True((await new TagService(factory, Substitute.For<INfoSyncService>()).MergeManyAsync([otherId, outdoorId], targetId)).Success);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.True((await verify.SceneTags.SingleAsync(st => st.SceneId == sceneId)).PropagatesDown);
        Assert.True((await verify.HighlightTags.SingleAsync(ht => ht.HighlightId == highlightId)).PropagatesDown);
    }

    [Fact]
    public async Task LibraryRules_RepointingAPassedDownTag_MovesTheClipsOptOuts()
    {
        await SeedAsync();
        await new TagService(factory, Substitute.For<INfoSyncService>()).SetMovieTagPropagatesDownAsync(movieId, nightId, true);
        await service.SetExcludedAsync(ClipKind.Apex, apexId, nightId, true);
        int eveningId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var evening = new Tag { Name = "Evening" };
            db.Tags.Add(evening);
            await db.SaveChangesAsync();
            eveningId = evening.Id;
            db.TagReplacementRules.Add(new TagReplacementRule { SourceValue = "Night", TargetTagId = eveningId });
            db.Movies.Single(m => m.Id == movieId).MetaGenres = "Night";
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await TagNormalization.ApplyToLibraryAsync(db);
            await db.SaveChangesAsync();
        }

        await using (var verify = await factory.CreateDbContextAsync())
        {
            Assert.Equal(eveningId, (await verify.ApexTagExclusions.SingleAsync(e => e.ApexId == apexId)).TagId);
        }
        var (scenes, _, apexes) = await StoredAsync();
        Assert.Equal([(sceneId, eveningId)], scenes);
        Assert.Empty(apexes);
    }

    [Fact]
    public async Task RemovingATagFromTheMovieAndItsClips_MarksStale_AndDropsWhatItPassedDown()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await StoredAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await ClipTagSync.RefreshAsync(db, movieId, CancellationToken.None);
            db.MovieTags.Single(mt => mt.MovieId == movieId && mt.TagId == outdoorId).IsExplicit = true;
            await db.SaveChangesAsync();
        }

        Assert.True((await new TagService(factory, Substitute.For<INfoSyncService>()).RemoveTagFromMovieAndClipsAsync(movieId, outdoorId)).Success);

        Assert.True(await StaleAsync());
        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task DeletingTheScene_DropsWhatItPassedDown()
    {
        await SeedAsync();
        await service.SetSceneTagPropagatesDownAsync(sceneId, outdoorId, true);
        await StoredAsync();

        Assert.True((await new MovieSceneService(factory).DeleteSceneAsync(sceneId)).Success);

        AssertNone(await StoredAsync());
    }

    [Fact]
    public async Task DeletingTheHighlight_DropsWhatItPassedDown()
    {
        await SeedAsync();
        var highlights = new MovieHighlightService(factory);
        await highlights.UpdateHighlightAsync(highlightId, 10, 100, null, tagIds: [nightId], propagatingTagIds: [nightId]);
        Assert.Equal([(apexId, nightId)], (await StoredAsync()).Apexes);

        Assert.True((await highlights.DeleteHighlightAsync(highlightId)).Success);

        Assert.Empty((await StoredAsync()).Apexes);
    }
}
