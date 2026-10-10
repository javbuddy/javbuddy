using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tags;

public sealed class ActorTagServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ActorTagService service;
    private readonly TagService tagService;
    private int movieId, meiId, rinId, sceneId, highlightId, apexId, blondeId, plainId;

    public ActorTagServiceTests()
    {
        var nfo = Substitute.For<INfoSyncService>();
        service = new ActorTagService(factory, new ClipTagSyncService(factory, nfo));
        tagService = new TagService(factory, nfo);
    }

    public void Dispose() => factory.Dispose();

    // Cast Mei (Rin exists but isn't in the cast). Scene from 0, highlight 10–100, apex at 50. One actor tag, one plain.
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var mei = new Actor { FirstName = "Mei" };
        var rin = new Actor { FirstName = "Rin" };
        var movie = new Movie { Code = "ATS-001", MediaDurationSeconds = 3600 };
        var blonde = new Tag { Name = "Blonde", IsActorTag = true };
        var plain = new Tag { Name = "Cosplay" };
        db.AddRange(mei, rin, movie, blonde, plain);
        await db.SaveChangesAsync();
        (movieId, meiId, rinId, blondeId, plainId) = (movie.Id, mei.Id, rin.Id, blonde.Id, plain.Id);
        db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = meiId });
        var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
        var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
        db.AddRange(scene, highlight, apex);
        await db.SaveChangesAsync();
        (sceneId, highlightId, apexId) = (scene.Id, highlight.Id, apex.Id);
    }

    [Fact]
    public async Task Set_StoresOwnTagsAtEachLevel_AndGivesTheMovieThePlainTag()
    {
        await SeedAsync();

        Assert.True((await service.SetAsync(ActorTagLevel.Apex, apexId, meiId, [blondeId])).MovieTagsChanged);
        Assert.True((await service.SetAsync(ActorTagLevel.Scene, sceneId, meiId, [blondeId])).Success);
        Assert.True((await service.SetAsync(ActorTagLevel.Highlight, highlightId, meiId, [blondeId])).Success);
        Assert.True((await service.SetAsync(ActorTagLevel.Movie, movieId, meiId, [blondeId])).Success);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.ApexActorTags.ToListAsync());
        Assert.Single(await db.SceneActorTags.ToListAsync());
        Assert.Single(await db.HighlightActorTags.ToListAsync());
        Assert.Single(await db.MovieActorTags.ToListAsync());
        var link = await db.MovieTags.SingleAsync(mt => mt.MovieId == movieId);
        Assert.True(link.FromClips);
        Assert.False(link.IsExplicit);
    }

    [Fact]
    public async Task Set_ReplacesTheActorsTags_AndAnEmptySetMakesThemInheritAgain()
    {
        await SeedAsync();
        var other = (await service.CreateActorTagAsync("Brunette")).Tag!;
        await service.SetAsync(ActorTagLevel.Scene, sceneId, meiId, [blondeId]);

        await service.SetAsync(ActorTagLevel.Scene, sceneId, meiId, [other.Id]);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal([other.Id], await db.SceneActorTags.Select(t => t.TagId).ToListAsync());
            Assert.Equal([other.Id], await db.MovieTags.Select(mt => mt.TagId).ToListAsync());
        }

        Assert.True((await service.SetAsync(ActorTagLevel.Scene, sceneId, meiId, [])).Success);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.SceneActorTags.ToListAsync());
        Assert.Empty(await check.MovieTags.ToListAsync());
    }

    [Fact]
    public async Task Set_RefusesAPlainTag_AnActorOutsideTheCast_AndAMissingOwner()
    {
        await SeedAsync();

        Assert.False((await service.SetAsync(ActorTagLevel.Scene, sceneId, meiId, [plainId])).Success);
        Assert.False((await service.SetAsync(ActorTagLevel.Scene, sceneId, rinId, [blondeId])).Success);
        Assert.False((await service.SetAsync(ActorTagLevel.Scene, 999, meiId, [blondeId])).Success);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.SceneActorTags.ToListAsync());
    }

    [Fact]
    public async Task GetEffective_ReturnsTheInheritedAndRolledUpTags()
    {
        await SeedAsync();
        await service.SetAsync(ActorTagLevel.Apex, apexId, meiId, [blondeId]);

        var effective = await service.GetEffectiveAsync(movieId);

        Assert.Contains(effective.Scenes[sceneId], t => t is { ActorId: var a, IsRolledUp: true } && a == meiId);
        Assert.Contains(effective.Highlights[highlightId], t => t.IsRolledUp);
        Assert.Contains(effective.Apexes[apexId], t => !t.IsRolledUp);
    }

    [Fact]
    public async Task ActorTags_AreKeptOutOfThePlainTagPaths()
    {
        await SeedAsync();

        Assert.DoesNotContain(await tagService.GetTagsAsync(), t => t.Id == blondeId);
        Assert.False((await tagService.AddTagToMovieAsync(movieId, blondeId)).Success);
        Assert.False((await new MovieSceneService(factory).AddSceneTagAsync(sceneId, blondeId)).Success);
        Assert.False((await new MovieApexService(factory).UpdateApexAsync(apexId, 50, [blondeId], null)).Success);
        Assert.False((await new MovieHighlightService(factory).AddHighlightAsync(movieId, 5, 9, null, [blondeId])).Success);
        Assert.Empty(await tagService.GetMergeCandidatesAsync(blondeId));
        Assert.DoesNotContain(await tagService.GetMergeCandidatesAsync(plainId), c => c.Id == blondeId);
        Assert.False((await tagService.MergeAsync(plainId, blondeId)).Success);
        Assert.False((await tagService.MergeManyAsync([blondeId], plainId)).Success);
        Assert.False((await tagService.SetParentAsync(blondeId, plainId)).Success);
        Assert.False((await tagService.CreateTagAsync("Sub", blondeId)).Success);
    }

    [Fact]
    public async Task SetIsActorTag_OnlyForAnUnusedTag()
    {
        await SeedAsync();

        Assert.True((await service.SetIsActorTagAsync(plainId, true)).Success);
        Assert.True((await service.SetIsActorTagAsync(plainId, false)).Success);

        await service.SetAsync(ActorTagLevel.Movie, movieId, meiId, [blondeId]);
        Assert.False((await service.SetIsActorTagAsync(blondeId, false)).Success);
    }

    [Fact]
    public async Task MakingAGenreAnActorTag_QueuesTheDriftCheckForItsMovies()
    {
        await SeedAsync();
        await tagService.AddTagToMovieAsync(movieId, plainId);

        var queue = Substitute.For<INfoDriftCheckQueue>();
        var withQueue = new ActorTagService(factory, null, queue);
        await withQueue.SetIsActorTagAsync(plainId, true);

        queue.Received(1).Enqueue(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { movieId })));
    }

    [Theory]
    [InlineData(ActorTagLevel.Scene)]
    [InlineData(ActorTagLevel.Highlight)]
    [InlineData(ActorTagLevel.Apex)]
    public async Task RemovingAnActorFromAClipsOwnActors_DropsTheirOwnTagsThere_AndTheMoviesPlainTag(ActorTagLevel level)
    {
        await SeedAsync();
        var nfo = Substitute.For<INfoSyncService>();
        var clipTags = new ClipTagSyncService(factory, nfo);
        var ownerId = level switch { ActorTagLevel.Scene => sceneId, ActorTagLevel.Highlight => highlightId, _ => apexId };
        await using (var db = await factory.CreateDbContextAsync())
        {
            if (level == ActorTagLevel.Scene) db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = meiId });
            if (level == ActorTagLevel.Highlight) db.HighlightActors.Add(new HighlightActor { HighlightId = highlightId, MovieId = movieId, ActorId = meiId });
            if (level == ActorTagLevel.Apex) db.ApexActors.Add(new ApexActor { ApexId = apexId, MovieId = movieId, ActorId = meiId });
            await db.SaveChangesAsync();
        }
        await service.SetAsync(level, ownerId, meiId, [blondeId]);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True(await db.MovieTags.AnyAsync(t => t.MovieId == movieId && t.TagId == blondeId));
        }

        switch (level)
        {
            case ActorTagLevel.Scene: await new MovieSceneService(factory, null, clipTags).SetSceneActorsAsync(sceneId, []); break;
            case ActorTagLevel.Highlight: await new MovieHighlightService(factory, null, clipTags).UpdateHighlightAsync(highlightId, 10, 100, null, actorIds: []); break;
            default: await new MovieApexService(factory, clipTags: clipTags).UpdateApexAsync(apexId, 50, [], []); break;
        }

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Empty(await verify.SceneActorTags.ToListAsync());
        Assert.Empty(await verify.HighlightActorTags.ToListAsync());
        Assert.Empty(await verify.ApexActorTags.ToListAsync());
        Assert.False(await verify.MovieTags.AnyAsync(t => t.MovieId == movieId && t.TagId == blondeId));
    }

    [Fact]
    public async Task MakingAGenreAnActorTag_RemovesItFromTheMoviesGenres()
    {
        await SeedAsync();
        await tagService.AddTagToMovieAsync(movieId, plainId);

        Assert.True((await service.SetIsActorTagAsync(plainId, true)).Success);

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.MovieTags.AnyAsync(mt => mt.TagId == plainId));
        Assert.DoesNotContain("Cosplay", (await check.Movies.SingleAsync(m => m.Id == movieId)).MetaGenres ?? "");
        Assert.True((await check.Tags.SingleAsync(t => t.Id == plainId)).IsActorTag);
    }

    [Fact]
    public async Task APlainTagOnAScene_CantBecomeAnActorTag()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = plainId });
            await db.SaveChangesAsync();
        }

        Assert.False((await service.SetIsActorTagAsync(plainId, true)).Success);
    }

    [Fact]
    public async Task Create_RefusesADuplicateName()
    {
        await SeedAsync();

        Assert.False((await service.CreateActorTagAsync("blonde")).Success);
        Assert.False((await service.CreateActorTagAsync("Cosplay")).Success);
        Assert.Equal(["Blonde"], (await service.GetActorTagsAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task ActorTags_NestOneLevel_UnderAnActorTag()
    {
        await SeedAsync();
        var hair = (await service.CreateActorTagAsync("Hair")).Tag!;
        var longHair = (await service.CreateActorTagAsync("Long", hair.Id)).Tag!;

        Assert.Equal(hair.Id, longHair.ParentTagId);
        Assert.Equal(["Blonde", "Hair", "Hair › Long"], (await service.GetActorTagsAsync()).Select(t => t.Label));
        Assert.False((await service.CreateActorTagAsync("Tiny", longHair.Id)).Success);
        Assert.False((await service.CreateActorTagAsync("Odd", plainId)).Success);
        Assert.False((await tagService.SetParentAsync(longHair.Id, plainId)).Success);
        Assert.True((await tagService.SetParentAsync(blondeId, hair.Id)).Success);
        Assert.False((await service.SetIsActorTagAsync(hair.Id, false)).Success);
    }

    [Fact]
    public async Task SearchingActorTags_MatchesTheParentsName_Too()
    {
        await SeedAsync();
        var hair = (await service.CreateActorTagAsync("Hair")).Tag!;
        await service.CreateActorTagAsync("Long", hair.Id);

        Assert.Equal(["Hair", "Hair › Long"], (await service.GetActorTagsAsync("hair")).Select(t => t.Label));
    }
}
