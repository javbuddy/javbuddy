using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Per-scene actors and tags: the service API, and how scene links follow
/// cast changes, tag/actor merges, orphan-tag pruning, the Movies genre filter and tag counts.</summary>
public sealed class SceneActorsAndTagsTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly MovieSceneService sceneService;
    private readonly INfoSyncService nfoSync = Substitute.For<INfoSyncService>();

    public SceneActorsAndTagsTests() => sceneService = new MovieSceneService(factory, clipTags: new ClipTagSyncService(factory, nfoSync));

    public void Dispose() => factory.Dispose();

    private sealed record Seed(int MovieId, int SceneId, int YuaId, int AikaId, int OutsiderId);

    /// <summary>A movie whose cast is Yua Mikami and Aika (via MetaActresses + MovieActors), one
    /// scene, and a third actor not in the cast.</summary>
    private async Task<Seed> SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
        var aika = new Actor { FirstName = "Aika" };
        var outsider = new Actor { FirstName = "Other", LastName = "Person" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Yua Mikami, Aika", MediaDurationSeconds = 3600 };
        db.AddRange(yua, aika, outsider, movie);
        await db.SaveChangesAsync();
        db.MovieActors.AddRange(new MovieActor { MovieId = movie.Id, ActorId = yua.Id }, new MovieActor { MovieId = movie.Id, ActorId = aika.Id });
        var scene = new Scene { MovieId = movie.Id, StartSeconds = 0 };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return new Seed(movie.Id, scene.Id, yua.Id, aika.Id, outsider.Id);
    }

    private async Task<int> AddTagAsync(string name, int? parentId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var tag = new Tag { Name = name, ParentTagId = parentId };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag.Id;
    }

    /// <summary>A tag only on the scene, not on the movie: since adding through the
    /// service also tags the movie, so this state (pre-#398 data, or a movie tag removed later) is
    /// seeded directly.</summary>
    private async Task AddSceneOnlyTagAsync(int sceneId, int tagId)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = tagId });
        await db.SaveChangesAsync();
    }

    private async Task<List<int>> MovieTagIdsAsync(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.MovieTags.Where(mt => mt.MovieId == movieId).Select(mt => mt.TagId).OrderBy(id => id).ToListAsync();
    }

    [Fact]
    public async Task AddScene_InheritsTheCast_UntilActorsArePicked()
    {
        var seed = await SeedAsync();
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId);

        // Lands inside the seeded scene (which only has Yua), but has none of its own: it inherits the
        // cast.
        var added = await sceneService.AddSceneAsync(seed.MovieId, 600, null, null);

        Assert.True(added.Success);
        var scene = Assert.Single(await sceneService.GetScenesAsync(seed.MovieId), s => s.Id == added.SceneId);
        Assert.Empty(scene.Actors);
        Assert.Equal(ActorSource.Cast, scene.EffectiveActors.Source);
        Assert.Equal(["Aika", "Mikami Yua"], scene.EffectiveActors.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task SetSceneActors_ReplacesTheOwnSet_EmptyInheritsAgain_AndOnlyFromCast()
    {
        var seed = await SeedAsync();
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId);

        Assert.True((await sceneService.SetSceneActorsAsync(seed.SceneId, [seed.AikaId])).Success);
        var scene = Assert.Single(await sceneService.GetScenesAsync(seed.MovieId), s => s.Id == seed.SceneId);
        Assert.Equal(["Aika"], scene.Actors.Select(a => a.Name));
        Assert.Equal(ActorSource.Explicit, scene.EffectiveActors.Source);

        var outsider = await sceneService.SetSceneActorsAsync(seed.SceneId, [seed.AikaId, seed.OutsiderId]);
        Assert.False(outsider.Success);
        scene = Assert.Single(await sceneService.GetScenesAsync(seed.MovieId), s => s.Id == seed.SceneId);
        Assert.Equal(["Aika"], scene.Actors.Select(a => a.Name));

        Assert.True((await sceneService.SetSceneActorsAsync(seed.SceneId, [])).Success);
        scene = Assert.Single(await sceneService.GetScenesAsync(seed.MovieId), s => s.Id == seed.SceneId);
        Assert.Empty(scene.Actors);
        Assert.Equal(ActorSource.Cast, scene.EffectiveActors.Source);
        Assert.False((await sceneService.SetSceneActorsAsync(999, [])).Success);
    }

    [Fact]
    public async Task SceneActors_OnlyFromCast_IdempotentAndListedByName()
    {
        var seed = await SeedAsync();

        Assert.True((await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId)).Success);
        Assert.True((await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId)).Success);
        Assert.True((await sceneService.AddSceneActorAsync(seed.SceneId, seed.AikaId)).Success);
        var outsider = await sceneService.AddSceneActorAsync(seed.SceneId, seed.OutsiderId);

        Assert.Equal("Only actors in the movie's cast can be added to a scene.", outsider.ErrorMessage);
        var scene = Assert.Single(await sceneService.GetScenesAsync(seed.MovieId));
        // Actor.DisplayName is family name first.
        Assert.Equal(["Aika", "Mikami Yua"], scene.Actors.Select(a => a.Name));
        Assert.Equal(["Aika", "Mikami Yua"], (await sceneService.GetCastOptionsAsync(seed.MovieId)).Select(a => a.Name));

        await sceneService.RemoveSceneActorAsync(seed.SceneId, seed.AikaId);
        Assert.Equal([seed.YuaId], (await sceneService.GetScenesAsync(seed.MovieId))[0].Actors.Select(a => a.ActorId));
    }

    [Fact]
    public async Task SceneTags_AddRemove_ShowParentForSubtags()
    {
        var seed = await SeedAsync();
        var play = await AddTagAsync("Play");
        var rough = await AddTagAsync("Rough", play);

        await sceneService.AddSceneTagAsync(seed.SceneId, rough);
        await sceneService.AddSceneTagAsync(seed.SceneId, rough);
        Assert.Equal("Tag not found.", (await sceneService.AddSceneTagAsync(seed.SceneId, 9999)).ErrorMessage);

        var tag = Assert.Single((await sceneService.GetScenesAsync(seed.MovieId))[0].Tags);
        Assert.Equal(new SceneTagItem(rough, "Rough", "Play"), tag);

        await sceneService.RemoveSceneTagAsync(seed.SceneId, rough);
        Assert.Empty((await sceneService.GetScenesAsync(seed.MovieId))[0].Tags);
    }

    [Fact]
    public async Task RemovingAnActorFromTheCast_ViaMetaActressesSync_RemovesThemFromScenes()
    {
        var seed = await SeedAsync();
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId);
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.AikaId);

        // The real cast-removal path (Edit Cast, metadata refresh, rescans): rewrite MetaActresses
        // and let MovieActorAssociation drop the stale MovieActor link.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = await db.Movies.SingleAsync(m => m.Id == seed.MovieId);
            movie.MetaActresses = "Yua Mikami";
            await MovieActorAssociation.SynchronizeAsync(db, movie);
            await db.SaveChangesAsync();
        }

        Assert.Equal([seed.YuaId], (await sceneService.GetScenesAsync(seed.MovieId))[0].Actors.Select(a => a.ActorId));
    }

    [Fact]
    public async Task DeletingAScene_RemovesItsActorAndTagLinks()
    {
        var seed = await SeedAsync();
        var tag = await AddTagAsync("Bath");
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId);
        await sceneService.AddSceneTagAsync(seed.SceneId, tag);

        await sceneService.DeleteSceneAsync(seed.SceneId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.SceneActors.ToListAsync());
        Assert.Empty(await db.SceneTags.ToListAsync());
    }

    [Fact]
    public async Task TagMerge_MovesSceneTagsToTarget_AndDedups()
    {
        var seed = await SeedAsync();
        int secondSceneId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var second = new Scene { MovieId = seed.MovieId, StartSeconds = 600 };
            db.Scenes.Add(second);
            await db.SaveChangesAsync();
            secondSceneId = second.Id;
        }
        var source = await AddTagAsync("Solowork");
        var target = await AddTagAsync("Solo");
        await sceneService.AddSceneTagAsync(seed.SceneId, source);
        await sceneService.AddSceneTagAsync(seed.SceneId, target);
        await sceneService.AddSceneTagAsync(secondSceneId, source);

        var result = await new TagService(factory, Substitute.For<INfoSyncService>()).MergeAsync(source, target);

        Assert.True(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        var links = await verify.SceneTags.OrderBy(st => st.SceneId).Select(st => new { st.SceneId, st.TagId }).ToListAsync();
        Assert.Equal([new { SceneId = seed.SceneId, TagId = target }, new { SceneId = secondSceneId, TagId = target }], links);
    }

    [Fact]
    public async Task TagMergeMany_MovesSceneTagsFromAllSources()
    {
        var seed = await SeedAsync();
        var a = await AddTagAsync("Solo Work");
        var b = await AddTagAsync("Solowork");
        var target = await AddTagAsync("Solo");
        await sceneService.AddSceneTagAsync(seed.SceneId, a);
        await sceneService.AddSceneTagAsync(seed.SceneId, b);

        var result = await new TagService(factory, Substitute.For<INfoSyncService>()).MergeManyAsync([a, b], target);

        Assert.True(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal([target], await verify.SceneTags.Select(st => st.TagId).ToListAsync());
    }

    [Fact]
    public async Task ActorMerge_MovesSceneActorsToTarget_AndDedups()
    {
        var seed = await SeedAsync();
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.AikaId);

        // Merge Aika into Yua: Yua is already on the movie and the scene gets Yua (once).
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.YuaId);
        var merged = await new ActorService(factory, Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>())
            .MergeAsync(seed.AikaId, seed.YuaId);

        Assert.True(merged.Success);
        Assert.Equal([seed.YuaId], (await sceneService.GetScenesAsync(seed.MovieId))[0].Actors.Select(a => a.ActorId));
    }

    [Fact]
    public async Task ActorMerge_SourceOnlyOnScene_IsReplacedByTarget()
    {
        var seed = await SeedAsync();
        await sceneService.AddSceneActorAsync(seed.SceneId, seed.AikaId);

        var merged = await new ActorService(factory, Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>())
            .MergeAsync(seed.AikaId, seed.YuaId);

        Assert.True(merged.Success);
        Assert.Equal([seed.YuaId], (await sceneService.GetScenesAsync(seed.MovieId))[0].Actors.Select(a => a.ActorId));
    }

    private async Task ApplyLibraryRulesAsync(Action<AppDbContext> addRules)
    {
        await using var db = await factory.CreateDbContextAsync();
        addRules(db);
        await db.SaveChangesAsync();
        await TagNormalization.ApplyToLibraryAsync(db);
        await db.SaveChangesAsync();
    }

    private async Task<List<int>> SceneTagIdsAsync(int sceneId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.SceneTags.Where(st => st.SceneId == sceneId).Select(st => st.TagId).OrderBy(id => id).ToListAsync();
    }

    [Fact]
    public async Task ApplyToLibrary_RemovesIgnoredSceneTags_AndPrunesTheOrphanedTag()
    {
        // The ignore list applies to scene tags, not just movie tags.
        var seed = await SeedAsync();
        var sample = await AddTagAsync("Sample");
        var keep = await AddTagAsync("Keep");
        await AddSceneOnlyTagAsync(seed.SceneId, sample);
        await AddSceneOnlyTagAsync(seed.SceneId, keep);

        await ApplyLibraryRulesAsync(db =>
            db.IgnoredTags.Add(new IgnoredTag { Value = "sample", MatchMode = TagMatchMode.CaseInsensitive }));

        Assert.Equal([keep], await SceneTagIdsAsync(seed.SceneId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.Tags.AnyAsync(t => t.Id == sample));
    }

    [Fact]
    public async Task ApplyToLibrary_RepointsSceneTagsMatchingAReplacementRule_AndTheMovieGetsTheTargetThroughItsClips()
    {
        var seed = await SeedAsync();
        var solowork = await AddTagAsync("Solowork");
        var solo = await AddTagAsync("Solo");
        await AddSceneOnlyTagAsync(seed.SceneId, solowork);

        await ApplyLibraryRulesAsync(db => db.TagReplacementRules.Add(
            new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = solo }));

        Assert.Equal([solo], await SceneTagIdsAsync(seed.SceneId));
        // Not copied as the movie's own tag: it rolls up as a clip-only one.
        Assert.Equal([solo], await MovieTagIdsAsync(seed.MovieId));
        Assert.Equal([(solo, false, true)], await MovieTagFlagsAsync(seed.MovieId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.Tags.AnyAsync(t => t.Id == solowork));
    }

    [Fact]
    public async Task ApplyToLibrary_ReplacementOntoATagTheSceneAlreadyHas_LeavesOneLink()
    {
        var seed = await SeedAsync();
        var solowork = await AddTagAsync("Solowork");
        var soloPerformer = await AddTagAsync("Solo Performer");
        var solo = await AddTagAsync("Solo");
        await AddSceneOnlyTagAsync(seed.SceneId, solowork);
        await AddSceneOnlyTagAsync(seed.SceneId, soloPerformer);
        await AddSceneOnlyTagAsync(seed.SceneId, solo);

        await ApplyLibraryRulesAsync(db => db.TagReplacementRules.AddRange(
            new TagReplacementRule { SourceValue = "Solowork", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = solo },
            new TagReplacementRule { SourceValue = "Solo Performer", MatchMode = TagMatchMode.CaseInsensitive, TargetTagId = solo }));

        Assert.Equal([solo], await SceneTagIdsAsync(seed.SceneId));
    }

    [Fact]
    public async Task OrphanPruning_KeepsATagStillUsedOnAScene()
    {
        // Rules match a subtag by its "Parent##Name" path, like MetaGenres does for movies, so an
        // ignore rule for the bare name leaves the scene's subtag link in place, and pruning
        // (which matches bare names) must still spare the tag.
        var seed = await SeedAsync();
        var play = await AddTagAsync("Play");
        var sample = await AddTagAsync("Sample", play);
        await AddSceneOnlyTagAsync(seed.SceneId, sample);

        await ApplyLibraryRulesAsync(db =>
            db.IgnoredTags.Add(new IgnoredTag { Value = "Sample", MatchMode = TagMatchMode.CaseInsensitive }));

        Assert.Equal([sample], await SceneTagIdsAsync(seed.SceneId));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.True(await verify.Tags.AnyAsync(t => t.Id == sample));
    }

    [Fact]
    public async Task GenreFilter_IgnoresSceneOnlyTags()
    {
        // The Genre filter matches movie tags only; a scene tag reaches it via the
        // movie tag that adding it to the scene creates.
        var seed = await SeedAsync();
        var play = await AddTagAsync("Play");
        var rough = await AddTagAsync("Rough", play);
        await AddSceneOnlyTagAsync(seed.SceneId, rough);
        var grid = new MovieGridQueryService(factory);

        Assert.Equal(0, await grid.CountAsync(new MovieGridFilter(Genres: ["Rough"])));
        Assert.Equal(0, await grid.CountAsync(new MovieGridFilter(Genres: ["Play"])));
        Assert.DoesNotContain("Play", (await grid.GetSummaryAsync(new MovieGridFilter())).Genres);
    }

    [Fact]
    public async Task TagList_ReportsSceneUsageSeparately()
    {
        var seed = await SeedAsync();
        var bath = await AddTagAsync("Bath");
        await AddSceneOnlyTagAsync(seed.SceneId, bath);

        var tags = await new TagService(factory, Substitute.For<INfoSyncService>()).GetTagsAsync();

        var item = Assert.Single(tags);
        Assert.Equal(0, item.MovieCount);
        Assert.Equal(1, item.SceneCount);
    }

    private async Task<(int TagId, bool IsExplicit, bool FromClips)[]> MovieTagFlagsAsync(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.MovieTags.Where(mt => mt.MovieId == movieId).OrderBy(mt => mt.TagId).ToListAsync())
            .Select(mt => (mt.TagId, mt.IsExplicit, mt.FromClips)).ToArray();
    }

    [Fact]
    public async Task AddingASceneTag_TagsTheMovieThroughItsClips_EvenWhenItHasTheParent()
    {
        var seed = await SeedAsync();
        var cosplay = await AddTagAsync("Cosplay");
        var nurse = await AddTagAsync("Nurse", cosplay);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieTags.Add(new MovieTag { MovieId = seed.MovieId, TagId = cosplay });
            await db.SaveChangesAsync();
        }

        Assert.True((await sceneService.AddSceneTagAsync(seed.SceneId, nurse)).Success);

        // Rolled up as a clip-only movie tag, exact match like before.
        Assert.Equal([(cosplay, true, false), (nurse, false, true)], await MovieTagFlagsAsync(seed.MovieId));
        await using var verify = await factory.CreateDbContextAsync();
        var metaGenres = await verify.Movies.Where(m => m.Id == seed.MovieId).Select(m => m.MetaGenres).SingleAsync();
        Assert.Contains("Nurse", metaGenres);
        await nfoSync.Received(1).CheckMovieNfoConflictAsync(seed.MovieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddingASceneTag_TheMovieAlreadyHas_KeepsItExplicit()
    {
        var seed = await SeedAsync();
        var bath = await AddTagAsync("Bath");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieTags.Add(new MovieTag { MovieId = seed.MovieId, TagId = bath });
            await db.SaveChangesAsync();
        }

        Assert.True((await sceneService.AddSceneTagAsync(seed.SceneId, bath)).Success);

        Assert.Equal([(bath, true, true)], await MovieTagFlagsAsync(seed.MovieId));
    }

    [Fact]
    public async Task RemovingASceneTagOrScene_DropsTheClipOnlyMovieTag()
    {
        var seed = await SeedAsync();
        var bath = await AddTagAsync("Bath");
        await sceneService.AddSceneTagAsync(seed.SceneId, bath);

        await sceneService.RemoveSceneTagAsync(seed.SceneId, bath);
        Assert.Empty(await MovieTagFlagsAsync(seed.MovieId));

        await sceneService.AddSceneTagAsync(seed.SceneId, bath);
        await sceneService.DeleteSceneAsync(seed.SceneId);
        Assert.Empty(await MovieTagFlagsAsync(seed.MovieId));
    }
}
