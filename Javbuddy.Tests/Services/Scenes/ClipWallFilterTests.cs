using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The Scenes wall matches, offers and shows rolled-up tags and inherited actors.</summary>
public sealed class ClipWallFilterTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ISceneWallQueryService wall;
    private int movieId, aika, bea, cora, squirt, play, rough, scene1, scene2, crossing, outside, inheritingApex, narrowedApex;

    public ClipWallFilterTests() => wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory)));

    public void Dispose() => factory.Dispose();

    /// <summary>ABC-561, cast Aika (cup G), Bea, Cora. Scene 1 [0,100) Aika+Bea, Scene 2 [100,200) Cora.
    /// Highlight "Crossing" [90,110) tagged Squirt, no actors; highlight "Outside" [300,400) no actors.
    /// Apex @50 tagged Play›Rough, no actors (inherits Aika+Bea); apex @20 narrowed to Bea.</summary>
    private async Task SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-561", MetaTitle = "Roll-up", MetaStudio = "S1", MediaDurationSeconds = 1000 };
        var actors = new[] { new Actor { FirstName = "Aika", CupSize = "G" }, new Actor { FirstName = "Bea" }, new Actor { FirstName = "Cora" } };
        var playTag = new Tag { Name = "Play" };
        var roughTag = new Tag { Name = "Rough", ParentTag = playTag };
        var squirtTag = new Tag { Name = "Squirt" };
        db.AddRange(movie, playTag, roughTag, squirtTag);
        db.Actors.AddRange(actors);
        await db.SaveChangesAsync();
        (movieId, aika, bea, cora, squirt, play, rough) = (movie.Id, actors[0].Id, actors[1].Id, actors[2].Id, squirtTag.Id, playTag.Id, roughTag.Id);
        db.MovieActors.AddRange(actors.Select(a => new MovieActor { MovieId = movieId, ActorId = a.Id }));
        await db.SaveChangesAsync();

        var s1 = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 100 };
        s1.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = aika });
        s1.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = bea });
        var s2 = new Scene { MovieId = movieId, StartSeconds = 100, EndSeconds = 200 };
        s2.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = cora });
        var h1 = new MovieHighlight { MovieId = movieId, StartSeconds = 90, EndSeconds = 110, Title = "Crossing" };
        h1.HighlightTags.Add(new HighlightTag { TagId = squirt });
        var h2 = new MovieHighlight { MovieId = movieId, StartSeconds = 300, EndSeconds = 400, Title = "Outside" };
        var a1 = new MovieApex { MovieId = movieId, Seconds = 50 };
        a1.ApexTags.Add(new ApexTag { TagId = rough });
        var a2 = new MovieApex { MovieId = movieId, Seconds = 20 };
        a2.ApexActors.Add(new ApexActor { MovieId = movieId, ActorId = bea });
        db.AddRange(s1, s2, h1, h2, a1, a2);
        await db.SaveChangesAsync();
        (scene1, scene2, crossing, outside, inheritingApex, narrowedApex) = (s1.Id, s2.Id, h1.Id, h2.Id, a1.Id, a2.Id);
    }

    private async Task<int[]> SceneIdsAsync(SceneWallFilter filter) =>
        (await wall.GetPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.SceneId).Order().ToArray();

    private async Task<int[]> HighlightIdsAsync(SceneWallFilter filter) =>
        (await wall.GetHighlightPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.HighlightId).Order().ToArray();

    private async Task<int[]> ApexIdsAsync(SceneWallFilter filter) =>
        (await wall.GetApexPageAsync(filter, SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Select(c => c.ApexId).Order().ToArray();

    [Fact]
    public async Task SceneView_TagFilter_MatchesTagsRolledUpFromApexesAndHighlights()
    {
        await SeedAsync();

        // The apex's tag is only on scene 1; the crossing highlight's reaches both scenes.
        Assert.Equal([scene1], await SceneIdsAsync(new SceneWallFilter(TagIds: [rough])));
        Assert.Equal([scene1, scene2], await SceneIdsAsync(new SceneWallFilter(TagIds: [squirt])));
        // A parent tag matches its subtag carried by an apex.
        Assert.Equal([scene1], await SceneIdsAsync(new SceneWallFilter(TagIds: [play])));
    }

    [Fact]
    public async Task HighlightView_TagFilter_MatchesTagsOfApexesInside()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieApexes.Add(new MovieApex { MovieId = movieId, Seconds = 350, ApexTags = { new ApexTag { TagId = rough } } });
            await db.SaveChangesAsync();
        }

        Assert.Equal([outside], await HighlightIdsAsync(new SceneWallFilter(TagIds: [rough])));
        Assert.Equal([crossing], await HighlightIdsAsync(new SceneWallFilter(TagIds: [squirt])));
    }

    [Fact]
    public async Task ActorFilters_MatchInheritedActors()
    {
        await SeedAsync();

        // The crossing highlight inherits scene 1's actors; the outside one the whole cast.
        Assert.Equal([crossing, outside], await HighlightIdsAsync(new SceneWallFilter(ActorIds: [aika])));
        Assert.Equal([outside], await HighlightIdsAsync(new SceneWallFilter(ActorIds: [cora])));
        // The actor-less apex inherits scene 1's; the narrowed one only has Bea.
        Assert.Equal([inheritingApex], await ApexIdsAsync(new SceneWallFilter(ActorIds: [aika])));
        Assert.Equal([inheritingApex, narrowedApex], await ApexIdsAsync(new SceneWallFilter(ActorIds: [bea])));
        Assert.Empty(await ApexIdsAsync(new SceneWallFilter(ActorIds: [cora])));
    }

    [Fact]
    public async Task SceneWithoutActors_InheritsTheCast_AndPassesItOn()
    {
        await SeedAsync();
        int bare, inBare;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // No actors of its own: it inherits the cast.
            var scene = new Scene { MovieId = movieId, StartSeconds = 500, EndSeconds = 600 };
            var apex = new MovieApex { MovieId = movieId, Seconds = 550 };
            db.AddRange(scene, apex);
            await db.SaveChangesAsync();
            (bare, inBare) = (scene.Id, apex.Id);
        }

        Assert.Equal([scene2, bare], await SceneIdsAsync(new SceneWallFilter(ActorIds: [cora])));
        var card = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single(c => c.SceneId == bare);
        Assert.Equal(["Aika", "Bea", "Cora"], card.Actors);
        Assert.Contains(inBare, await ApexIdsAsync(new SceneWallFilter(ActorIds: [cora])));
    }

    [Fact]
    public async Task ActressAttributeFilters_MatchInheritedActors()
    {
        await SeedAsync();
        var gCup = new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["G"] });

        Assert.Equal([inheritingApex], await ApexIdsAsync(gCup));
        Assert.Equal([crossing, outside], await HighlightIdsAsync(gCup));
    }

    [Fact]
    public async Task Options_OfferRolledUpTagsAndInheritedActors()
    {
        await SeedAsync();

        var sceneOptions = await wall.GetOptionsAsync();
        Assert.Contains(sceneOptions.Tags, t => t.Id == rough);
        Assert.Contains(sceneOptions.Tags, t => t.Id == squirt);

        var highlightOptions = await wall.GetHighlightOptionsAsync();
        Assert.Equal(["Aika", "Bea", "Cora"], highlightOptions.Actors.Select(a => a.Name));
        Assert.Contains(highlightOptions.ActorAttributes.CupSizes, c => c == "G");

        var apexOptions = await wall.GetApexOptionsAsync();
        Assert.Equal(["Aika", "Bea"], apexOptions.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task Cards_ShowInheritedActors()
    {
        await SeedAsync();

        var highlights = (await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        Assert.Equal(["Aika", "Bea"], highlights.Single(c => c.HighlightId == crossing).Actors);
        var apexCards = (await wall.GetApexPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        Assert.Equal(["Aika", "Bea"], apexCards.Single(c => c.ApexId == inheritingApex).Actors);
        Assert.Equal("Aika, Bea — Rough", apexCards.Single(c => c.ApexId == inheritingApex).DisplayTitle);
    }

    [Fact]
    public async Task SceneCards_ShowRolledUpTags_WithTheirSources()
    {
        await SeedAsync();

        var cards = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        // Apex @50 is apex 2 (the narrowed one @20 is apex 1); the crossing highlight is highlight 1.
        var first = cards.Single(c => c.SceneId == scene1);
        Assert.Empty(first.Tags);
        Assert.Equal([(rough, "apex 2"), (squirt, "highlight 1")],
            first.ImplicitTags.Select(t => (t.Tag.TagId, string.Join(", ", t.Sources))));
        Assert.Equal("Play", first.ImplicitTags[0].Tag.ParentName);
        Assert.Equal([squirt], cards.Single(c => c.SceneId == scene2).ImplicitTags.Select(t => t.Tag.TagId));
    }

    [Fact]
    public async Task SceneCards_DoNotRepeatAnOwnTagAsRolledUp()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SceneTags.Add(new SceneTag { SceneId = scene1, TagId = squirt });
            await db.SaveChangesAsync();
        }

        var card = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single(c => c.SceneId == scene1);
        Assert.Equal([squirt], card.Tags.Select(t => t.TagId));
        Assert.Equal([rough], card.ImplicitTags.Select(t => t.Tag.TagId));
    }

    [Fact]
    public async Task HighlightCards_ShowTagsRolledUpFromTheirApexes()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieApexes.Add(new MovieApex { MovieId = movieId, Seconds = 350, ApexTags = { new ApexTag { TagId = rough } } });
            await db.SaveChangesAsync();
        }

        var cards = (await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        var outsideCard = cards.Single(c => c.HighlightId == outside);
        Assert.Empty(outsideCard.Tags);
        var tag = Assert.Single(outsideCard.ImplicitTags);
        Assert.Equal((rough, "apex 3"), (tag.Tag.TagId, string.Join(", ", tag.Sources)));
        Assert.Empty(cards.Single(c => c.HighlightId == crossing).ImplicitTags);
    }
}
