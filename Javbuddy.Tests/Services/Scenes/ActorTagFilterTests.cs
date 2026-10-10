using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The "actor + actor tag" filter on the Scenes wall (all three views) and the Movies grid.</summary>
public sealed class ActorTagFilterTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ISceneWallQueryService wall;
    private int movieId, otherMovieId, aika, bea, blonde, brunette, scene, highlight, apex;

    public ActorTagFilterTests() => wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory)));

    public void Dispose() => factory.Dispose();

    /// <summary>ATF-001, cast Aika and Bea, both inheriting everywhere: scene [0,100), highlight [10,90), apex @50. The movie says Bea is
    /// blonde; the apex says Aika is brunette. A second movie has nothing.</summary>
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ATF-001", MetaTitle = "Tags", MediaDurationSeconds = 1000 };
        var other = new Movie { Code = "ATF-002" };
        var actors = new[] { new Actor { FirstName = "Aika" }, new Actor { FirstName = "Bea" } };
        var blondeTag = new Tag { Name = "Blonde", IsActorTag = true };
        var brunetteTag = new Tag { Name = "Brunette", IsActorTag = true };
        db.AddRange(movie, other, blondeTag, brunetteTag);
        db.Actors.AddRange(actors);
        await db.SaveChangesAsync();
        (movieId, otherMovieId, aika, bea, blonde, brunette) = (movie.Id, other.Id, actors[0].Id, actors[1].Id, blondeTag.Id, brunetteTag.Id);
        db.MovieActors.AddRange(actors.Select(a => new MovieActor { MovieId = movieId, ActorId = a.Id }));
        var s = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 100 };
        var h = new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 90 };
        var a = new MovieApex { MovieId = movieId, Seconds = 50 };
        db.AddRange(s, h, a);
        await db.SaveChangesAsync();
        (scene, highlight, apex) = (s.Id, h.Id, a.Id);
        db.MovieActorTags.Add(new MovieActorTag { MovieId = movieId, ActorId = bea, TagId = blonde });
        db.ApexActorTags.Add(new ApexActorTag { ApexId = apex, MovieId = movieId, ActorId = aika, TagId = brunette });
        await db.SaveChangesAsync();
    }

    private async Task<int[]> SceneIdsAsync(SceneWallFilter filter) =>
        (await wall.GetPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.SceneId).Order().ToArray();

    private async Task<int[]> HighlightIdsAsync(SceneWallFilter filter) =>
        (await wall.GetHighlightPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.HighlightId).Order().ToArray();

    private async Task<int[]> ApexIdsAsync(SceneWallFilter filter) =>
        (await wall.GetApexPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.ApexId).Order().ToArray();

    [Fact]
    public async Task WithoutActors_AnyActorWithTheTagMatches_AtEveryLevel()
    {
        await SeedAsync();

        // Brunette sits on the apex and rolls up to the highlight and scene around it.
        Assert.Equal([scene], await SceneIdsAsync(new SceneWallFilter(ActorTagIds: [brunette])));
        Assert.Equal([highlight], await HighlightIdsAsync(new SceneWallFilter(ActorTagIds: [brunette])));
        Assert.Equal([apex], await ApexIdsAsync(new SceneWallFilter(ActorTagIds: [brunette])));
    }

    [Fact]
    public async Task WithActors_TheSameActorMustHaveTheTag()
    {
        await SeedAsync();

        Assert.Equal([scene], await SceneIdsAsync(new SceneWallFilter(ActorIds: [aika], ActorTagIds: [brunette])));
        Assert.Empty(await SceneIdsAsync(new SceneWallFilter(ActorIds: [bea], ActorTagIds: [brunette])));
        Assert.Equal([apex], await ApexIdsAsync(new SceneWallFilter(ActorIds: [bea], ActorTagIds: [blonde])));
    }

    [Fact]
    public async Task AnActorsOwnTagsReplaceTheInheritedOnes()
    {
        await SeedAsync();

        // Bea inherits the movie's Blonde down to the apex; Aika has her own Brunette there, so she is not blonde.
        Assert.Equal([highlight], await HighlightIdsAsync(new SceneWallFilter(ActorIds: [bea], ActorTagIds: [blonde])));
        Assert.Empty(await ApexIdsAsync(new SceneWallFilter(ActorIds: [aika], ActorTagIds: [blonde])));
    }

    [Fact]
    public async Task MoviesGrid_MatchesTheMovieOrAnyOfItsClips_AndTheActor()
    {
        await SeedAsync();
        var grid = new MovieGridQueryService(factory);

        Assert.Equal(1, await grid.CountAsync(new MovieGridFilter(ActorTagIds: [brunette])));
        Assert.Equal(1, await grid.CountAsync(new MovieGridFilter(ActorTagIds: [blonde])));
        Assert.Equal(1, await grid.CountAsync(new MovieGridFilter(ActorIds: [aika], ActorTagIds: [brunette])));
        Assert.Equal(0, await grid.CountAsync(new MovieGridFilter(ActorIds: [bea], ActorTagIds: [brunette])));
        Assert.Equal(["ATF-001"], (await grid.GetRangeAsync(new MovieGridFilter(ActorTagIds: [blonde, brunette]), new MovieGridSort("title", false, 1), 0, 10)).Select(m => m.Code));
        Assert.NotEqual(movieId, otherMovieId);
    }

    [Fact]
    public async Task Cards_LabelEachActorWithTheirTags_ButNotRolledUpOnes()
    {
        await SeedAsync();

        var apexCard = Assert.Single((await wall.GetApexPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards);
        Assert.Equal(["Aika (Brunette)", "Bea (Blonde)"], apexCard.ActorLabels);

        // The apex's Brunette only rolls up into the scene, so it isn't on Aika's label there.
        var sceneCard = Assert.Single((await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards);
        Assert.Equal(["Aika", "Bea (Blonde)"], sceneCard.ActorLabels);
    }

    [Fact]
    public async Task AParentActorTag_MatchesItsSubtags_AndIsOffered()
    {
        await SeedAsync();
        int hair;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var parent = new Tag { Name = "Hair", IsActorTag = true };
            var sub = new Tag { Name = "Long", IsActorTag = true, ParentTag = parent };
            db.AddRange(parent, sub);
            await db.SaveChangesAsync();
            db.SceneActorTags.Add(new SceneActorTag { SceneId = scene, MovieId = movieId, ActorId = aika, TagId = sub.Id });
            await db.SaveChangesAsync();
            hair = parent.Id;
        }

        Assert.Equal([scene], await SceneIdsAsync(new SceneWallFilter(ActorIds: [aika], ActorTagIds: [hair])));
        Assert.Equal(1, await new MovieGridQueryService(factory).CountAsync(new MovieGridFilter(ActorTagIds: [hair])));
        Assert.Contains("Hair", (await wall.GetOptionsAsync()).ActorTags.Select(t => t.Name));
        Assert.Contains("Hair › Long", (await wall.GetOptionsAsync()).ActorTags.Select(t => t.Name));
        var grid = await new MovieGridQueryService(factory).GetSummaryAsync(new MovieGridFilter());
        Assert.Contains("Hair › Long", grid.ActorTags.Select(t => t.Name));
    }

    [Fact]
    public async Task FilterOptions_GroupEachSubtagUnderItsOwnParent_EvenWhenAnotherNameStartsWithTheParents()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var hair = new Tag { Name = "Hair", IsActorTag = true };
            var colour = new Tag { Name = "Hair colour", IsActorTag = true };
            var shortHair = new Tag { Name = "Short", IsActorTag = true, ParentTag = hair };
            var black = new Tag { Name = "Black", IsActorTag = true, ParentTag = colour };
            db.AddRange(hair, colour, shortHair, black);
            await db.SaveChangesAsync();
            db.MovieActorTags.AddRange(
                new MovieActorTag { MovieId = movieId, ActorId = aika, TagId = shortHair.Id },
                new MovieActorTag { MovieId = movieId, ActorId = aika, TagId = black.Id });
            db.SceneActorTags.AddRange(
                new SceneActorTag { SceneId = scene, MovieId = movieId, ActorId = aika, TagId = shortHair.Id },
                new SceneActorTag { SceneId = scene, MovieId = movieId, ActorId = aika, TagId = black.Id });
            await db.SaveChangesAsync();
        }
        string[] expected = ["Blonde", "Brunette", "Hair", "Hair › Short", "Hair colour", "Hair colour › Black"];

        var grid = await new MovieGridQueryService(factory).GetSummaryAsync(new MovieGridFilter());
        Assert.Equal(expected, grid.ActorTags.Select(t => t.Name));
        Assert.Equal(expected, (await wall.GetOptionsAsync()).ActorTags.Select(t => t.Name));
    }
}
