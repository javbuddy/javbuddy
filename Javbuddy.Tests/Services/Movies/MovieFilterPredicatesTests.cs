using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

/// <summary>In-memory evaluation, the way Actor Detail applies these predicates;
/// MovieGridQueryServiceTests covers the same predicates translated to SQL.</summary>
public class MovieFilterPredicatesTests
{
    private static Movie MovieTagged(string code, params Tag[] tags)
    {
        var movie = new Movie { Code = code };
        foreach (var tag in tags) movie.MovieTags.Add(new MovieTag { Tag = tag });
        return movie;
    }

    private static List<string> Codes(IQueryable<Movie> query) => query.Select(m => m.Code!).Order().ToList();

    [Fact]
    public void WhereGenres_RollsUpSubtagsToTheirParent_AndMatchesCompoundAndBareSubtagNames()
    {
        var play = new Tag { Name = "Play" };
        var rough = new Tag { Name = "Rough", ParentTag = play };
        var drama = new Tag { Name = "Drama" };
        var movies = new[] { MovieTagged("A-001", rough), MovieTagged("A-002", play), MovieTagged("A-003", drama) }.AsQueryable();

        Assert.Equal(["A-001", "A-002"], Codes(MovieFilterPredicates.WhereGenres(movies, ["play"])));
        Assert.Equal(["A-001"], Codes(MovieFilterPredicates.WhereGenres(movies, ["Play##Rough"])));
        Assert.Equal(["A-001"], Codes(MovieFilterPredicates.WhereGenres(movies, ["Rough"])));
        Assert.Equal(["A-001", "A-002", "A-003"], Codes(MovieFilterPredicates.WhereGenres(movies, [])));
    }

    [Fact]
    public void WhereGenres_MatchAll_NeedsEverySelectedGenre_WithRollup()
    {
        var play = new Tag { Name = "Play" };
        var rough = new Tag { Name = "Rough", ParentTag = play };
        var drama = new Tag { Name = "Drama" };
        var movies = new[] { MovieTagged("A-001", rough, drama), MovieTagged("A-002", play), MovieTagged("A-003", drama) }.AsQueryable();

        Assert.Equal(["A-001", "A-002", "A-003"], Codes(MovieFilterPredicates.WhereGenres(movies, ["Play", "Drama"])));
        Assert.Equal(["A-001"], Codes(MovieFilterPredicates.WhereGenres(movies, ["Play", "Drama"], matchAll: true)));
        Assert.Equal(["A-001"], Codes(MovieFilterPredicates.WhereGenres(movies, ["Play##Rough", "drama"], matchAll: true)));
        Assert.Equal(["A-001", "A-002", "A-003"], Codes(MovieFilterPredicates.WhereGenres(movies, [], matchAll: true)));
    }

    [Fact]
    public void WhereGenres_MatchesATagTheMovieOnlyHasThroughItsClips()
    {
        var squirt = new Tag { Name = "Squirt" };
        var movie = new Movie { Code = "C-001" };
        movie.MovieTags.Add(new MovieTag { Movie = movie, Tag = squirt, IsExplicit = false, FromClips = true });

        Assert.Equal(["C-001"], Codes(MovieFilterPredicates.WhereGenres(new[] { movie }.AsQueryable(), ["Squirt"])));
    }

    [Theory]
    [InlineData(MovieFeatureFilterOption.Favorites, "F-FAV")]
    [InlineData(MovieFeatureFilterOption.HasFavoriteScene, "F-FAVSCENE")]
    [InlineData(MovieFeatureFilterOption.UnmatchedActors, "F-UNMATCHED")]
    [InlineData(MovieFeatureFilterOption.MissingCast, "F-NOCAST")]
    [InlineData(MovieFeatureFilterOption.Vr, "F-VR")]
    public void WhereFeatures_AppliesEachOption(MovieFeatureFilterOption feature, string expectedCodes)
    {
        var fetched = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withScene = new Movie { Code = "F-SCENE", MetaFetchedAt = fetched, MetaActresses = "Yua" };
        withScene.Scenes.Add(new Scene());
        var withFavoriteScene = new Movie { Code = "F-FAVSCENE", MetaFetchedAt = fetched, MetaActresses = "Yua" };
        withFavoriteScene.Scenes.Add(new Scene { IsFavorite = true });
        var movies = new[]
        {
            new Movie { Code = "F-FAV", IsFavorite = true, MetaFetchedAt = fetched, MetaActresses = "Yua" },
            withScene,
            withFavoriteScene,
            new Movie { Code = "F-UNMATCHED", HasUnmatchedActors = true, MetaFetchedAt = fetched, MetaActresses = "Yua" },
            new Movie { Code = "F-NOCAST", MetaFetchedAt = fetched, MetaActresses = "" },
            new Movie { Code = "F-VR", VrType = "VR180 SBS", MetaFetchedAt = fetched, MetaActresses = "Yua" },
            // Not fetched yet, so an empty cast doesn't count as "no cast".
            new Movie { Code = "F-UNFETCHED" },
        }.AsQueryable();

        Assert.Equal(
            expectedCodes.Split(',').Order().ToList(),
            Codes(MovieFilterPredicates.WhereFeatures(movies, [feature])));
    }

    [Fact]
    public void BuildGenreOptions_AddsParentAndCompoundPathForSubtags_DedupedCaseInsensitively()
    {
        var options = MovieFilterPredicates.BuildGenreOptions(
            [("Rough", "Play"), ("drama", null), ("Drama", null), ("Rough", "Play")]);

        Assert.Equal(["drama", "Play", "Play##Rough"], options);
    }

    [Fact]
    public void BuildGenreOptions_ListsEachParentRightBeforeItsSubtags()
    {
        var options = MovieFilterPredicates.BuildGenreOptions([("Short", "Hair"), ("Long", "Hair"), ("Hair colour", null)]);

        Assert.Equal(["Hair", "Hair##Long", "Hair##Short", "Hair colour"], options);
    }

    private static IQueryable<Movie> WithMedia() => new[]
    {
        new Movie { Code = "SD", MediaWidth = 720, MediaScanType = "Progressive", MediaVideoCodec = "AVC", MetaStudio = "S1", NfoDriftKind = NfoDriftKind.None },
        new Movie { Code = "HD", MediaWidth = 1920, MediaScanType = " progressive ", MediaVideoCodec = "hevc", MetaStudio = "Moodyz", NfoDriftKind = NfoDriftKind.ExternalEdit },
        new Movie { Code = "4K", MediaWidth = 3840, MediaScanType = "Interlaced", MediaVideoCodec = "HEVC", MetaStudio = null, NfoDriftKind = NfoDriftKind.None },
        new Movie { Code = "8K", MediaWidth = 7680, MediaScanType = null, MediaVideoCodec = null, MetaStudio = "S1", NfoDriftKind = NfoDriftKind.None },
        new Movie { Code = "NOW", MediaWidth = null },
    }.AsQueryable();

    [Fact]
    public void WhereResolutions_MatchesAnyCheckedWidthBucket_AndIgnoresUnknownWidth()
    {
        var movies = WithMedia();

        Assert.Equal(["HD"], Codes(MovieFilterPredicates.WhereResolutions(movies, [MovieResolutionFilterOption.Hd])));
        Assert.Equal(["4K", "SD"], Codes(MovieFilterPredicates.WhereResolutions(movies, [MovieResolutionFilterOption.Sd, MovieResolutionFilterOption.FourK])));
        Assert.Equal(["8K"], Codes(MovieFilterPredicates.WhereResolutions(movies, [MovieResolutionFilterOption.EightK])));
        Assert.Equal(5, Codes(MovieFilterPredicates.WhereResolutions(movies, [])).Count);
    }

    [Fact]
    public void WhereScanTypes_ProgressiveIsTrimmedAndCaseInsensitive_InterlacedIsAnyOtherKnownValue()
    {
        var movies = WithMedia();

        Assert.Equal(["HD", "SD"], Codes(MovieFilterPredicates.WhereScanTypes(movies, [MovieScanTypeFilterOption.Progressive])));
        Assert.Equal(["4K"], Codes(MovieFilterPredicates.WhereScanTypes(movies, [MovieScanTypeFilterOption.Interlaced])));
    }

    [Fact]
    public void WhereCodecsAndStudios_UseTheGivenCollectionsComparer()
    {
        var movies = WithMedia();
        var codecs = new HashSet<string>(["hevc"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(["4K", "HD"], Codes(MovieFilterPredicates.WhereCodecs(movies, codecs)));
        Assert.Equal(["8K", "SD"], Codes(MovieFilterPredicates.WhereStudios(movies, ["S1"])));
        Assert.Equal(5, Codes(MovieFilterPredicates.WhereStudios(movies, null)).Count);
    }

    [Fact]
    public void WhereNfoDriftKinds_MatchesAnyCheckedKind()
    {
        var movies = WithMedia();

        Assert.Equal(["HD"], Codes(MovieFilterPredicates.WhereNfoDriftKinds(movies, [NfoDriftKind.ExternalEdit])));
    }

    private static Movie MovieWithCast(string code, params Actor[] cast)
    {
        var movie = new Movie { Code = code };
        foreach (var actor in cast) movie.MovieActors.Add(new MovieActor { Actor = actor });
        return movie;
    }

    private static IQueryable<Movie> ActorAttributeMovies() => new[]
    {
        MovieWithCast("M-E170", new Actor { CupSize = "E", HeightCm = 170 }),
        MovieWithCast("M-E158", new Actor { CupSize = "E", HeightCm = 158 }),
        MovieWithCast("M-SPLIT", new Actor { CupSize = "E", HeightCm = 150 }, new Actor { CupSize = "B", HeightCm = 170 }),
        MovieWithCast("M-NOHEIGHT", new Actor { CupSize = "E" }),
        MovieWithCast("M-NOCUP", new Actor { HeightCm = 170 }),
        MovieWithCast("M-LOWER", new Actor { CupSize = "e", HeightCm = 170 }),
        MovieWithCast("M-PADDED", new Actor { CupSize = " e ", HeightCm = 120 }),
        MovieWithCast("M-NOCAST"),
    }.AsQueryable();

    [Fact]
    public void WhereActorAttributes_NoCriteria_ReturnsEverything() =>
        Assert.Equal(8, MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), ActorAttributeSelection.Empty, Today).Count());

    [Fact]
    public void WhereActorAttributes_CupOnly_MatchesAnyActressAndIgnoresCase()
    {
        var codes = Codes(MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), new ActorAttributeSelection { CupSizes = ["E"] }, Today));
        Assert.Equal(["M-E158", "M-E170", "M-LOWER", "M-NOHEIGHT", "M-PADDED", "M-SPLIT"], codes);
    }

    [Fact]
    public void WhereActorAttributes_HeightOnly_IsInclusiveAndExcludesUnknownHeight()
    {
        var codes = Codes(MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), new ActorAttributeSelection { Height = new IntRange(158, 170) }, Today));
        Assert.Equal(["M-E158", "M-E170", "M-LOWER", "M-NOCUP", "M-SPLIT"], codes);
    }

    [Fact]
    public void WhereActorAttributes_CupAndHeight_RequireTheSameActress()
    {
        var codes = Codes(MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, 175) }, Today));
        Assert.Equal(["M-E170", "M-LOWER"], codes); // M-SPLIT: the E-cup actress is 150cm, the 170cm one is B-cup
    }

    [Fact]
    public void WhereActorAttributes_HeightMinOnly_And_MaxOnly()
    {
        Assert.Equal(["M-E170", "M-LOWER", "M-NOCUP", "M-SPLIT"], Codes(MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), new ActorAttributeSelection { Height = new IntRange(165, null) }, Today)));
        Assert.Equal(["M-E158", "M-PADDED", "M-SPLIT"], Codes(MovieFilterPredicates.WhereActorAttributes(ActorAttributeMovies(), new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(null, 158) }, Today)));
    }

    [Fact]
    public void WhereActorAttributes_Transgender_OnlyNeedsOneTransgenderActress_ExcludeNeedsNone()
    {
        var movies = new[]
        {
            MovieWithCast("TRANS", new Actor { IsTransgender = true, CupSize = "E" }),
            MovieWithCast("MIXED", new Actor { IsTransgender = true, CupSize = "B" }, new Actor { CupSize = "E" }),
            MovieWithCast("PLAIN", new Actor { CupSize = "E" }),
            MovieWithCast("NOCAST"),
        }.AsQueryable();

        Assert.Equal(["MIXED", "TRANS"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Transgender = TransgenderFilter.Only }, Today)));
        // With another criterion, the same actress has to meet both.
        Assert.Equal(["TRANS"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Transgender = TransgenderFilter.Only, CupSizes = ["E"] }, Today)));
        // Exclude is about the whole cast and keeps a movie without one.
        Assert.Equal(["NOCAST", "PLAIN"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Transgender = TransgenderFilter.Exclude }, Today)));
        Assert.Equal(["PLAIN"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Transgender = TransgenderFilter.Exclude, CupSizes = ["E"] }, Today)));
    }

    private static Actor Act(string? cup = null, int? h = null, int? b = null, int? w = null, int? hip = null, string? born = null) =>
        new() { CupSize = cup, HeightCm = h, Bust = b, Waist = w, Hips = hip, BirthDate = born is null ? null : DateTime.Parse(born) };

    private static Movie MovieOn(string code, string? released, params Actor[] cast)
    {
        var movie = MovieWithCast(code, cast);
        movie.MetaReleaseDate = released is null ? null : DateTime.Parse(released);
        return movie;
    }

    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public void WhereActorAttributes_MeasurementRanges_AreInclusiveAndNeedNonNull()
    {
        var movies = new[]
        {
            MovieOn("IN", null, Act(b: 85, w: 60, hip: 88)),
            MovieOn("LOWEDGE", null, Act(b: 80, w: 55, hip: 80)),
            MovieOn("BUST-LOW", null, Act(b: 79, w: 60, hip: 88)),
            MovieOn("NOWAIST", null, Act(b: 85, hip: 88)),
            MovieOn("SPLIT", null, Act(b: 85, w: 99, hip: 88), Act(b: 70, w: 60, hip: 88)),
        }.AsQueryable();
        var sel = new ActorAttributeSelection { Bust = new IntRange(80, 90), Waist = new IntRange(55, 65), Hips = new IntRange(80, null) };

        Assert.Equal(["IN", "LOWEDGE"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, sel, Today)));
    }

    [Theory]
    [InlineData("2000-06-15", "2021-06-15", 21, 21, true)]   // birthday on the release date: already 21
    [InlineData("2000-06-16", "2021-06-15", 21, 21, false)]  // day before the 21st birthday: still 20
    [InlineData("2000-06-16", "2021-06-15", 20, 20, true)]
    [InlineData("2000-02-29", "2021-02-28", 21, 21, false)]  // Feb 29 turns on Mar 1 in a non-leap year
    [InlineData("2000-02-29", "2021-03-01", 21, 21, true)]
    [InlineData("2000-06-15", "2021-06-15", 22, null, false)]
    [InlineData("2000-06-15", "2021-06-15", null, 20, false)]
    [InlineData("2000-06-15", "2021-06-15", null, 21, true)]
    public void WhereActorAttributes_Age_IsAgeOnTheReleaseDate(string born, string released, int? min, int? max, bool expected)
    {
        var movies = new[] { MovieOn("X", released, Act(born: born)) }.AsQueryable();

        var codes = Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Age = new IntRange(min, max) }, Today));

        Assert.Equal(expected ? ["X"] : [], codes);
        // The same answer the app's own displayed age gives for that movie.
        var age = Javbuddy.Services.Actors.ActorPhysicalAttributesHelper.CalculateAge(DateTime.Parse(born), DateOnly.FromDateTime(DateTime.Parse(released)))!.Value;
        Assert.Equal(expected, age >= (min ?? 0) && age <= (max ?? 200));
    }

    [Fact]
    public void WhereActorAttributes_Age_FallsBackToTodayWithoutAReleaseDate_AndNeedsABirthDate()
    {
        var movies = new[]
        {
            MovieOn("NO-RELEASE", null, Act(born: "2000-06-15")),  // 26 on Today (2026-06-15)
            MovieOn("NO-BIRTH", "2021-01-01", Act(h: 160)),
        }.AsQueryable();

        Assert.Equal(["NO-RELEASE"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Age = new IntRange(26, 26) }, Today)));
        Assert.DoesNotContain("NO-BIRTH", Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { Age = new IntRange(0, 99) }, Today)));
    }

    [Fact]
    public void WhereActorAttributes_AllCriteria_NeedOneActress()
    {
        var movies = new[]
        {
            MovieOn("ONE", "2021-06-15", Act(cup: "E", h: 170, born: "2000-06-15")),
            MovieOn("SPLIT", "2021-06-15", Act(cup: "E", h: 150, born: "2000-06-15"), Act(cup: "B", h: 170, born: "1990-01-01")),
        }.AsQueryable();
        var sel = new ActorAttributeSelection { CupSizes = ["e"], Height = new IntRange(165, null), Age = new IntRange(20, 22) };

        Assert.Equal(["ONE"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, sel, Today)));
    }

    [Fact]
    public void WhereActors_AppliesEveryCriterionExceptCupSizeAndAge()
    {
        var actors = new[] { Act("E", 170, 85, 60, 88, "1990-01-01"), Act("E", 150, 85, 60, 88, "1990-01-01"), Act("B", 170, 85, 60, 88), Act("B", 170, 95, 60, 88) }.AsQueryable();
        var sel = new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, null), Bust = new IntRange(80, 90), Age = new IntRange(99, 99) };

        Assert.Equal(2, MovieFilterPredicates.WhereActors(actors, sel).Count());
    }

    [Fact]
    public void WhereActorAttributes_CupSize_IsTheOneInEffectOnTheReleaseDate()
    {
        // C regularly; E from 2022-01-01, G from 2024-01-01.
        static Actor Augmented()
        {
            var actor = Act(cup: "C");
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2024, 1, 1), CupSize = "G" });
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = "E" });
            return actor;
        }
        var movies = new[]
        {
            MovieOn("BEFORE", "2021-12-31", Augmented()),
            MovieOn("FROM-E", "2022-01-01", Augmented()),
            MovieOn("LATE-E", "2023-12-31", Augmented()),
            MovieOn("FROM-G", "2024-01-01", Augmented()),
            MovieOn("UNDATED", null, Augmented()),
        }.AsQueryable();

        Assert.Equal(["BEFORE", "UNDATED"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { CupSizes = ["C"] }, Today)));
        Assert.Equal(["FROM-E", "LATE-E"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { CupSizes = ["e"] }, Today)));
        Assert.Equal(["FROM-G"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { CupSizes = ["G"] }, Today)));
    }

    [Fact]
    public void WhereActorAttributes_CupSizePeriod_AppliesEvenWithoutARegularCupSize()
    {
        var actor = Act();
        actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = "F" });
        var movies = new[] { MovieOn("OLD", "2020-01-01", actor), MovieOn("NEW", "2023-01-01", actor) }.AsQueryable();

        Assert.Equal(["NEW"], Codes(MovieFilterPredicates.WhereActorAttributes(movies, new ActorAttributeSelection { CupSizes = ["F"] }, Today)));
    }
    [Fact]
    public void WhereNotGenres_And_WhereNotStudios_Exclude_LikeTheirIncludeCounterparts()
    {
        var play = new Tag { Name = "Play" };
        var rough = new Tag { Name = "Rough", ParentTag = play };
        var drama = new Tag { Name = "Drama" };
        var a = MovieTagged("A-001", rough);
        a.MetaStudio = "S1";
        var movies = new[] { a, MovieTagged("A-002", drama), MovieTagged("A-003") }.AsQueryable();

        Assert.Equal(["A-002", "A-003"], Codes(MovieFilterPredicates.WhereNotGenres(movies, ["play"])));
        Assert.Equal(["A-002", "A-003"], Codes(MovieFilterPredicates.WhereNotStudios(movies, ["S1"])));
        Assert.Equal(["A-001", "A-002", "A-003"], Codes(MovieFilterPredicates.WhereNotGenres(movies, [])));
    }

    [Fact]
    public void WhereRanges_InMemory_MatchesTheSameMoviesAsInSql()
    {
        var cast = new Movie { Code = "R-001", MetaReleaseDate = new DateTime(2020, 6, 1), MediaVideoBitRateKbps = 5500 };
        cast.MovieActors.Add(new MovieActor { Actor = new Actor { IsFavorite = true } });
        var movies = new[] { cast, new Movie { Code = "R-002", MetaReleaseDate = new DateTime(2021, 1, 1), MediaOverallBitRateKbps = 6000 }, new Movie { Code = "R-003" } }.AsQueryable();

        Assert.Equal(["R-001"], Codes(MovieFilterPredicates.WhereRanges(movies, new() { ReleaseYear = new(null, 2020) })));
        Assert.Equal(["R-001"], Codes(MovieFilterPredicates.WhereRanges(movies, new() { BitrateMbps = new(5, 5) })));
        Assert.Equal(["R-002", "R-003"], Codes(MovieFilterPredicates.WhereRanges(movies, new() { CastSize = new(null, 0) })));
        Assert.Equal(["R-001"], Codes(MovieFilterPredicates.WhereFeatures(movies, [MovieFeatureFilterOption.HasFavoriteActor])));
    }

    [Fact]
    public void SortOrders_InMemory_SortCodesNaturally_AndCountsWithCodeBreakingTies()
    {
        var many = new Movie { Code = "B-1" };
        many.Scenes.Add(new Scene());
        var movies = new[] { new Movie { Code = "A-10" }, many, new Movie { Code = "A-9" } }.AsQueryable();

        static List<string> Sorted(IQueryable<Movie> q, string field, bool descending) =>
            MovieSortOrders.Apply(q, new MovieGridSort(field, descending, 1), m => m.CreatedAt).Select(m => m.Code!).ToList();

        Assert.Equal(["A-9", "A-10", "B-1"], Sorted(movies, "code", false));
        Assert.Equal(["B-1", "A-10", "A-9"], Sorted(movies, "scenes", true));
        // Codes compare ignoring case, as in SQL (the Code column is NOCASE).
        var mixed = new[] { new Movie { Code = "ZZZ-1" }, new Movie { Code = "abc-10" }, new Movie { Code = "Abd-1" }, new Movie { Code = "ABC-2" } }.AsQueryable();
        Assert.Equal(["ABC-2", "abc-10", "Abd-1", "ZZZ-1"], Sorted(mixed, "code", false));
        // No movie has a bitrate: code breaks the tie.
        Assert.Equal(["A-10", "A-9", "B-1"], Sorted(movies, "bitrate", true));
    }
}
