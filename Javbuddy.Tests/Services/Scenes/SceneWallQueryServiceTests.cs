using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

public sealed class SceneWallQueryServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly MovieSceneService scenes;
    private readonly ISceneWallQueryService wall;
    private int playId, roughId, bathId, yuaId, aikaId, rinId;
    private int oldMovieId, newMovieId;

    public SceneWallQueryServiceTests()
    {
        scenes = new MovieSceneService(factory);
        wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, scenes));
    }

    public void Dispose() => factory.Dispose();

    /// <summary>OLD-001 (2019, S1, added first; cast Aika, Rin): Intro 0–600 [Bath, Aika, fav, 5★], 600– [Play›Rough, Rin].
    /// NEW-001 (2024, Moodyz, added last): 0– [Yua, 3★], inheriting its cast. Rin has no
    /// measurements, so Scene 2 never matches an attribute filter.</summary>
    private async Task SeedAsync()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var play = new Tag { Name = "Play" };
            var rough = new Tag { Name = "Rough", ParentTag = play };
            var bath = new Tag { Name = "Bath" };
            var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var aika = new Actor { FirstName = "Aika" };
            var rin = new Actor { FirstName = "Rin" };
            var old = new Movie { Code = "OLD-001", MetaTitle = "Old one", MetaStudio = "S1", MetaReleaseDate = new DateTime(2019, 1, 1), FileAddedAt = new DateTime(2020, 1, 1), MediaDurationSeconds = 3600 };
            var recent = new Movie { Code = "NEW-001", MetaTitle = "New one", MetaStudio = "Moodyz", MetaReleaseDate = new DateTime(2024, 1, 1), FileAddedAt = new DateTime(2025, 1, 1), MediaDurationSeconds = 3600 };
            db.AddRange(play, rough, bath, yua, aika, rin, old, recent);
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(new MovieActor { MovieId = old.Id, ActorId = aika.Id }, new MovieActor { MovieId = old.Id, ActorId = rin.Id }, new MovieActor { MovieId = recent.Id, ActorId = yua.Id });
            await db.SaveChangesAsync();
            (playId, roughId, bathId, yuaId, aikaId, rinId, oldMovieId, newMovieId) = (play.Id, rough.Id, bath.Id, yua.Id, aika.Id, rin.Id, old.Id, recent.Id);
        }

        var intro = (await scenes.AddSceneAsync(oldMovieId, 0, 600, "Intro")).SceneId!.Value;
        await scenes.AddSceneTagAsync(intro, bathId);
        await scenes.AddSceneActorAsync(intro, aikaId);
        await scenes.ToggleFavoriteAsync(intro);
        var second = (await scenes.AddSceneAsync(oldMovieId, 600, null, null)).SceneId!.Value;
        await scenes.AddSceneTagAsync(second, roughId);
        await scenes.AddSceneActorAsync(second, rinId);
        await scenes.AddSceneAsync(newMovieId, 0, null, null);
    }

    private async Task<List<string>> TitlesAsync(SceneWallFilter filter, SceneWallSort sort = SceneWallSort.ReleaseDate) =>
        (await wall.GetPageAsync(filter, sort, 1, 0, 50)).Cards.Select(c => $"{c.Code}/{c.DisplayTitle}").ToList();

    [Fact]
    public async Task Cards_CarryTheResolvedSceneAndMovieDetails()
    {
        await SeedAsync();

        var page = await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50);

        Assert.Equal(3, page.TotalCount);
        var second = page.Cards.Single(c => c.Code == "OLD-001" && c.StartSeconds == 600);
        Assert.Equal("Scene 2", second.DisplayTitle);
        Assert.Equal("Old one", second.MovieTitle);
        Assert.Equal(3600d, second.EffectiveEndSeconds);
        Assert.Equal(new SceneTagItem(roughId, "Rough", "Play"), Assert.Single(second.Tags));
        var intro = page.Cards.Single(c => c.DisplayTitle == "Intro");
        Assert.True(intro.IsFavorite);
        Assert.Equal(["Aika"], intro.Actors);
    }

    [Fact]
    public async Task Cards_CarryTheMoviesPlaybackDetails()
    {
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "JF-001", JellyfinItemId = "jf-1", JellyfinServerId = "srv", MediaDurationSeconds = 1800 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        await scenes.AddSceneAsync(movieId, 0, null, "Only");
        await AddHighlightAsync(movieId, 10, 20, "Clip");

        var scene = Assert.Single((await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards);
        var highlight = Assert.Single((await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards);

        Assert.Equal(("jf-1", "srv", 1800d), (scene.JellyfinItemId, scene.JellyfinServerId, scene.DurationSeconds));
        Assert.Equal(("jf-1", "srv", 1800d), (highlight.JellyfinItemId, highlight.JellyfinServerId, highlight.DurationSeconds));
    }

    [Fact]
    public async Task Filters_TagWithSubtagRollup_Actor_Studio_Favorite()
    {
        await SeedAsync();

        Assert.Equal(["OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(TagIds: [playId])));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(TagIds: [bathId])));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(new SceneWallFilter(ActorIds: [yuaId])));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(new SceneWallFilter(Studios: ["Moodyz"])));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(FavoritesOnly: true)));
        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorIds: [yuaId], Studios: ["S1"])));
    }

    [Fact]
    public async Task Filters_MultiSelectMatchesAnyValue_AndTextMatchesSceneOrMovie()
    {
        await SeedAsync();

        Assert.Equal(["NEW-001/Scene 1", "OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorIds: [yuaId, aikaId])));
        Assert.Equal(3, (await TitlesAsync(new SceneWallFilter(Studios: ["S1", "Moodyz"]))).Count);
        Assert.Equal(["OLD-001/Intro", "OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(TagIds: [bathId, playId])));
        // Groups still combine with AND.
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorIds: [yuaId, aikaId], Studios: ["S1"])));

        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(Text: "intr")));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(new SceneWallFilter(Text: "new-0")));
        Assert.Equal(["OLD-001/Intro", "OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(Text: "OLD ONE")));
        Assert.Empty(await TitlesAsync(new SceneWallFilter(Text: "nothing like this")));
    }

    [Fact]
    public async Task Sorts_ReleaseDate_DateAdded()
    {
        await SeedAsync();

        Assert.Equal(["NEW-001/Scene 1", "OLD-001/Intro", "OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate));
        Assert.Equal(["NEW-001/Scene 1", "OLD-001/Intro", "OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(), SceneWallSort.DateAdded));
    }

    [Fact]
    public async Task RandomSort_IsStableForASeed_AndPagesWithoutOverlap()
    {
        await SeedAsync();

        var all = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.Random, 12345, 0, 50)).Cards.Select(c => c.SceneId).ToList();
        var first = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.Random, 12345, 0, 2)).Cards.Select(c => c.SceneId);
        var rest = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.Random, 12345, 2, 2)).Cards.Select(c => c.SceneId);

        Assert.Equal(all, first.Concat(rest));
        Assert.Equal(3, all.Distinct().Count());
    }

    [Fact]
    public async Task Options_OfferOnlyWhatScenesUse()
    {
        await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "Unused" });
            await db.SaveChangesAsync();
        }

        var options = await wall.GetOptionsAsync();

        Assert.Equal(["Bath", "Play › Rough"], options.Tags.Select(t => t.Name));
        Assert.Equal(["Aika", "Mikami Yua", "Rin"], options.Actors.Select(a => a.Name));
        Assert.Equal(["Moodyz", "S1"], options.Studios);
    }

    [Theory]
    [InlineData("MIH-006 If You Want", "MIH-006", "If You Want")]
    [InlineData("mih-006 - If You Want", "MIH-006", "If You Want")]
    [InlineData("Old one", "OLD-001", "Old one")]
    [InlineData("MIH-006", "MIH-006", "MIH-006")]
    [InlineData(null, "MIH-006", "MIH-006")]
    public void MovieTitleWithoutCode_DropsALeadingCode(string? title, string code, string expected) =>
        Assert.Equal(expected, SceneWallQueryService.MovieTitleWithoutCode(title, code));

    [Fact]
    public async Task HiddenScenes_AreLeftOutOfTheWallAndItsOptions_UnlessIncluded()
    {
        await SeedAsync();
        var intro = (await scenes.GetScenesAsync(oldMovieId)).Single(s => s.Title == "Intro");
        await scenes.SetHiddenFromOverviewAsync(intro.Id, true);

        Assert.Equal(["NEW-001/Scene 1", "OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter()));
        Assert.Equal(2, (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).TotalCount);
        // Intro was the only scene with Bath and Aika.
        var options = await wall.GetOptionsAsync();
        Assert.Equal(["Play › Rough"], options.Tags.Select(t => t.Name));
        Assert.Equal(["Mikami Yua", "Rin"], options.Actors.Select(a => a.Name));
        Assert.Equal(["Moodyz", "S1"], options.Studios);

        Assert.Contains("OLD-001/Intro", await TitlesAsync(new SceneWallFilter(IncludeHidden: true)));
        var withHidden = await wall.GetOptionsAsync(includeHidden: true);
        Assert.Equal(["Bath", "Play › Rough"], withHidden.Tags.Select(t => t.Name));
        Assert.Equal(["Aika", "Mikami Yua", "Rin"], withHidden.Actors.Select(a => a.Name));
    }

    // --- Highlights ---

    /// <summary>Adds a highlight with exactly the given tags and actors (none by default, rather than
    /// the inherited ones).</summary>
    private async Task<int> AddHighlightAsync(int movieId, double start, double end, string? title = null, bool favorite = false, int[]? tags = null, int[]? actors = null)
    {
        var highlights = new MovieHighlightService(factory);
        var id = (await highlights.AddHighlightAsync(movieId, start, end, title, tags ?? [], actors ?? [])).HighlightId!.Value;
        if (favorite) await highlights.ToggleFavoriteAsync(id);
        return id;
    }

    private async Task<List<int>> HighlightIdsAsync(SceneWallFilter filter, SceneWallSort sort = SceneWallSort.ReleaseDate) =>
        (await wall.GetHighlightPageAsync(filter, sort, 1, 0, 50)).Cards.Select(c => c.HighlightId).ToList();

    [Fact]
    public async Task HighlightCards_ShowTheirOwnTags_AndTheirOwnOrInheritedActors()
    {
        await SeedAsync();
        var tagged = await AddHighlightAsync(oldMovieId, 590, 610, "Crossing", tags: [bathId], actors: [aikaId]);
        var rough = await AddHighlightAsync(oldMovieId, 700, 710, tags: [roughId]);
        var bare = await AddHighlightAsync(oldMovieId, 30, 45, "Bare"); // in Intro, which has Bath and Aika
        // Rough inherits Scene 2's Rin.

        var cards = (await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        var taggedCard = cards.Single(c => c.HighlightId == tagged);
        Assert.Equal(["Bath"], taggedCard.Tags.Select(t => t.Name));
        Assert.Equal(["Aika"], taggedCard.Actors);
        Assert.Equal(new SceneTagItem(roughId, "Rough", "Play"), Assert.Single(cards.Single(c => c.HighlightId == rough).Tags));
        var bareCard = cards.Single(c => c.HighlightId == bare);
        Assert.Empty(bareCard.Tags); // the scene's tags don't flow down
        Assert.Equal(["Aika"], bareCard.Actors); // its scene's actors do

        Assert.Equal([tagged], await HighlightIdsAsync(new SceneWallFilter(TagIds: [bathId])));
        Assert.Equal([rough], await HighlightIdsAsync(new SceneWallFilter(TagIds: [playId]))); // subtag rollup
        Assert.Equal(new[] { tagged, bare }.Order(), (await HighlightIdsAsync(new SceneWallFilter(ActorIds: [aikaId]))).Order());
        Assert.Equal([rough], await HighlightIdsAsync(new SceneWallFilter(ActorIds: [rinId])));
    }

    [Fact]
    public async Task UntitledHighlightCards_AreNamedAfterTheirActorsAndTags()
    {
        await SeedAsync();
        await AddHighlightAsync(oldMovieId, 30, 45, tags: [bathId, roughId], actors: [aikaId]);
        await AddHighlightAsync(oldMovieId, 50, 60, tags: [bathId]);
        await AddHighlightAsync(oldMovieId, 70, 80);

        var cards = (await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;

        // The last two inherit Intro's Aika.
        Assert.Equal(["Aika — Bath, Rough", "Aika — Bath", "Aika"], cards.Select(c => c.DisplayTitle));
    }

    [Fact]
    public async Task ContainingScene_OfTwoSharingAStart_IsTheHigherId_InMovieDetailsCount()
    {
        await SeedAsync();
        int tieMovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tie = new Movie { Code = "TIE-001", MetaStudio = "S1", MediaDurationSeconds = 3600 };
            db.Movies.Add(tie);
            await db.SaveChangesAsync();
            tieMovieId = tie.Id;
            // Only data that bypassed validation (e.g. an import) has two scenes sharing a start. The
            // higher id is inserted first, so neither insertion nor row order decides the tie.
            db.Scenes.Add(new Scene { Id = 901, MovieId = tie.Id, StartSeconds = 100 });
            await db.SaveChangesAsync();
            db.Scenes.Add(new Scene { Id = 900, MovieId = tie.Id, StartSeconds = 100 });
            await db.SaveChangesAsync();
        }
        await scenes.AddSceneTagAsync(900, bathId);
        await scenes.AddSceneTagAsync(901, roughId);
        await AddHighlightAsync(tieMovieId, 150, 160);

        var counts = HighlightRanges.CountStartsByScene(await scenes.GetScenesAsync(tieMovieId), await new MovieHighlightService(factory).GetHighlightsAsync(tieMovieId));
        Assert.Equal(new Dictionary<int, int> { [901] = 1 }, counts);
    }

    [Fact]
    public async Task HighlightCards_CarryTheirOwnDetails_InPlaybackOrder()
    {
        await SeedAsync();
        var second = await AddHighlightAsync(oldMovieId, 700, 710);
        var first = await AddHighlightAsync(oldMovieId, 30, 45, "Climax", favorite: true);

        var page = await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([first, second], page.Cards.Select(c => c.HighlightId));
        var climax = page.Cards[0];
        Assert.Equal(("Climax", "OLD-001", "Old one", oldMovieId), (climax.DisplayTitle, climax.Code, climax.MovieTitle, climax.MovieId));
        Assert.Equal((30d, 45d, true), (climax.StartSeconds, climax.EndSeconds, climax.IsFavorite));
        Assert.Equal("Rin", page.Cards[1].DisplayTitle); // untitled: named after the actors it inherits from Scene 2
        Assert.Null(climax.ThumbVersion);
    }

    [Fact]
    public async Task HighlightFilters_UseTheHighlightsOwnFavoriteAndTitle_AndIgnoreHiddenScenes()
    {
        await SeedAsync();
        var intro = (await scenes.GetScenesAsync(oldMovieId)).Single(s => s.Title == "Intro").Id;
        await scenes.SetHiddenFromOverviewAsync(intro, true);
        var climax = await AddHighlightAsync(oldMovieId, 30, 45, "Climax", favorite: true);
        var plain = await AddHighlightAsync(newMovieId, 10, 20);

        Assert.Equal([plain, climax], await HighlightIdsAsync(new SceneWallFilter(IncludeHidden: false)));
        Assert.Equal([climax], await HighlightIdsAsync(new SceneWallFilter(FavoritesOnly: true)));
        Assert.Equal([climax], await HighlightIdsAsync(new SceneWallFilter(Text: "clim")));
        Assert.Equal([plain], await HighlightIdsAsync(new SceneWallFilter(Text: "NEW-0")));
        Assert.Equal([plain], await HighlightIdsAsync(new SceneWallFilter(Studios: ["Moodyz"])));
    }

    [Fact]
    public async Task HighlightSorts_DateAddedAndRandom()
    {
        await SeedAsync();
        var oldOne = await AddHighlightAsync(oldMovieId, 30, 45);
        var newOne = await AddHighlightAsync(newMovieId, 10, 20);

        Assert.Equal([newOne, oldOne], await HighlightIdsAsync(new SceneWallFilter(), SceneWallSort.DateAdded));
        Assert.Equal(2, (await HighlightIdsAsync(new SceneWallFilter(), SceneWallSort.Random)).Distinct().Count());
    }

    [Fact]
    public async Task HighlightOptions_ListTheHighlightsOwnValues()
    {
        await SeedAsync();
        await AddHighlightAsync(oldMovieId, 30, 45, tags: [bathId], actors: [aikaId]);
        await AddHighlightAsync(newMovieId, 10, 20); // no actors of its own: inherits Yua's scene's

        var options = await wall.GetHighlightOptionsAsync();

        Assert.Equal(["Bath"], options.Tags.Select(t => t.Name));
        Assert.Equal(["Aika", "Mikami Yua"], options.Actors.Select(a => a.Name));
        Assert.Equal(["Moodyz", "S1"], options.Studios);
    }

    /// <summary>Aika (E, 150cm) is on OLD-001/Intro; Yua (B, 170cm) on NEW-001/Scene 1; OLD-001/Scene 2 has
    /// nobody tagged. Bea (E, 170cm) joins OLD-001's cast but is tagged on no scene yet.</summary>
    private async Task<int> SeedActorAttributesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var aika = await db.Actors.FindAsync(aikaId);
        var yua = await db.Actors.FindAsync(yuaId);
        (aika!.CupSize, aika.HeightCm, aika.Bust, aika.Waist, aika.Hips, aika.BirthDate) = ("E", 150, 85, 58, 86, new DateTime(1998, 3, 1));
        (yua!.CupSize, yua.HeightCm, yua.Bust, yua.Waist, yua.Hips, yua.BirthDate) = ("B", 170, 92, 62, 90, new DateTime(1990, 1, 1));
        var bea = new Actor { FirstName = "Bea", CupSize = "E", HeightCm = 170, Bust = 85, Waist = 58, Hips = 86, BirthDate = new DateTime(2001, 1, 1) };
        db.Actors.Add(bea);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = oldMovieId, ActorId = bea.Id });
        await db.SaveChangesAsync();
        return bea.Id;
    }

    [Fact]
    public async Task ActorAttributeFilter_MatchesOnlyTaggedSceneActors_AndNeedsOneActressForEveryCriterion()
    {
        await SeedAsync();
        var beaId = await SeedActorAttributesAsync();

        // OLD-001/Scene 2 has only Rin of its own, so the rest of the movie's cast (Bea) doesn't count.
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["e"] })));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Height = new IntRange(165, 175) })));
        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, null) })));

        // Tagging Bea (E, 170) on Intro gives it one actress who meets both criteria.
        var intro = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single(c => c.DisplayTitle == "Intro").SceneId;
        await scenes.AddSceneActorAsync(intro, beaId);
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, null) })));
    }

    [Fact]
    public async Task ActorCupSizeFilter_UsesTheCupInEffectOnTheScenesReleaseDate()
    {
        await SeedAsync();
        await SeedActorAttributesAsync();
        // Aika is G from 2018 on, so OLD-001 (2019) shows her as G, not her regular E.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ActorCupSizePeriods.Add(new ActorCupSizePeriod { ActorId = aikaId, EffectiveFrom = new DateTime(2018, 1, 1), CupSize = "G" });
            await db.SaveChangesAsync();
        }
        var inIntro = await AddHighlightAsync(oldMovieId, 30, 45, actors: [aikaId]);

        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"] })));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["G"] })));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["G"], Height = new IntRange(null, 155) })));
        Assert.Equal([inIntro], await HighlightIdsAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["G"] })));
        Assert.Equal(["B", "G"], (await wall.GetOptionsAsync()).ActorAttributes.CupSizes);
    }

    [Fact]
    public async Task ActorAttributeFilter_SplitAcrossTwoSceneActresses_DoesNotMatch()
    {
        await SeedAsync();
        await SeedActorAttributesAsync();
        // Intro: Aika (E, 150) + Yua (B, 170) -> "E and 165+" is met only by two different actresses.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieActors.Add(new MovieActor { MovieId = oldMovieId, ActorId = yuaId });
            await db.SaveChangesAsync();
        }
        var intro = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single(c => c.DisplayTitle == "Intro").SceneId;
        await scenes.AddSceneActorAsync(intro, yuaId);

        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, null) })));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"] })));
        Assert.Equal(["NEW-001/Scene 1", "OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Height = new IntRange(165, null) })));
    }

    [Fact]
    public async Task ActorAttributeFilter_CombinesWithTheActorFilterAsIndependentConditions()
    {
        await SeedAsync();
        await SeedActorAttributesAsync();

        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorIds: [aikaId], ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"] })));
        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorIds: [aikaId], ActorAttributes: new ActorAttributeSelection { CupSizes = ["B"] })));
    }

    [Fact]
    public async Task ActorAttributeFilter_OnHighlights_UsesTheirOwnOrInheritedActors()
    {
        await SeedAsync();
        await SeedActorAttributesAsync();
        var inIntro = await AddHighlightAsync(oldMovieId, 30, 45, actors: [aikaId]); // Aika (E, 150)
        var inUntagged = await AddHighlightAsync(oldMovieId, 15, 25);                // in Aika's Intro, nobody of its own: inherits Aika
        var inYua = await AddHighlightAsync(newMovieId, 10, 20, actors: [yuaId]);    // Yua (B, 170)

        Assert.Equal(new[] { inIntro, inUntagged }.Order(), (await HighlightIdsAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { CupSizes = ["E"] }))).Order());
        Assert.Equal([inYua], await HighlightIdsAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Height = new IntRange(165, null) })));
        Assert.Equal(new[] { inIntro, inUntagged, inYua }.Order(), (await HighlightIdsAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Height = new IntRange(100, null) }))).Order());
    }

    [Fact]
    public async Task Options_OfferTheCupSizesAndRangeBoundsOfTheOfferedSceneActors()
    {
        await SeedAsync();
        await SeedActorAttributesAsync(); // Bea is tagged on no scene, so her E/170/2001 must not widen anything
        await AddHighlightAsync(oldMovieId, 30, 45, actors: [aikaId]);

        var scenesOptions = (await wall.GetOptionsAsync()).ActorAttributes;
        Assert.Equal(["B", "E"], scenesOptions.CupSizes);
        Assert.Equal(new RangeBounds(150, 170), scenesOptions.Height);
        Assert.Equal(new RangeBounds(85, 92), scenesOptions.Bust);
        Assert.Equal(new RangeBounds(58, 62), scenesOptions.Waist);
        Assert.Equal(new RangeBounds(86, 90), scenesOptions.Hips);
        // Aika is 20 on OLD-001 (her March birthday is after the January release); Yua is 34 on NEW-001.
        Assert.Equal(new RangeBounds(20, 34), scenesOptions.Age);

        var highlightOptions = (await wall.GetHighlightOptionsAsync()).ActorAttributes;
        Assert.Equal(["E"], highlightOptions.CupSizes);
        Assert.Equal(new RangeBounds(150, 150), highlightOptions.Height);
        Assert.Equal(new RangeBounds(20, 20), highlightOptions.Age);
    }

    [Fact]
    public async Task Options_WithoutActorMetadata_OfferNothing()
    {
        await SeedAsync();

        var options = (await wall.GetOptionsAsync()).ActorAttributes;

        Assert.Empty(options.CupSizes);
        Assert.Null(options.Height);
        Assert.Null(options.Age);
    }

    [Fact]
    public async Task ActorAttributeFilter_AgeUsesTheParentMoviesReleaseDate_OnScenesAndHighlights()
    {
        await SeedAsync(); // OLD-001 released 2019-01-01 (Aika on Intro), NEW-001 2024-01-01 (Yua)
        await SeedActorAttributesAsync();
        var inIntro = await AddHighlightAsync(oldMovieId, 30, 45, actors: [aikaId]);
        var inYua = await AddHighlightAsync(newMovieId, 10, 20, actors: [yuaId]);
        // Aika (1998-03-01) was 20 when OLD-001 came out; Yua (1990-01-01) was 34 when NEW-001 did.
        var twenty = new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Age = new IntRange(20, 20) });
        var thirtyFour = new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Age = new IntRange(34, 34) });

        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(twenty));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(thirtyFour));
        Assert.Equal([inIntro], await HighlightIdsAsync(twenty));
        Assert.Equal([inYua], await HighlightIdsAsync(thirtyFour));
        Assert.Equal(["NEW-001/Scene 1"], await TitlesAsync(new SceneWallFilter(ActorAttributes: new ActorAttributeSelection { Age = new IntRange(21, null) })));
    }

    [Fact]
    public async Task ActorAttributeFilter_AgeAndMeasurements_NeedTheSameSceneActress()
    {
        await SeedAsync();
        await SeedActorAttributesAsync();
        // Intro gets Yua too (bust 92, age 29 on OLD-001's 2019 release); Aika is 20 with bust 85.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MovieActors.Add(new MovieActor { MovieId = oldMovieId, ActorId = yuaId });
            await db.SaveChangesAsync();
        }
        var intro = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single(c => c.DisplayTitle == "Intro").SceneId;
        await scenes.AddSceneActorAsync(intro, yuaId);

        var split = new ActorAttributeSelection { Bust = new IntRange(90, null), Age = new IntRange(20, 20) }; // bust 92 is Yua (29), age 20 is Aika
        var same = new ActorAttributeSelection { Bust = new IntRange(90, null), Age = new IntRange(29, 29) };

        Assert.Empty(await TitlesAsync(new SceneWallFilter(ActorAttributes: split)));
        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ActorAttributes: same)));
    }

    // --- Apexes ---

    private async Task<int> AddApexAsync(int movieId, double seconds, int[]? tagIds = null, int[]? actorIds = null, bool favorite = false)
    {
        var apexes = new MovieApexService(factory);
        var result = await apexes.AddApexAsync(movieId, seconds, tagIds ?? [], actorIds ?? []);
        Assert.True(result.Success, result.ErrorMessage);
        if (favorite) await apexes.ToggleFavoriteAsync(result.ApexId!.Value);
        return result.ApexId!.Value;
    }

    private async Task<List<int>> ApexIdsAsync(SceneWallFilter filter, SceneWallSort sort = SceneWallSort.ReleaseDate) =>
        (await wall.GetApexPageAsync(filter, sort, 1, 0, 50)).Cards.Select(c => c.ApexId).ToList();

    [Fact]
    public async Task ApexCounts_CountTheApexesInsideEachSceneAndHighlight()
    {
        await SeedAsync();
        await AddApexAsync(oldMovieId, 100);
        await AddApexAsync(oldMovieId, 599);
        await AddApexAsync(oldMovieId, 600); // Scene 2's start, not Intro's end
        var inClip = await AddHighlightAsync(oldMovieId, 90, 120);
        var touching = await AddHighlightAsync(oldMovieId, 120, 600);

        var sceneCards = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        Assert.Equal(2, sceneCards.Single(c => c.DisplayTitle == "Intro").ApexCount);
        Assert.Equal(1, sceneCards.Single(c => c.Code == "OLD-001" && c.DisplayTitle == "Scene 2").ApexCount);
        Assert.Equal(0, sceneCards.Single(c => c.Code == "NEW-001").ApexCount);
        var highlightCards = (await wall.GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        Assert.Equal(1, highlightCards.Single(c => c.HighlightId == inClip).ApexCount);
        Assert.Equal(1, highlightCards.Single(c => c.HighlightId == touching).ApexCount);
    }

    [Fact]
    public async Task ApexOnly_KeepsTheScenesAndHighlightsContainingAnApex()
    {
        await SeedAsync();
        await AddApexAsync(oldMovieId, 900);
        var withApex = await AddHighlightAsync(oldMovieId, 850, 950);
        await AddHighlightAsync(oldMovieId, 100, 200);
        await AddHighlightAsync(oldMovieId, 900.5, 950); // starts just after it
        await AddHighlightAsync(newMovieId, 850, 950); // same times, another movie

        var apexOnly = new SceneWallFilter(ApexOnly: true);

        Assert.Equal(["OLD-001/Scene 2"], await TitlesAsync(apexOnly));
        Assert.Equal([withApex], await HighlightIdsAsync(apexOnly));
    }

    [Fact]
    public async Task ApexOnly_AnOpenEndedSceneEndsWhereTheNextStarts()
    {
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "GAP-001", MediaDurationSeconds = 3000 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        await scenes.AddSceneAsync(movieId, 0, null, "First");
        await scenes.AddSceneAsync(movieId, 1000, null, "Second");
        await AddApexAsync(movieId, 1200);

        Assert.Equal(["GAP-001/Second"], await TitlesAsync(new SceneWallFilter(ApexOnly: true)));
        var cards = (await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards;
        Assert.Equal(0, cards.Single(c => c.DisplayTitle == "First").ApexCount);
        Assert.Equal(1, cards.Single(c => c.DisplayTitle == "Second").ApexCount);
    }

    [Fact]
    public async Task ApexTags_KeepClipsWithAnApexOfThatTagOrASubtag()
    {
        await SeedAsync();
        await AddApexAsync(oldMovieId, 100, [bathId]);
        await AddApexAsync(oldMovieId, 900, [roughId]);
        var inIntro = await AddHighlightAsync(oldMovieId, 50, 150);
        await AddHighlightAsync(oldMovieId, 850, 950);

        Assert.Equal(["OLD-001/Intro"], await TitlesAsync(new SceneWallFilter(ApexTagIds: [bathId])));
        Assert.Equal(["OLD-001/Scene 2"], await TitlesAsync(new SceneWallFilter(ApexTagIds: [playId])));
        Assert.Equal([inIntro], await HighlightIdsAsync(new SceneWallFilter(ApexTagIds: [bathId])));
        // Scene tags don't count as apex tags: Intro is tagged Bath, Scene 2 Rough.
        Assert.Empty(await TitlesAsync(new SceneWallFilter(TagIds: [bathId], ApexTagIds: [roughId])));
    }

    [Fact]
    public async Task ApexPage_NamesTheActorEvenInASoloMovie()
    {
        await SeedAsync();
        await AddApexAsync(newMovieId, 100, [roughId]);

        // Unlike Movie Detail, the wall mixes movies, so her name still tells them apart.
        var card = (await wall.GetApexPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards.Single();
        Assert.Equal("Mikami Yua — Rough", card.DisplayTitle);
    }

    [Fact]
    public async Task ApexPage_CarriesTheApexAndMovieDetails()
    {
        await SeedAsync();
        var second = await AddApexAsync(oldMovieId, 900, [roughId], [aikaId], favorite: true);
        var first = await AddApexAsync(oldMovieId, 100, [bathId]);

        var page = await wall.GetApexPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([first, second], page.Cards.Select(c => c.ApexId));
        var card = page.Cards[1];
        Assert.Equal("Aika — Rough", card.DisplayTitle);
        Assert.Equal("OLD-001", card.Code);
        Assert.Equal("Old one", card.MovieTitle);
        Assert.Equal(900, card.Seconds);
        Assert.Equal(["Aika"], card.Actors);
        Assert.Equal(new SceneTagItem(roughId, "Rough", "Play"), Assert.Single(card.Tags));
        Assert.True(card.IsFavorite);
        Assert.Equal(3600d, card.DurationSeconds);
        // Without a media service nothing advertises a preview.
        Assert.Null(card.PreviewVersion);
        Assert.Equal(ApexWindow.Default, card.Window);
    }

    [Fact]
    public async Task ApexPage_CarriesEachApexsOwnWindow()
    {
        await SeedAsync();
        var apexId = (await new MovieApexService(factory).AddApexAsync(oldMovieId, 900, [], window: new ApexWindowOverride(2, 20))).ApexId;

        var card = Assert.Single((await wall.GetApexPageAsync(new SceneWallFilter(), SceneWallSort.ReleaseDate, 1, 0, 50)).Cards);

        Assert.Equal((apexId, new ApexWindow(2, 20)), ((int?)card.ApexId, card.Window));
    }

    [Fact]
    public async Task ApexPage_FiltersAndOrdersByTheApexsOwnValues()
    {
        await SeedAsync();
        var oldApex = await AddApexAsync(oldMovieId, 100, [roughId], [aikaId], favorite: true);
        var newApex = await AddApexAsync(newMovieId, 50, [bathId], [yuaId]);

        Assert.Equal([newApex, oldApex], await ApexIdsAsync(new SceneWallFilter()));
        Assert.Equal([oldApex], await ApexIdsAsync(new SceneWallFilter(ApexTagIds: [playId])));
        Assert.Equal([newApex], await ApexIdsAsync(new SceneWallFilter(ActorIds: [yuaId])));
        Assert.Equal([oldApex], await ApexIdsAsync(new SceneWallFilter(FavoritesOnly: true)));
        Assert.Equal([oldApex], await ApexIdsAsync(new SceneWallFilter(Studios: ["S1"])));
        Assert.Equal([newApex], await ApexIdsAsync(new SceneWallFilter(Text: "new")));
        // The scene tag filter and ApexOnly don't apply to apexes.
        Assert.Equal([newApex, oldApex], await ApexIdsAsync(new SceneWallFilter(TagIds: [bathId], ApexOnly: true)));
    }

    [Fact]
    public async Task ApexOptions_OfferTheApexesActorsStudiosAndTags()
    {
        await SeedAsync();
        await AddApexAsync(oldMovieId, 100, [roughId], [aikaId]);

        var options = await wall.GetApexOptionsAsync();

        Assert.Empty(options.Tags);
        Assert.Equal([new SceneWallOption(aikaId, "Aika")], options.Actors);
        Assert.Equal(["S1"], options.Studios);
        Assert.Equal([new SceneWallOption(roughId, "Play › Rough")], options.ApexTags);
        Assert.Equal(options.ApexTags, (await wall.GetOptionsAsync()).ApexTags);
        Assert.Equal(options.ApexTags, (await wall.GetHighlightOptionsAsync()).ApexTags);
    }
}
