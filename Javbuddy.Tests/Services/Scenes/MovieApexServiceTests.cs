using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

public class MovieApexServiceTests
{
    private static async Task<int> SeedMovieAsync(TestDbContextFactory factory, double? durationSeconds = 900, string code = "ABC-123")
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, MediaDurationSeconds = durationSeconds };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private static async Task<int[]> SeedTagsAsync(TestDbContextFactory factory, params string[] names)
    {
        await using var db = await factory.CreateDbContextAsync();
        var tags = names.Select(n => new Tag { Name = n }).ToList();
        db.Tags.AddRange(tags);
        await db.SaveChangesAsync();
        return tags.Select(t => t.Id).ToArray();
    }

    [Fact]
    public async Task AddApex_ListsByTimeWithPositionsAndTagsByName()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "Zeta", "Alpha");
        var service = new MovieApexService(factory);

        var late = await service.AddApexAsync(movieId, 600, tagIds);
        var early = await service.AddApexAsync(movieId, 120.5, []);

        Assert.True(late.Success);
        Assert.True(early.Success);
        var apexes = await service.GetApexesAsync(movieId);
        Assert.Collection(apexes,
            a =>
            {
                Assert.Equal(early.ApexId, a.Id);
                Assert.Equal(1, a.Position);
                Assert.Equal(120.5, a.Seconds);
                Assert.Empty(a.Tags);
                Assert.Equal("Apex", a.DisplayLabel);
            },
            a =>
            {
                Assert.Equal(2, a.Position);
                Assert.Equal(["Alpha", "Zeta"], a.Tags.Select(t => t.Name));
                Assert.Equal("Alpha, Zeta", a.DisplayLabel);
            });
    }

    [Fact]
    public async Task Apexes_PlayWithTheirOwnLeadInAndTail_ElseTheDefaults()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var settings = Substitute.For<IApexPlaybackSettingsService>();
        settings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new ApexWindow(4, 8));
        var service = new MovieApexService(factory, playbackSettings: settings);

        var plain = await service.AddApexAsync(movieId, 100, []);
        var custom = await service.AddApexAsync(movieId, 200, [], window: new ApexWindowOverride(null, 20));

        Assert.Equal(new ApexWindow(4, 8), await service.GetDefaultWindowAsync());
        var apexes = await service.GetApexesAsync(movieId);
        Assert.Equal([(plain.ApexId, (double?)null, (double?)null, new ApexWindow(4, 8)), (custom.ApexId, null, 20, new ApexWindow(4, 20))],
            apexes.Select(a => ((int?)a.Id, a.OwnLeadInSeconds, a.OwnTailSeconds, a.Window)));
    }

    [Fact]
    public async Task UpdateApex_ReplacesItsWindow_OnlyWhenGiven()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieApexService(factory);
        var apexId = (await service.AddApexAsync(movieId, 100, [], window: new ApexWindowOverride(2, 30))).ApexId!.Value;

        // Moving it without a window keeps its own, relative to the new time.
        await service.UpdateApexAsync(apexId, 150, []);
        var moved = Assert.Single(await service.GetApexesAsync(movieId));
        Assert.Equal((150d, new ApexWindow(2, 30)), (moved.Seconds, moved.Window));

        await service.UpdateApexAsync(apexId, 150, [], window: ApexWindowOverride.None);
        var reset = Assert.Single(await service.GetApexesAsync(movieId));
        Assert.Equal(((double?)null, (double?)null, ApexWindow.Default), (reset.OwnLeadInSeconds, reset.OwnTailSeconds, reset.Window));
    }

    [Fact]
    public async Task AddAndUpdateApex_RejectAnInvalidWindow_WithoutSaving()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieApexService(factory);

        Assert.False((await service.AddApexAsync(movieId, 100, [], window: new ApexWindowOverride(-1, null))).Success);
        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        var result = await service.UpdateApexAsync(apexId, 120, [], window: new ApexWindowOverride(null, 0));

        Assert.False(result.Success);
        var apex = Assert.Single(await service.GetApexesAsync(movieId));
        Assert.Equal((100d, (double?)null), (apex.Seconds, apex.OwnTailSeconds));
    }

    [Fact]
    public async Task AddApex_RejectsBadTimesMissingMoviesAndUnknownTags_WithoutSaving()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieApexService(factory);

        var pastEnd = await service.AddApexAsync(movieId, 901, []);
        var negative = await service.AddApexAsync(movieId, -1, []);
        var noMovie = await service.AddApexAsync(movieId + 1, 10, []);
        var noTag = await service.AddApexAsync(movieId, 10, [999]);

        Assert.All([pastEnd, negative, noMovie, noTag], r => Assert.False(r.Success));
        Assert.Equal("Movie not found.", noMovie.ErrorMessage);
        Assert.Equal("Tag not found.", noTag.ErrorMessage);
        Assert.Empty(await service.GetApexesAsync(movieId));
    }

    [Fact]
    public async Task UpdateApex_MovesItAndReplacesItsTags()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A", "B", "C");
        var service = new MovieApexService(factory);
        var id = (await service.AddApexAsync(movieId, 100, [tagIds[0], tagIds[1]])).ApexId!.Value;

        var result = await service.UpdateApexAsync(id, 200, [tagIds[1], tagIds[2], tagIds[2]]);

        Assert.True(result.Success);
        var apex = Assert.Single(await service.GetApexesAsync(movieId));
        Assert.Equal(200, apex.Seconds);
        Assert.Equal(["B", "C"], apex.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task UpdateApex_RejectsInvalidTimeAndUnknownApex()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieApexService(factory);
        var id = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;

        Assert.False((await service.UpdateApexAsync(id, 1000, [])).Success);
        Assert.Equal("Apex not found.", (await service.UpdateApexAsync(id + 1, 10, [])).ErrorMessage);
        Assert.Equal(100, Assert.Single(await service.GetApexesAsync(movieId)).Seconds);
    }

    [Fact]
    public async Task DeleteApex_RemovesItAndItsTagLinks_ButNotTheTag()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A");
        var service = new MovieApexService(factory);
        var id = (await service.AddApexAsync(movieId, 100, tagIds)).ApexId!.Value;

        Assert.True((await service.DeleteApexAsync(id)).Success);
        Assert.False((await service.DeleteApexAsync(id)).Success);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ApexTags.ToListAsync());
        Assert.Single(await db.Tags.ToListAsync());
    }

    [Fact]
    public async Task DeletingTheMovie_CascadesToItsApexes()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A");
        await new MovieApexService(factory).AddApexAsync(movieId, 100, tagIds);

        await using var db = await factory.CreateDbContextAsync();
        await db.Movies.Where(m => m.Id == movieId).ExecuteDeleteAsync();

        Assert.Empty(await db.MovieApexes.ToListAsync());
        Assert.Empty(await db.ApexTags.ToListAsync());
    }

    private static async Task AddPreviewRowAsync(TestDbContextFactory factory, int apexId, double stampedSeconds, DateTime updatedAt)
    {
        await using var db = await factory.CreateDbContextAsync();
        var ms = SceneMediaService.StartMs(stampedSeconds);
        db.CachedImages.Add(new CachedImage
        {
            Code = "ABC-123",
            Role = ApexMediaService.Role,
            Index = apexId,
            Variant = SceneMediaService.VariantPreview,
            StorageId = Guid.NewGuid(),
            SceneStartMs = ms,
            SceneEndMs = ms,
            UpdatedAt = updatedAt,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ApexList_ReportsAPreviewVersion_OnlyForAPreviewOfTheApexsCurrentTime()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var media = Substitute.For<IApexMediaService>();
        media.ServesPreview.Returns(true);
        media.IsCurrent(Arg.Any<CachedImage>(), Arg.Any<SceneMediaWindow>())
            .Returns(call => call.ArgAt<CachedImage>(0).SceneStartMs == call.ArgAt<SceneMediaWindow>(1).StartMs);
        var service = new MovieApexService(factory, media);
        var current = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        var moved = (await service.AddApexAsync(movieId, 200, [])).ApexId!.Value;
        var none = (await service.AddApexAsync(movieId, 300, [])).ApexId!.Value;
        var updatedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await AddPreviewRowAsync(factory, current, 100, updatedAt);
        await AddPreviewRowAsync(factory, moved, 150, updatedAt);

        var apexes = await service.GetApexesAsync(movieId);

        Assert.Equal([updatedAt.Ticks, null, null], apexes.Select(a => a.PreviewVersion));
        Assert.Equal([current, moved, none], apexes.Select(a => a.Id));
    }

    [Fact]
    public async Task ApexList_ReportsNoPreviewVersion_WhenTheCacheModeServesNone()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var media = Substitute.For<IApexMediaService>();
        media.IsCurrent(Arg.Any<CachedImage>(), Arg.Any<SceneMediaWindow>()).Returns(true);
        var service = new MovieApexService(factory, media);
        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        await AddPreviewRowAsync(factory, apexId, 100, DateTime.UtcNow);

        Assert.Null(Assert.Single(await service.GetApexesAsync(movieId)).PreviewVersion);
    }

    [Fact]
    public async Task DeleteApex_AlsoDeletesItsPreview()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var media = Substitute.For<IApexMediaService>();
        var service = new MovieApexService(factory, media);
        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;

        Assert.True((await service.DeleteApexAsync(apexId)).Success);

        await media.Received(1).DeleteForApexAsync(apexId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddingOrMovingAnApex_QueuesItsPreview_ButRetaggingDoesnt()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "Facial");
        var media = Substitute.For<IApexMediaService>();
        var service = new MovieApexService(factory, media);

        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        await media.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());

        media.ClearReceivedCalls();
        await service.UpdateApexAsync(apexId, 100, tagIds);
        await media.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);

        await service.UpdateApexAsync(apexId, 120, tagIds);
        await media.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());
    }

    /// <summary>Seeds the movie's cast (in the given order) and returns their ids.</summary>
    private static async Task<int[]> SeedCastAsync(TestDbContextFactory factory, int movieId, params string[] names)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actors = names.Select(n => new Actor { FirstName = n }).ToList();
        db.Actors.AddRange(actors);
        await db.SaveChangesAsync();
        db.MovieActors.AddRange(actors.Select(a => new MovieActor { MovieId = movieId, ActorId = a.Id }));
        await db.SaveChangesAsync();
        return actors.Select(a => a.Id).ToArray();
    }

    private static async Task<int> SeedSceneAsync(TestDbContextFactory factory, int movieId, double start, double? end, params int[] actorIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        var scene = new Scene { MovieId = movieId, StartSeconds = start, EndSeconds = end };
        foreach (var actorId in actorIds)
        {
            scene.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = actorId });
        }
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return scene.Id;
    }

    /// <summary>The apex's effective actors: its own, else inherited.</summary>
    private static async Task<IReadOnlyList<int>> ActorIdsOfAsync(MovieApexService service, int movieId, int apexId) =>
        (await service.GetApexesAsync(movieId)).Single(a => a.Id == apexId).EffectiveActors.Actors.Select(a => a.ActorId).ToList();

    private static async Task<IReadOnlyList<int>> OwnActorIdsOfAsync(MovieApexService service, int movieId, int apexId) =>
        (await service.GetApexesAsync(movieId)).Single(a => a.Id == apexId).Actors.Select(a => a.ActorId).ToList();

    private static async Task<int> SeedHighlightAsync(TestDbContextFactory factory, int movieId, double start, double end, params int[] actorIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = start, EndSeconds = end };
        foreach (var actorId in actorIds)
        {
            highlight.HighlightActors.Add(new HighlightActor { MovieId = movieId, ActorId = actorId });
        }
        db.MovieHighlights.Add(highlight);
        await db.SaveChangesAsync();
        return highlight.Id;
    }

    [Fact]
    public async Task AddApex_WithoutActorIds_InheritsTheEnclosingScenesActors_WithoutStoringThem()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea", "Cora");
        await SeedSceneAsync(factory, movieId, 0, 300, cast[0]);
        // Open-ended: runs to the movie's end.
        await SeedSceneAsync(factory, movieId, 300, null, cast[1], cast[2]);
        var service = new MovieApexService(factory);

        var inFirst = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        var atBoundary = (await service.AddApexAsync(movieId, 300, [])).ApexId!.Value;
        var inOpenEnded = (await service.AddApexAsync(movieId, 800, [])).ApexId!.Value;

        Assert.Equal([cast[0]], await ActorIdsOfAsync(service, movieId, inFirst));
        Assert.Equal([cast[1], cast[2]], await ActorIdsOfAsync(service, movieId, atBoundary));
        Assert.Equal([cast[1], cast[2]], await ActorIdsOfAsync(service, movieId, inOpenEnded));
        var item = (await service.GetApexesAsync(movieId)).Single(a => a.Id == inFirst);
        Assert.Equal((ActorSource.Scene, "scene 1"), (item.EffectiveActors.Source, item.EffectiveActors.From));
        Assert.Empty(item.Actors);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ApexActors.ToListAsync());
    }

    [Fact]
    public async Task AddApex_OutsideAnySceneOrInASceneWithoutActors_InheritsTheWholeCast()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        await SeedSceneAsync(factory, movieId, 100, 200);
        await SeedSceneAsync(factory, movieId, 300, 400, cast[0]);
        var service = new MovieApexService(factory);

        var beforeScenes = (await service.AddApexAsync(movieId, 50, [])).ApexId!.Value;
        var inEmptyScene = (await service.AddApexAsync(movieId, 150, [])).ApexId!.Value;
        var inGap = (await service.AddApexAsync(movieId, 250, [])).ApexId!.Value;

        foreach (var id in new[] { beforeScenes, inEmptyScene, inGap })
        {
            Assert.Equal(cast, await ActorIdsOfAsync(service, movieId, id));
        }
    }

    [Fact]
    public async Task ApexInsideAHighlightWithActors_InheritsTheHighlights()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        await SeedSceneAsync(factory, movieId, 0, 300, cast[0], cast[1]);
        await SeedHighlightAsync(factory, movieId, 50, 150, cast[1]);
        var service = new MovieApexService(factory);

        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;

        var item = (await service.GetApexesAsync(movieId)).Single();
        Assert.Equal([cast[1]], await ActorIdsOfAsync(service, movieId, apexId));
        Assert.Equal((ActorSource.Highlight, "highlight 1"), (item.EffectiveActors.Source, item.EffectiveActors.From));
        // The untitled label still names who's in it.
        Assert.Equal("Bea", item.DisplayLabel);
    }

    [Fact]
    public async Task ApexInASoloMovie_LeavesTheActorOutOfItsLabel()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Mei");
        var tagIds = await SeedTagsAsync(factory, "Kiss");
        var service = new MovieApexService(factory);

        await service.AddApexAsync(movieId, 100, tagIds, [cast[0]]);
        await service.AddApexAsync(movieId, 200, []);

        // Only the tags, else "Apex".
        Assert.Equal(["Kiss", "Apex"], (await service.GetApexesAsync(movieId)).Select(a => a.DisplayLabel));
    }

    [Fact]
    public async Task AddApex_WithActorIds_UsesThemAndRejectsActorsOutsideTheCast()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var otherMovieId = await SeedMovieAsync(factory, code: "XYZ-999");
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        var outsider = (await SeedCastAsync(factory, otherMovieId, "Cora"))[0];
        await SeedSceneAsync(factory, movieId, 0, null, cast[0]);
        var service = new MovieApexService(factory);

        var none = await service.AddApexAsync(movieId, 100, [], []);
        var chosen = await service.AddApexAsync(movieId, 200, [], [cast[1], cast[1]]);
        var invalid = await service.AddApexAsync(movieId, 300, [], [cast[0], outsider]);

        // An empty choice means "inherit".
        Assert.Empty(await OwnActorIdsOfAsync(service, movieId, none.ApexId!.Value));
        Assert.Equal([cast[0]], await ActorIdsOfAsync(service, movieId, none.ApexId!.Value));
        Assert.Equal([cast[1]], await OwnActorIdsOfAsync(service, movieId, chosen.ApexId!.Value));
        Assert.Equal([cast[1]], await ActorIdsOfAsync(service, movieId, chosen.ApexId!.Value));
        Assert.False(invalid.Success);
        Assert.Equal("Actor is not in the movie's cast.", invalid.ErrorMessage);
        Assert.Equal(2, (await service.GetApexesAsync(movieId)).Count);
    }

    [Fact]
    public async Task UpdateApex_ReplacesActorsWhenGiven_ClearsToInheritedWithNone_AndInheritanceFollowsAMove()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea", "Cora");
        await SeedSceneAsync(factory, movieId, 0, 300, cast[0]);
        await SeedSceneAsync(factory, movieId, 300, 600, cast[1]);
        var service = new MovieApexService(factory);
        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;

        Assert.True((await service.UpdateApexAsync(apexId, 400, [])).Success);
        Assert.Equal([cast[1]], await ActorIdsOfAsync(service, movieId, apexId));

        Assert.True((await service.UpdateApexAsync(apexId, 400, [], [cast[2], cast[1]])).Success);
        Assert.Equal([cast[1], cast[2]], await ActorIdsOfAsync(service, movieId, apexId));

        var otherMovieId = await SeedMovieAsync(factory, code: "XYZ-999");
        var outsider = (await SeedCastAsync(factory, otherMovieId, "Dana"))[0];
        Assert.Equal("Actor is not in the movie's cast.", (await service.UpdateApexAsync(apexId, 400, [], [outsider])).ErrorMessage);
        Assert.Equal([cast[1], cast[2]], await ActorIdsOfAsync(service, movieId, apexId));

        Assert.True((await service.UpdateApexAsync(apexId, 100, [], [])).Success);
        Assert.Empty(await OwnActorIdsOfAsync(service, movieId, apexId));
        Assert.Equal([cast[0]], await ActorIdsOfAsync(service, movieId, apexId));
    }

    [Fact]
    public async Task LeavingTheCast_RemovesTheActorFromTheMoviesApexes_OwnAndInherited()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        var service = new MovieApexService(factory);
        var inheriting = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        var own = (await service.AddApexAsync(movieId, 200, [], cast)).ApexId!.Value;

        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.MovieActors.Where(ma => ma.MovieId == movieId && ma.ActorId == cast[0]).ExecuteDeleteAsync();
        }

        Assert.Equal([cast[1]], await ActorIdsOfAsync(service, movieId, inheriting));
        Assert.Equal([cast[1]], await ActorIdsOfAsync(service, movieId, own));
    }

    [Fact]
    public async Task GetActorOptions_ListsTheCastByName()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        await SeedCastAsync(factory, movieId, "Cora", "Aika");

        var options = await new MovieApexService(factory).GetActorOptionsAsync(movieId);

        Assert.Equal(["Aika", "Cora"], options.Cast.Select(a => a.Name));
    }

    [Fact]
    public async Task ToggleFavorite_FlipsTheFlagAndFavoritedAt()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieApexService(factory);
        var apexId = (await service.AddApexAsync(movieId, 100, [])).ApexId!.Value;
        Assert.False(Assert.Single(await service.GetApexesAsync(movieId)).IsFavorite);

        Assert.True(await service.ToggleFavoriteAsync(apexId));
        Assert.True(Assert.Single(await service.GetApexesAsync(movieId)).IsFavorite);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.NotNull((await db.MovieApexes.SingleAsync()).FavoritedAt);
        }

        Assert.False(await service.ToggleFavoriteAsync(apexId));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var apex = await db.MovieApexes.SingleAsync();
            Assert.False(apex.IsFavorite);
            Assert.Null(apex.FavoritedAt);
        }

        Assert.False(await service.ToggleFavoriteAsync(apexId + 1));
    }

    private static MovieApexService ServiceWithClipTags(TestDbContextFactory factory) =>
        new(factory, clipTags: new ClipTagSyncService(factory, Substitute.For<INfoSyncService>()));

    private static async Task<int[]> SceneTagIdsAsync(TestDbContextFactory factory, int sceneId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.SceneTags.Where(st => st.SceneId == sceneId).Select(st => st.TagId).OrderBy(id => id).ToArrayAsync();
    }

    private static async Task<(int TagId, bool IsExplicit, bool FromClips)[]> MovieTagsAsync(TestDbContextFactory factory, int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.MovieTags.Where(mt => mt.MovieId == movieId).OrderBy(mt => mt.TagId).ToListAsync())
            .Select(mt => (mt.TagId, mt.IsExplicit, mt.FromClips)).ToArray();
    }

    [Fact]
    public async Task AddApex_LeavesTheSceneAlone_AndTagsTheMovieThroughItsClips()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "Creampie", "Squirt");
        var scene = await SeedSceneAsync(factory, movieId, 0, 300);
        var service = ServiceWithClipTags(factory);

        var result = await service.AddApexAsync(movieId, 100, tagIds);

        // Rolled up, not copied: the scene shows them as implicit, the movie holds clip-only rows.
        Assert.True(result.MovieTagsChanged);
        Assert.Empty(await SceneTagIdsAsync(factory, scene));
        Assert.Equal(tagIds.Select(id => (id, false, true)), await MovieTagsAsync(factory, movieId));
        Assert.False((await service.AddApexAsync(movieId, 200, tagIds)).MovieTagsChanged);
        Assert.False((await service.AddApexAsync(movieId, 250, [])).MovieTagsChanged);
    }

    [Fact]
    public async Task RemovingAnApexTagOrTheApex_DropsTheClipOnlyMovieTag()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A", "B");
        var service = ServiceWithClipTags(factory);
        var apexId = (await service.AddApexAsync(movieId, 100, tagIds)).ApexId!.Value;

        Assert.True((await service.UpdateApexAsync(apexId, 100, [tagIds[0]])).MovieTagsChanged);
        Assert.Equal([(tagIds[0], false, true)], await MovieTagsAsync(factory, movieId));

        await service.DeleteApexAsync(apexId);
        Assert.Empty(await MovieTagsAsync(factory, movieId));
    }

    [Fact]
    public async Task RemovingAnApexTag_KeepsAnExplicitMovieTag()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = tagIds[0] });
            await db.SaveChangesAsync();
        }
        var service = ServiceWithClipTags(factory);
        var apexId = (await service.AddApexAsync(movieId, 100, tagIds)).ApexId!.Value;
        Assert.Equal([(tagIds[0], true, true)], await MovieTagsAsync(factory, movieId));

        await service.UpdateApexAsync(apexId, 100, []);

        Assert.Equal([(tagIds[0], true, false)], await MovieTagsAsync(factory, movieId));
    }
}
