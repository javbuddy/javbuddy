using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The wall reads the stored effective actors and follows edits through the stale flag.</summary>
public sealed class StoredClipActorsWallTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ISceneWallQueryService wall;
    private int movieId, aikaId, rinId, sceneId, hiddenSceneId, highlightId, apexId;

    public StoredClipActorsWallTests() => wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory)));

    public void Dispose() => factory.Dispose();

    /// <summary>Cast Aika (cup E), Rin (cup B). Scene 0–600 own Aika; hidden scene 600– own Rin; highlight
    /// 10–100 inheriting the scene (Aika); apex 50 inheriting the highlight (Aika).</summary>
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var aika = new Actor { FirstName = "Aika", CupSize = "E" };
        var rin = new Actor { FirstName = "Rin", CupSize = "B" };
        var movie = new Movie { Code = "WALL-001", MediaDurationSeconds = 3600, LocalFileSizeBytes = 1 };
        db.AddRange(aika, rin, movie);
        await db.SaveChangesAsync();
        (movieId, aikaId, rinId) = (movie.Id, aika.Id, rin.Id);
        db.MovieActors.AddRange(new MovieActor { MovieId = movieId, ActorId = aikaId }, new MovieActor { MovieId = movieId, ActorId = rinId });
        var scene = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 600 };
        var hidden = new Scene { MovieId = movieId, StartSeconds = 600, IsHiddenFromOverview = true };
        var highlight = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 100 };
        var apex = new MovieApex { MovieId = movieId, Seconds = 50 };
        db.AddRange(scene, hidden, highlight, apex);
        await db.SaveChangesAsync();
        (sceneId, hiddenSceneId, highlightId, apexId) = (scene.Id, hidden.Id, highlight.Id, apex.Id);
        db.SceneActors.AddRange(
            new SceneActor { SceneId = sceneId, MovieId = movieId, ActorId = aikaId },
            new SceneActor { SceneId = hiddenSceneId, MovieId = movieId, ActorId = rinId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ApexActorFilter_FollowsParentHighlightActorChange()
    {
        await SeedAsync();
        var byRin = new SceneWallFilter(ActorIds: [rinId]);
        Assert.Empty((await wall.GetApexPageAsync(byRin, SceneWallSort.ReleaseDate, 0, 0, 48)).Cards);

        var result = await new MovieHighlightService(factory).UpdateHighlightAsync(highlightId, 10, 100, null, actorIds: [rinId]);
        Assert.True(result.Success);

        var page = await wall.GetApexPageAsync(byRin, SceneWallSort.ReleaseDate, 0, 0, 48);
        Assert.Equal([apexId], page.Cards.Select(c => c.ApexId));
    }

    [Fact]
    public async Task SceneAttributeOptions_IgnoreHiddenScenesUnlessIncluded()
    {
        await SeedAsync();

        var shown = await wall.GetOptionsAsync(includeHidden: false);
        var all = await wall.GetOptionsAsync(includeHidden: true);

        Assert.Equal(["E"], shown.ActorAttributes.CupSizes);
        Assert.Equal(["B", "E"], all.ActorAttributes.CupSizes);
        Assert.Equal(["Aika"], shown.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task ApexAttributeFilter_MatchesStoredInheritedActress()
    {
        await SeedAsync();
        var cupE = new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"] });
        var cupB = new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["B"] });

        Assert.Equal([apexId], (await wall.GetApexPageAsync(cupE, SceneWallSort.ReleaseDate, 0, 0, 48)).Cards.Select(c => c.ApexId));
        Assert.Empty((await wall.GetApexPageAsync(cupB, SceneWallSort.ReleaseDate, 0, 0, 48)).Cards);
    }
}
