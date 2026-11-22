using Javbuddy.Models;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

public sealed class ClipAssignmentsTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();

    public void Dispose() => factory.Dispose();

    private sealed record Seed(int MovieId, int EmptyMovieId, int YuaId, int AikaId, int MeiId, int TagId, int Scene1Id, int Scene2Id, int HighlightId, int ApexInHighlightId, int ApexInSceneId, int ApexInCastId);

    /// <summary>Cast Yua and Aika; scene 1 (0-1200) has Yua, scene 2 (1200-end) has none; one highlight
    /// (100-400) has Aika with a tag; apexes at 150 (in the highlight), 50 (in scene 1) and 1500 (in scene 2).</summary>
    private async Task<Seed> SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var yua = new Actor { FirstName = "Yua" };
        var aika = new Actor { FirstName = "Aika" };
        var mei = new Actor { FirstName = "Mei" };
        var tag = new Tag { Name = "Alpha" };
        var movie = new Movie { Code = "AAA-001", MediaDurationSeconds = 3600 };
        var empty = new Movie { Code = "BBB-002", MediaDurationSeconds = 600 };
        db.AddRange(yua, aika, mei, tag, movie, empty);
        await db.SaveChangesAsync();
        db.MovieActors.AddRange(
            new MovieActor { MovieId = movie.Id, ActorId = yua.Id },
            new MovieActor { MovieId = movie.Id, ActorId = aika.Id },
            new MovieActor { MovieId = empty.Id, ActorId = mei.Id });
        var scene1 = new Scene { MovieId = movie.Id, StartSeconds = 0, EndSeconds = 1200 };
        var scene2 = new Scene { MovieId = movie.Id, StartSeconds = 1200 };
        var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = 100, EndSeconds = 400 };
        var apexHighlight = new MovieApex { MovieId = movie.Id, Seconds = 150 };
        var apexScene = new MovieApex { MovieId = movie.Id, Seconds = 50 };
        var apexCast = new MovieApex { MovieId = movie.Id, Seconds = 1500 };
        db.AddRange(scene1, scene2, highlight, apexHighlight, apexScene, apexCast);
        await db.SaveChangesAsync();
        db.SceneActors.Add(new SceneActor { SceneId = scene1.Id, MovieId = movie.Id, ActorId = yua.Id });
        db.HighlightActors.Add(new HighlightActor { HighlightId = highlight.Id, MovieId = movie.Id, ActorId = aika.Id });
        db.HighlightActorTags.Add(new HighlightActorTag { HighlightId = highlight.Id, MovieId = movie.Id, ActorId = aika.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        return new Seed(movie.Id, empty.Id, yua.Id, aika.Id, mei.Id, tag.Id, scene1.Id, scene2.Id, highlight.Id, apexHighlight.Id, apexScene.Id, apexCast.Id);
    }

    [Fact]
    public async Task LoadEffectiveActorsAsync_AppliesOwnThenParentThenCastInheritance()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var result = await ClipAssignments.LoadEffectiveActorsAsync(db, seed.MovieId, CancellationToken.None);

        Assert.Equal(ActorSource.Explicit, result.Scenes[seed.Scene1Id].Source);
        Assert.Equal([seed.YuaId], result.Scenes[seed.Scene1Id].Actors.Select(a => a.ActorId));
        Assert.Equal(ActorSource.Cast, result.Scenes[seed.Scene2Id].Source);
        Assert.Equal(new[] { seed.AikaId, seed.YuaId }.Order(), result.Scenes[seed.Scene2Id].Actors.Select(a => a.ActorId).Order());
        Assert.Equal([seed.AikaId], result.Highlights[seed.HighlightId].Actors.Select(a => a.ActorId));
        Assert.Equal(ActorSource.Highlight, result.Apexes[seed.ApexInHighlightId].Source);
        Assert.Equal("highlight 1", result.Apexes[seed.ApexInHighlightId].From);
        Assert.Equal(ActorSource.Scene, result.Apexes[seed.ApexInSceneId].Source);
        Assert.Equal([seed.YuaId], result.Apexes[seed.ApexInSceneId].Actors.Select(a => a.ActorId));
        Assert.Equal(ActorSource.Cast, result.Apexes[seed.ApexInCastId].Source);
    }

    [Fact]
    public async Task LoadEffectiveActorsAsync_ManyMovies_ReturnsAnEntryForEachIncludingOnesWithoutClips()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var result = await ClipAssignments.LoadEffectiveActorsAsync(db, [seed.MovieId, seed.EmptyMovieId, seed.EmptyMovieId], CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[seed.MovieId].Apexes.Count);
        Assert.Empty(result[seed.EmptyMovieId].Scenes);
        Assert.Empty(result[seed.EmptyMovieId].Highlights);
        Assert.Empty(result[seed.EmptyMovieId].Apexes);
    }

    [Fact]
    public async Task LoadEffectiveActorTagsAsync_ReturnsTheHighlightsOwnActorTag()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var tags = await ClipAssignments.LoadEffectiveActorTagsAsync(db, seed.MovieId, CancellationToken.None);

        var own = Assert.Single(tags.Highlights[seed.HighlightId]);
        Assert.Equal(seed.AikaId, own.ActorId);
        Assert.Equal(seed.TagId, own.TagId);
        Assert.False(own.IsRolledUp);
        // The highlight starts in scene 1, so its tag also rolls up to that scene.
        var rolledUp = Assert.Single(tags.Scenes[seed.Scene1Id]);
        Assert.True(rolledUp.IsRolledUp);
        Assert.Equal((seed.AikaId, seed.TagId), (rolledUp.ActorId, rolledUp.TagId));
    }

    [Fact]
    public async Task LoadEffectiveActorsAndTagsAsync_MatchesTheSeparateLoaders()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var (actors, tags) = await ClipAssignments.LoadEffectiveActorsAndTagsAsync(db, seed.MovieId, CancellationToken.None);

        var separateActors = await ClipAssignments.LoadEffectiveActorsAsync(db, seed.MovieId, CancellationToken.None);
        var separateTags = await ClipAssignments.LoadEffectiveActorTagsAsync(db, seed.MovieId, CancellationToken.None);
        Assert.Equal(separateActors.Highlights[seed.HighlightId].Actors, actors.Highlights[seed.HighlightId].Actors);
        Assert.Equal(separateActors.Apexes[seed.ApexInHighlightId].From, actors.Apexes[seed.ApexInHighlightId].From);
        Assert.Equal(separateTags.Highlights[seed.HighlightId], tags.Highlights[seed.HighlightId]);
    }

    [Fact]
    public async Task LoadActorOptionsAsync_ListsTheCastSortedByName()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        var options = await ClipAssignments.LoadActorOptionsAsync(db, seed.MovieId, CancellationToken.None);

        Assert.Equal([seed.AikaId, seed.YuaId], options.Cast.Select(a => a.ActorId));
    }

    [Fact]
    public async Task ValidateActorsAsync_OnlyAcceptsActorsInTheMoviesCast()
    {
        var seed = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();

        Assert.Null(await ClipAssignments.ValidateActorsAsync(db, seed.MovieId, [seed.YuaId, seed.AikaId], CancellationToken.None));
        Assert.Equal("Actor is not in the movie's cast.", await ClipAssignments.ValidateActorsAsync(db, seed.MovieId, [seed.YuaId, seed.MeiId], CancellationToken.None));
    }

    [Fact]
    public async Task LoadClipMediaVersionsAsync_ReturnsTheRowsTicksOnlyWhenTheWindowMatches()
    {
        var updated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.AddRange(
                new CachedImage { Code = "AAA-001", Role = "scene", Index = 4, Variant = SceneMediaService.VariantThumb, StorageId = Guid.NewGuid(), SceneStartMs = 1000, SceneEndMs = 2000, UpdatedAt = updated },
                new CachedImage { Code = "AAA-001", Role = "highlight", Index = 4, Variant = SceneMediaService.VariantThumb, StorageId = Guid.NewGuid(), SceneStartMs = 1000, SceneEndMs = 2000, UpdatedAt = updated.AddDays(1) });
            await db.SaveChangesAsync();
        }
        await using var query = await factory.CreateDbContextAsync();

        var versions = await ClipAssignments.LoadClipMediaVersionsAsync(query, "scene", [4], null, null, CancellationToken.None);

        Assert.Equal(updated.Ticks, versions.Of(4, SceneMediaService.VariantThumb, new SceneMediaWindow(1000, 2000)));
        Assert.Null(versions.Of(4, SceneMediaService.VariantThumb, new SceneMediaWindow(1000, 3000)));
        Assert.Null(versions.Of(4, SceneMediaService.VariantPreview, new SceneMediaWindow(1000, 2000)));
        Assert.Null(versions.Of(5, SceneMediaService.VariantThumb, new SceneMediaWindow(1000, 2000)));
    }

    [Fact]
    public async Task LoadClipMediaVersionsAsync_HonoursTheServesAndCurrentCallbacks()
    {
        var updated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Add(new CachedImage { Code = "AAA-001", Role = "scene", Index = 4, Variant = SceneMediaService.VariantThumb, StorageId = Guid.NewGuid(), SceneStartMs = 1, SceneEndMs = 2, UpdatedAt = updated });
            await db.SaveChangesAsync();
        }
        await using var query = await factory.CreateDbContextAsync();
        var window = new SceneMediaWindow(1, 2);

        var notServed = await ClipAssignments.LoadClipMediaVersionsAsync(query, "scene", [4], _ => false, null, CancellationToken.None);
        var notCurrent = await ClipAssignments.LoadClipMediaVersionsAsync(query, "scene", [4], _ => true, (_, _) => false, CancellationToken.None);
        var current = await ClipAssignments.LoadClipMediaVersionsAsync(query, "scene", [4], _ => true, (_, _) => true, CancellationToken.None);

        Assert.Null(notServed.Of(4, SceneMediaService.VariantThumb, window));
        Assert.Null(notCurrent.Of(4, SceneMediaService.VariantThumb, window));
        Assert.Equal(updated.Ticks, current.Of(4, SceneMediaService.VariantThumb, window));
    }

    [Fact]
    public async Task LoadClipMediaVersionsAsync_NoClipIds_LoadsNothing()
    {
        await using var db = await factory.CreateDbContextAsync();

        var versions = await ClipAssignments.LoadClipMediaVersionsAsync(db, "scene", [], null, null, CancellationToken.None);

        Assert.Null(versions.Of(1, SceneMediaService.VariantThumb, new SceneMediaWindow(0, 1)));
    }
}
