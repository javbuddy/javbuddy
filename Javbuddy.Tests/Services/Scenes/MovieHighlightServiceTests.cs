using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

public class MovieHighlightServiceTests
{
    private static async Task<int> SeedMovieAsync(TestDbContextFactory factory, double? durationSeconds = 900, string code = "ABC-123")
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, MediaDurationSeconds = durationSeconds };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task AddHighlight_AllowsOverlapsAndListsByStartWithLanes()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieHighlightService(factory);

        var late = await service.AddHighlightAsync(movieId, 300, 360, "  Climax  ");
        var early = await service.AddHighlightAsync(movieId, 280, 320, "   ");
        var inside = await service.AddHighlightAsync(movieId, 290, 300, null);

        Assert.True(late.Success);
        Assert.True(early.Success);
        Assert.True(inside.Success);
        var highlights = await service.GetHighlightsAsync(movieId);
        Assert.Collection(highlights,
            h =>
            {
                Assert.Equal(early.HighlightId, h.Id);
                Assert.Null(h.Title);
                Assert.Equal("Highlight 1", h.DisplayTitle);
                Assert.Equal(0, h.Lane);
                Assert.Equal(40d, h.DurationSeconds);
            },
            h =>
            {
                Assert.Equal(inside.HighlightId, h.Id);
                Assert.Equal(1, h.Lane);
            },
            h =>
            {
                Assert.Equal("Climax", h.Title);
                Assert.Equal(3, h.Position);
                // (300,360) overlaps (280,320) but only touches (290,300), so it takes lane 1.
                Assert.Equal(1, h.Lane);
            });
    }

    [Fact]
    public async Task AddHighlight_RejectsInvalidRangesAndLongTitles_WithoutSaving()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieHighlightService(factory);

        var pastEnd = await service.AddHighlightAsync(movieId, 890, 901, null);
        var backwards = await service.AddHighlightAsync(movieId, 100, 50, null);
        var longTitle = await service.AddHighlightAsync(movieId, 0, 10, new string('x', 201));
        var noMovie = await service.AddHighlightAsync(movieId + 1, 0, 10, null);

        Assert.Equal("End can't be past the end of the movie.", pastEnd.ErrorMessage);
        Assert.Equal("End must be after start.", backwards.ErrorMessage);
        Assert.False(longTitle.Success);
        Assert.Equal("Movie not found.", noMovie.ErrorMessage);
        Assert.Empty(await service.GetHighlightsAsync(movieId));
    }

    [Fact]
    public async Task AddHighlight_UnknownDuration_SkipsDurationCheck()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory, durationSeconds: null);
        var service = new MovieHighlightService(factory);

        Assert.True((await service.AddHighlightAsync(movieId, 5000, 5100, null)).Success);
    }

    [Fact]
    public async Task UpdateHighlight_ChangesRangeAndTitle_AndValidates()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieHighlightService(factory);
        var id = (await service.AddHighlightAsync(movieId, 10, 20, "Old")).HighlightId!.Value;

        var invalid = await service.UpdateHighlightAsync(id, 10, 950, "New");
        var valid = await service.UpdateHighlightAsync(id, 15, 45, "New");
        var missing = await service.UpdateHighlightAsync(id + 1, 15, 45, null);

        Assert.False(invalid.Success);
        Assert.True(valid.Success);
        Assert.Equal("Highlight not found.", missing.ErrorMessage);
        var highlight = Assert.Single(await service.GetHighlightsAsync(movieId));
        Assert.Equal((15d, 45d, "New"), (highlight.StartSeconds, highlight.EndSeconds, highlight.Title));
    }

    [Fact]
    public async Task Favorite_RoundTrip()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieHighlightService(factory);
        var id = (await service.AddHighlightAsync(movieId, 10, 20, null)).HighlightId!.Value;

        Assert.True(await service.ToggleFavoriteAsync(id));

        var highlight = Assert.Single(await service.GetHighlightsAsync(movieId));
        Assert.True(highlight.IsFavorite);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.NotNull((await db.MovieHighlights.SingleAsync()).FavoritedAt);
        }

        Assert.False(await service.ToggleFavoriteAsync(id));
        highlight = Assert.Single(await service.GetHighlightsAsync(movieId));
        Assert.False(highlight.IsFavorite);
        Assert.False(await service.ToggleFavoriteAsync(id + 1));
    }

    [Fact]
    public async Task DeleteHighlight_RemovesOnlyThatHighlight_AndMovieDeleteCascades()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var service = new MovieHighlightService(factory);
        var first = (await service.AddHighlightAsync(movieId, 10, 20, null)).HighlightId!.Value;
        await service.AddHighlightAsync(movieId, 30, 40, null);

        Assert.True((await service.DeleteHighlightAsync(first)).Success);
        Assert.False((await service.DeleteHighlightAsync(first)).Success);
        Assert.Single(await service.GetHighlightsAsync(movieId));

        await using var db = await factory.CreateDbContextAsync();
        await db.Movies.Where(m => m.Id == movieId).ExecuteDeleteAsync();
        Assert.Empty(await db.MovieHighlights.ToListAsync());
    }

    [Fact]
    public async Task Edits_ResetTheSettleDelay_DeleteRemovesMedia_TitleOnlyEditDoesNotReset()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var media = Substitute.For<IHighlightMediaService>();
        var service = new MovieHighlightService(factory, media);

        var id = (await service.AddHighlightAsync(movieId, 10, 20, null)).HighlightId!.Value;
        await service.UpdateHighlightAsync(id, 10, 20, "Title only");
        await service.UpdateHighlightAsync(id, 10, 25, "Title only");
        await service.DeleteHighlightAsync(id);

        media.Received(3).NoteHighlightsChanged(movieId); // add, range edit, delete
        await media.Received(1).DeleteForHighlightAsync(id, Arg.Any<CancellationToken>());
    }

    // --- Actors and tags ---

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

    private static async Task<int[]> SeedTagsAsync(TestDbContextFactory factory, params string[] names)
    {
        await using var db = await factory.CreateDbContextAsync();
        var tags = names.Select(n => new Tag { Name = n }).ToList();
        db.Tags.AddRange(tags);
        await db.SaveChangesAsync();
        return tags.Select(t => t.Id).ToArray();
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

    private static async Task<HighlightItem> HighlightAsync(MovieHighlightService service, int movieId, int highlightId) =>
        (await service.GetHighlightsAsync(movieId)).Single(h => h.Id == highlightId);

    private static MovieHighlightService ServiceWithClipTags(TestDbContextFactory factory) =>
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
    public async Task AddHighlight_WithoutActorIds_InheritsTheActorsOfTheSceneItStartsIn_WithoutStoringThem()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea", "Cora");
        await SeedSceneAsync(factory, movieId, 0, 300, cast[0]);
        await SeedSceneAsync(factory, movieId, 300, null, cast[1], cast[2]);
        await SeedSceneAsync(factory, movieId, 700, 800); // no actors
        var service = new MovieHighlightService(factory);

        var crossing = (await service.AddHighlightAsync(movieId, 250, 350, null)).HighlightId!.Value;
        var atBoundary = (await service.AddHighlightAsync(movieId, 300, 320, null)).HighlightId!.Value;
        var inEmptyScene = (await service.AddHighlightAsync(movieId, 750, 760, null)).HighlightId!.Value;
        var inGap = (await service.AddHighlightAsync(movieId, 850, 860, null)).HighlightId!.Value;

        Assert.Equal([cast[0]], (await HighlightAsync(service, movieId, crossing)).EffectiveActors.Actors.Select(a => a.ActorId));
        Assert.Equal([cast[1], cast[2]], (await HighlightAsync(service, movieId, atBoundary)).EffectiveActors.Actors.Select(a => a.ActorId));
        Assert.Equal(cast, (await HighlightAsync(service, movieId, inEmptyScene)).EffectiveActors.Actors.Select(a => a.ActorId));
        Assert.Equal(cast, (await HighlightAsync(service, movieId, inGap)).EffectiveActors.Actors.Select(a => a.ActorId));
        var crossingItem = await HighlightAsync(service, movieId, crossing);
        Assert.Empty(crossingItem.Actors);
        Assert.Equal((ActorSource.Scene, "scene 1"), (crossingItem.EffectiveActors.Source, crossingItem.EffectiveActors.From));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.HighlightActors.ToListAsync());
    }

    [Fact]
    public async Task AddHighlight_WithActorsAndTags_StoresThem_AndRejectsOutsidersAndUnknownTags()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var otherMovieId = await SeedMovieAsync(factory, code: "XYZ-999");
        var cast = await SeedCastAsync(factory, movieId, "Bea", "Aika");
        var outsider = (await SeedCastAsync(factory, otherMovieId, "Cora"))[0];
        var tagIds = await SeedTagsAsync(factory, "Squirt", "Creampie");
        var service = new MovieHighlightService(factory);

        var none = await service.AddHighlightAsync(movieId, 10, 20, null, [], []);
        var chosen = await service.AddHighlightAsync(movieId, 30, 40, null, [tagIds[0], tagIds[1], tagIds[0]], [cast[0], cast[1]]);
        var badActor = await service.AddHighlightAsync(movieId, 50, 60, null, [], [outsider]);
        var badTag = await service.AddHighlightAsync(movieId, 50, 60, null, [tagIds[0] + 100], []);

        // No own actors: it inherits the cast (no scenes), and its untitled label names them.
        var noneItem = await HighlightAsync(service, movieId, none.HighlightId!.Value);
        Assert.Empty(noneItem.Actors);
        Assert.Equal(["Aika", "Bea"], noneItem.EffectiveActors.Actors.Select(a => a.Name));
        Assert.Equal("Aika, Bea", noneItem.DisplayTitle);
        var chosenItem = await HighlightAsync(service, movieId, chosen.HighlightId!.Value);
        Assert.Equal(["Aika", "Bea"], chosenItem.Actors.Select(a => a.Name));
        Assert.Equal(["Creampie", "Squirt"], chosenItem.Tags.Select(t => t.Name));
        Assert.Equal("Aika, Bea — Creampie, Squirt", chosenItem.DisplayTitle);
        Assert.Equal("Actor is not in the movie's cast.", badActor.ErrorMessage);
        Assert.Equal("Tag not found.", badTag.ErrorMessage);
        Assert.Equal(2, (await service.GetHighlightsAsync(movieId)).Count);
    }

    [Fact]
    public async Task UntitledHighlight_InASoloMovie_LeavesTheActorOutOfItsTitle()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Mei");
        var tagIds = await SeedTagsAsync(factory, "Squirt", "Creampie");
        var service = new MovieHighlightService(factory);

        var tagged = (await service.AddHighlightAsync(movieId, 10, 20, null, tagIds, [cast[0]])).HighlightId!.Value;
        var bare = (await service.AddHighlightAsync(movieId, 30, 40, null)).HighlightId!.Value;

        // Her name on every highlight says nothing on her own movie.
        Assert.Equal("Creampie, Squirt", (await HighlightAsync(service, movieId, tagged)).DisplayTitle);
        Assert.Equal("Highlight 2", (await HighlightAsync(service, movieId, bare)).DisplayTitle);
    }

    [Fact]
    public async Task UpdateHighlight_ReplacesActorsAndTagsWhenGiven_AndInheritanceFollowsAMove()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        var tagIds = await SeedTagsAsync(factory, "A", "B");
        await SeedSceneAsync(factory, movieId, 0, 300, cast[0]);
        await SeedSceneAsync(factory, movieId, 300, 600, cast[1]);
        var service = new MovieHighlightService(factory);
        var id = (await service.AddHighlightAsync(movieId, 100, 120, "Named", [tagIds[0]])).HighlightId!.Value;

        Assert.True((await service.UpdateHighlightAsync(id, 400, 420, "Named")).Success);
        var moved = await HighlightAsync(service, movieId, id);
        Assert.Equal([cast[1]], moved.EffectiveActors.Actors.Select(a => a.ActorId));
        Assert.Equal([tagIds[0]], moved.Tags.Select(t => t.TagId));
        Assert.Equal("Named", moved.DisplayTitle);

        Assert.True((await service.UpdateHighlightAsync(id, 400, 420, null, [tagIds[1]], [cast[1]])).Success);
        var edited = await HighlightAsync(service, movieId, id);
        Assert.Equal([cast[1]], edited.Actors.Select(a => a.ActorId));
        Assert.Equal([tagIds[1]], edited.Tags.Select(t => t.TagId));
        Assert.Equal("Bea — B", edited.DisplayTitle);

        // An empty choice clears it back to inheriting.
        Assert.True((await service.UpdateHighlightAsync(id, 100, 120, null, [tagIds[1]], [])).Success);
        var cleared = await HighlightAsync(service, movieId, id);
        Assert.Empty(cleared.Actors);
        Assert.Equal([cast[0]], cleared.EffectiveActors.Actors.Select(a => a.ActorId));
    }

    [Fact]
    public async Task LeavingTheCast_OrDeletingTheHighlight_RemovesItsLinks_ButNotTheTag()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var cast = await SeedCastAsync(factory, movieId, "Aika", "Bea");
        var tagIds = await SeedTagsAsync(factory, "A");
        var service = new MovieHighlightService(factory);
        var id = (await service.AddHighlightAsync(movieId, 100, 120, null, tagIds, cast)).HighlightId!.Value;

        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.MovieActors.Where(ma => ma.MovieId == movieId && ma.ActorId == cast[0]).ExecuteDeleteAsync();
        }
        Assert.Equal([cast[1]], (await HighlightAsync(service, movieId, id)).Actors.Select(a => a.ActorId));

        await service.DeleteHighlightAsync(id);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Empty(await db.HighlightActors.ToListAsync());
            Assert.Empty(await db.HighlightTags.ToListAsync());
            Assert.Single(await db.Tags.ToListAsync());
        }
    }

    [Fact]
    public async Task AddHighlight_LeavesTheScenesAlone_AndTagsTheMovieThroughItsClips()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "Creampie", "Squirt");
        var first = await SeedSceneAsync(factory, movieId, 0, 300);
        var second = await SeedSceneAsync(factory, movieId, 300, null);
        var service = ServiceWithClipTags(factory);

        var result = await service.AddHighlightAsync(movieId, 250, 350, null, tagIds);

        // Rolled up, not copied.
        Assert.True(result.MovieTagsChanged);
        Assert.Empty(await SceneTagIdsAsync(factory, first));
        Assert.Empty(await SceneTagIdsAsync(factory, second));
        Assert.Equal(tagIds.Select(id => (id, false, true)), await MovieTagsAsync(factory, movieId));
        Assert.False((await service.AddHighlightAsync(movieId, 260, 270, null, tagIds)).MovieTagsChanged);
    }

    [Fact]
    public async Task RemovingAHighlightTagOrTheHighlight_DropsTheClipOnlyMovieTag()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedMovieAsync(factory);
        var tagIds = await SeedTagsAsync(factory, "A", "B");
        var service = ServiceWithClipTags(factory);
        var id = (await service.AddHighlightAsync(movieId, 100, 120, null, tagIds)).HighlightId!.Value;

        Assert.True((await service.UpdateHighlightAsync(id, 400, 420, null, [tagIds[1]])).MovieTagsChanged);
        Assert.Equal([(tagIds[1], false, true)], await MovieTagsAsync(factory, movieId));
        Assert.False((await service.UpdateHighlightAsync(id, 400, 430, null, [tagIds[1]])).MovieTagsChanged);

        await service.DeleteHighlightAsync(id);
        Assert.Empty(await MovieTagsAsync(factory, movieId));
    }
}
