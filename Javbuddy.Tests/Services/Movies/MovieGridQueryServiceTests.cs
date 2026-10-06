using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Movies;

public sealed class MovieGridQueryServiceTests : IDisposable
{
    private static readonly MovieGridSort CodeAscending = new("title", Descending: false, RandomSeed: 1);

    private readonly TestDbContextFactory factory = new();
    private readonly MovieGridQueryService service;

    public MovieGridQueryServiceTests() => service = new MovieGridQueryService(factory);

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task CombinedFilters_NarrowRangeAndCountTogether()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", Status = MovieStatus.Got, MediaWidth = 1920, MetaStudio = "S1", JellyfinLibraryName = "Main" },
                new Movie { Code = "ABC-002", Status = MovieStatus.Got, MediaWidth = 3840, MetaStudio = "S1", JellyfinLibraryName = "Main" },
                new Movie { Code = "ABC-003", Status = MovieStatus.Missing, MediaWidth = 1920, MetaStudio = "S1", JellyfinLibraryName = "Main" },
                new Movie { Code = "ABC-004", Status = MovieStatus.Got, MediaWidth = 1920, MetaStudio = "Moodyz", JellyfinLibraryName = "Main" },
                new Movie { Code = "XYZ-005", Status = MovieStatus.Got, MediaWidth = 1280, MetaStudio = "S1", JellyfinLibraryName = "Main" },
                new Movie { Code = "ABC-006", Status = MovieStatus.Got, MediaWidth = 1920, MetaStudio = "S1", JellyfinLibraryName = "Other" });
            db.SaveChanges();
        }

        var filter = new MovieGridFilter(
            Status: MovieStatus.Got,
            Library: "Main",
            Resolutions: [MovieResolutionFilterOption.Hd],
            Studios: ["S1"],
            Text: "ABC");

        Assert.Equal(1, await service.CountAsync(filter));
        Assert.Equal(["ABC-001"], Codes(await service.GetRangeAsync(filter, CodeAscending, 0, 10)));
    }

    private static MovieGridFilter Attr(ActorAttributeSelection selection) => new(ActorAttributes: selection);

    [Fact]
    public async Task ActorAttributeFilter_NarrowsMoviesAndCount_ByTheSameActress()
    {
        using (var db = factory.CreateDbContext())
        {
            var split = new Movie { Code = "ACT-001" };
            split.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "A1", CupSize = "E", HeightCm = 150, Bust = 85 } });
            split.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "A2", CupSize = "B", HeightCm = 170, Bust = 70 } });
            var match = new Movie { Code = "ACT-002" };
            match.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "A3", CupSize = "E", HeightCm = 170, Bust = 85 } });
            db.Movies.AddRange(split, match, new Movie { Code = "ACT-003" });
            db.SaveChanges();
        }

        var filter = Attr(new ActorAttributeSelection { CupSizes = ["E"], Height = new IntRange(165, 175), Bust = new IntRange(80, 90) });

        Assert.Equal(1, await service.CountAsync(filter));
        Assert.Equal(["ACT-002"], Codes(await service.GetRangeAsync(filter, CodeAscending, 0, 10)));
    }

    [Fact]
    public async Task ActorCupSizeFilter_UsesTheCupInEffectOnTheReleaseDate()
    {
        // C regularly, E from 2022-01-01.
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Cup", CupSize = "C" };
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = "E" });
            foreach (var (code, released) in new[] { ("CUP-001", (DateTime?)new DateTime(2021, 12, 31)), ("CUP-002", new DateTime(2022, 1, 1)), ("CUP-003", null) })
            {
                var movie = new Movie { Code = code, MetaReleaseDate = released };
                movie.MovieActors.Add(new MovieActor { Actor = actor });
                db.Movies.Add(movie);
            }
            db.SaveChanges();
        }

        Assert.Equal(["CUP-001", "CUP-003"], Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { CupSizes = ["C"] }), CodeAscending, 0, 10)));
        Assert.Equal(["CUP-002"], Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { CupSizes = ["E"] }), CodeAscending, 0, 10)));
        Assert.Equal(1, await service.CountAsync(Attr(new ActorAttributeSelection { CupSizes = ["E"] })));
        Assert.Equal(["C", "E"], (await service.GetSummaryAsync(new MovieGridFilter())).ActorAttributes.CupSizes);
    }

    [Fact]
    public async Task ActorAgeFilter_UsesTheMoviesReleaseDate_AndTodayWithoutOne()
    {
        using (var db = factory.CreateDbContext())
        {
            var born = new DateTime(2000, 6, 15);
            var at21 = new Movie { Code = "AGE-001", MetaReleaseDate = new DateTime(2021, 6, 15) };
            var at20 = new Movie { Code = "AGE-002", MetaReleaseDate = new DateTime(2021, 6, 14) };
            var noRelease = new Movie { Code = "AGE-003" };
            var leap = new Movie { Code = "AGE-004", MetaReleaseDate = new DateTime(2021, 2, 28) };
            foreach (var (movie, name) in new[] { (at21, "g1"), (at20, "g2"), (noRelease, "g3") })
            {
                movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = name, BirthDate = born } });
            }
            leap.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "g4", BirthDate = new DateTime(2000, 2, 29) } });
            db.Movies.AddRange(at21, at20, noRelease, leap);
            db.SaveChanges();
        }

        Assert.Equal(["AGE-001"], Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { Age = new IntRange(21, 21) }), CodeAscending, 0, 10)));
        Assert.Equal(["AGE-002", "AGE-004"], Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { Age = new IntRange(20, 20) }), CodeAscending, 0, 10)));
        // No release date: her age today is what counts.
        var todayAge = Javbuddy.Services.Actors.ActorPhysicalAttributesHelper.CalculateAge(new DateTime(2000, 6, 15))!.Value;
        Assert.Contains("AGE-003", Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { Age = new IntRange(todayAge, todayAge) }), CodeAscending, 0, 10)));
        // Open-ended ranges work on either side.
        Assert.Equal(["AGE-001"], Codes(await service.GetRangeAsync(Attr(new ActorAttributeSelection { Age = new IntRange(21, null) }), CodeAscending, 0, 10)).Where(c => c != "AGE-003").ToList());
    }

    [Fact]
    public async Task ActorAgeFilter_MatchesCalculateAge_OnLeapDayReleasesAndBirthDatesWithATime()
    {
        using (var db = factory.CreateDbContext())
        {
            // Released on Feb 29: an actress born 2003-03-01 is still 20 (her 21st birthday is tomorrow).
            var leapRelease = new Movie { Code = "EDGE-001", MetaReleaseDate = new DateTime(2024, 2, 29) };
            leapRelease.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "e1", BirthDate = new DateTime(2003, 3, 1) } });
            // Birthday today, birth date stored with a time of day: already 30.
            var withTime = new Movie { Code = "EDGE-002", MetaReleaseDate = new DateTime(2021, 3, 12) };
            withTime.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "e2", BirthDate = new DateTime(1991, 3, 12, 9, 30, 0) } });
            // Feb 29 birthday on a non-leap Feb 28: still 20; on Mar 1: 21.
            var leapBirth28 = new Movie { Code = "EDGE-003", MetaReleaseDate = new DateTime(2021, 2, 28) };
            leapBirth28.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "e3", BirthDate = new DateTime(2000, 2, 29) } });
            var leapBirth01 = new Movie { Code = "EDGE-004", MetaReleaseDate = new DateTime(2021, 3, 1) };
            leapBirth01.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "e4", BirthDate = new DateTime(2000, 2, 29) } });
            db.Movies.AddRange(leapRelease, withTime, leapBirth28, leapBirth01);
            db.SaveChanges();
        }

        async Task<List<string?>> Match(int? min, int? max) => Codes(await service.GetRangeAsync(
            Attr(new ActorAttributeSelection { Age = new IntRange(min, max) }), CodeAscending, 0, 10));

        Assert.Equal(["EDGE-001", "EDGE-003"], await Match(20, 20));
        Assert.DoesNotContain("EDGE-001", await Match(21, null));
        Assert.Equal(["EDGE-002"], await Match(30, 30));
        Assert.Equal(["EDGE-004"], await Match(21, 21));
        foreach (var (code, born, released) in new[] { ("EDGE-001", new DateTime(2003, 3, 1), new DateTime(2024, 2, 29)), ("EDGE-002", new DateTime(1991, 3, 12, 9, 30, 0), new DateTime(2021, 3, 12)) })
        {
            var age = Javbuddy.Services.Actors.ActorPhysicalAttributesHelper.CalculateAge(born, DateOnly.FromDateTime(released))!.Value;
            Assert.Contains(code, await Match(age, age));   // the SQL form agrees with the age the app displays
        }
    }

    [Fact]
    public async Task ActorAttributeFilter_AgeAndMeasurements_NeedTheSameActress()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "MIX-001", MetaReleaseDate = new DateTime(2021, 6, 15) };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "M1", Bust = 95, BirthDate = new DateTime(1990, 1, 1) } }); // 31, bust 95
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "M2", Bust = 80, BirthDate = new DateTime(2000, 6, 15) } }); // 21, bust 80
            db.Movies.Add(movie);
            db.SaveChanges();
        }

        Assert.Equal(0, await service.CountAsync(Attr(new ActorAttributeSelection { Bust = new IntRange(90, null), Age = new IntRange(21, 21) })));
        Assert.Equal(1, await service.CountAsync(Attr(new ActorAttributeSelection { Bust = new IntRange(90, null), Age = new IntRange(31, 31) })));
    }

    [Fact]
    public async Task Summary_ActorAttributeOptions_AreLibraryWideFromLinkedActorsOnly()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ACT-010" };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "B1", CupSize = "e", HeightCm = 150, Bust = 80 } });
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "B2", CupSize = "DD", HeightCm = 172, Bust = 96 } });
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "B3", CupSize = "C" } });
            db.Movies.Add(movie);
            db.Actors.Add(new Actor { FirstName = "Unlinked", CupSize = "K", HeightCm = 199, Bust = 120 }); // no movie, so ignored
            db.SaveChanges();
        }

        var options = (await service.GetSummaryAsync(Attr(new ActorAttributeSelection { CupSizes = ["C"] }))).ActorAttributes;

        Assert.Equal(["C", "E", "DD"], options.CupSizes);
        Assert.Equal(new RangeBounds(150, 172), options.Height);
        Assert.Equal(new RangeBounds(80, 96), options.Bust);
        Assert.Null(options.Waist);
    }

    [Fact]
    public async Task ActorCupSizeFilter_And_SummaryOptions_IgnoreWhitespaceAroundStoredValues()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ACT-020" };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "C1", CupSize = " e " } });
            db.Movies.Add(movie);
            db.SaveChanges();
        }

        Assert.Equal(1, await service.CountAsync(Attr(new ActorAttributeSelection { CupSizes = ["E"] })));
        Assert.Equal(["E"], (await service.GetSummaryAsync(new MovieGridFilter())).ActorAttributes.CupSizes);
    }

    [Fact]
    public async Task Summary_NoActorAttributes_OffersNothing()
    {
        var options = (await service.GetSummaryAsync(new MovieGridFilter())).ActorAttributes;

        Assert.Empty(options.CupSizes);
        Assert.Null(options.Height);
        Assert.Null(options.Age);
    }

    [Fact]
    public async Task Summary_StatusCountsIgnoreStatusButHonorOtherFilters_OptionsStayLibraryWide()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", Status = MovieStatus.Got, MetaStudio = "S1", MediaVideoCodec = "h264", LocalFileSizeBytes = 100, JellyfinLibraryName = "Main" },
                new Movie { Code = "ABC-002", Status = MovieStatus.Missing, MetaStudio = "S1" },
                new Movie { Code = "ABC-003", Status = MovieStatus.Got, MetaStudio = "Moodyz", MediaVideoCodec = "hevc", LocalFileSizeBytes = 50, JellyfinLibraryName = "Other" });
            db.SaveChanges();
        }

        var summary = await service.GetSummaryAsync(new MovieGridFilter(Status: MovieStatus.Got, Studios: ["S1"]));

        Assert.Equal((2, 1, 1), (summary.TotalCount, summary.MissingCount, summary.GotCount));
        Assert.Equal(2, summary.FilesCount);
        Assert.Equal(150, summary.TotalFileSizeBytes);
        Assert.Equal(["Main", "Other"], summary.LibraryNames);
        Assert.Equal(["h264", "hevc"], summary.Codecs);
        Assert.Equal(["Moodyz", "S1"], summary.Studios);
    }

    [Fact]
    public async Task Range_WindowsPartitionTheOrderedResultWithoutGapsOrDuplicates()
    {
        using (var db = factory.CreateDbContext())
        {
            for (var i = 1; i <= 25; i++)
            {
                db.Movies.Add(new Movie { Code = $"ABC-{i:000}", LocalFileSizeBytes = i % 4 == 0 ? null : i * 10 });
            }
            db.SaveChanges();
        }

        var sort = new MovieGridSort("size", Descending: true, RandomSeed: 1);
        var all = Codes(await service.GetRangeAsync(new MovieGridFilter(), sort, 0, 100));
        var windowed = new List<string?>();
        for (var skip = 0; skip < 25; skip += 7)
        {
            windowed.AddRange(Codes(await service.GetRangeAsync(new MovieGridFilter(), sort, skip, 7)));
        }

        Assert.Equal(all, windowed);
        Assert.Equal("ABC-025", all[0]);
        // Sizeless movies sort last regardless of direction.
        Assert.All(all.TakeLast(6), code => Assert.Equal(0, int.Parse(code![4..]) % 4));
    }

    [Fact]
    public async Task RandomSort_IsAStableFullShuffleForOneSeed()
    {
        using (var db = factory.CreateDbContext())
        {
            for (var i = 1; i <= 30; i++)
            {
                db.Movies.Add(new Movie { Code = $"ABC-{i:000}" });
            }
            db.SaveChanges();
        }

        var sort = new MovieGridSort("random", Descending: false, RandomSeed: 123_456_789);
        var first = Codes(await service.GetRangeAsync(new MovieGridFilter(), sort, 0, 30));
        var paged = Codes(await service.GetRangeAsync(new MovieGridFilter(), sort, 0, 15))
            .Concat(Codes(await service.GetRangeAsync(new MovieGridFilter(), sort, 15, 15)))
            .ToList();

        Assert.Equal(first, paged);
        Assert.Equal(30, first.Distinct().Count());
    }

    [Fact]
    public async Task StatusSort_AscendingPutsMissingFirst()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", Status = MovieStatus.Got },
                new Movie { Code = "ABC-002", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var result = await service.GetRangeAsync(new MovieGridFilter(), new MovieGridSort("status", false, 1), 0, 10);

        Assert.Equal(["ABC-002", "ABC-001"], Codes(result));
    }

    // A crop rewrites the movie's cached poster, so its thumbnail URL must change with it.
    [Fact]
    public async Task GetRangeAsync_PosterVersionChangesWhenCachedPosterIsRewritten()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", Status = MovieStatus.Got },
                new Movie { Code = "ABC-002", Status = MovieStatus.Got });
            db.CachedImages.AddRange(
                new CachedImage { Code = "ABC-001", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) },
                new CachedImage { Code = "ABC-002", Role = "poster", Variant = "thumb", UpdatedAt = new DateTime(2026, 1, 1) });
            db.SaveChanges();
        }

        var before = await service.GetRangeAsync(new MovieGridFilter(), CodeAscending, 0, 10);

        using (var db = factory.CreateDbContext())
        {
            db.CachedImages.Single(c => c.Code == "ABC-001").UpdatedAt = new DateTime(2026, 2, 1);
            db.SaveChanges();
        }

        var after = await service.GetRangeAsync(new MovieGridFilter(), CodeAscending, 0, 10);

        Assert.NotNull(before[0].PosterVersion);
        Assert.NotEqual(before[0].PosterVersion, after[0].PosterVersion);
        Assert.Equal(before[1].PosterVersion, after[1].PosterVersion);
    }

    [Fact]
    public async Task GenreFilter_MatchesWholeTagNamesIncludingCommas_AndRollsUpSubtags()
    {
        using (var db = factory.CreateDbContext())
        {
            var category = new Tag { Name = "Play" };
            var subtag = new Tag { Name = "Rough", ParentTag = category };
            var comma = new Tag { Name = "Nasty, hardcore" };
            var vrkm = new Tag { Name = "VRKM" };
            db.Tags.AddRange(category, subtag, comma, vrkm);
            AddMovie(db, "ABC-001", subtag);
            AddMovie(db, "ABC-002", comma);
            AddMovie(db, "ABC-003", vrkm);
            db.SaveChanges();
        }

        async Task<List<string?>> Matching(params string[] genres) =>
            Codes(await service.GetRangeAsync(new MovieGridFilter(Genres: genres), CodeAscending, 0, 10));

        Assert.Equal(["ABC-001"], await Matching("Play"));
        Assert.Equal(["ABC-001"], await Matching("Play##Rough"));
        Assert.Equal(["ABC-001"], await Matching("Rough"));
        Assert.Equal(["ABC-002"], await Matching("Nasty, hardcore"));
        Assert.Empty(await Matching("Nasty"));
        Assert.Empty(await Matching("VR"));
        Assert.Equal(["ABC-002", "ABC-003"], await Matching("Nasty, hardcore", "VRKM"));
    }

    [Fact]
    public async Task GenreOptions_ComeFromLinkedTagsOnly_NotTheMetaGenresCache()
    {
        // MetaGenres is a display cache rebuilt from MovieTag links (TagNormalization); a row
        // carrying only the cache string has never been through normalization, so its values are
        // neither offered nor matched until library discovery links them.
        using (var db = factory.CreateDbContext())
        {
            var category = new Tag { Name = "Play" };
            var subtag = new Tag { Name = "Rough", ParentTag = category };
            var solo = new Tag { Name = "solo" };
            var unused = new Tag { Name = "Unused" };
            db.Tags.AddRange(category, subtag, solo, unused);
            AddMovie(db, "ABC-001", subtag, solo);
            AddMovie(db, "ABC-002", solo);
            db.Movies.Add(new Movie { Code = "ABC-003", MetaGenres = "Legacy" });
            db.SaveChanges();
        }

        var summary = await service.GetSummaryAsync(new MovieGridFilter());

        Assert.Equal(["Play", "Play##Rough", "solo"], summary.Genres);
        Assert.Equal(0, await service.CountAsync(new MovieGridFilter(Genres: ["Legacy"])));
    }

    [Fact]
    public async Task FeatureAndScanTypeFilters_Apply()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", MediaScanType = " Progressive ", MediaHasSubtitleFile = true },
                new Movie { Code = "ABC-002", MediaScanType = "Interlaced", MediaSubtitleCount = 1 },
                new Movie { Code = "ABC-003", MediaScanType = "Progressive", MetaFetchedAt = DateTime.UtcNow },
                new Movie { Code = "ABC-004" });
            db.SaveChanges();
        }

        async Task<List<string?>> Matching(MovieGridFilter filter) =>
            Codes(await service.GetRangeAsync(filter, CodeAscending, 0, 10));

        Assert.Equal(["ABC-001", "ABC-003"], await Matching(new(ScanTypes: [MovieScanTypeFilterOption.Progressive])));
        Assert.Equal(["ABC-002"], await Matching(new(ScanTypes: [MovieScanTypeFilterOption.Interlaced])));
        Assert.Equal(["ABC-001", "ABC-002"], await Matching(new(Features: [MovieFeatureFilterOption.HasSubtitles])));
        Assert.Equal(["ABC-003"], await Matching(new(Features: [MovieFeatureFilterOption.MissingCast])));
        Assert.Equal(["ABC-001"], await Matching(new(CodePrefix: "ABC", Features: [MovieFeatureFilterOption.HasSubtitles], ScanTypes: [MovieScanTypeFilterOption.Progressive])));
    }

    [Fact]
    public async Task NfoDriftFilter_MatchesAnySelectedKind_AndAndsWithOtherFilters()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", NfoDriftKind = NfoDriftKind.JavbuddyChanged, IsFavorite = true },
                new Movie { Code = "ABC-002", NfoDriftKind = NfoDriftKind.ExternalEdit },
                new Movie { Code = "ABC-003", NfoDriftKind = NfoDriftKind.BothChanged, IsFavorite = true },
                new Movie { Code = "ABC-004", NfoDriftKind = NfoDriftKind.Unreadable },
                new Movie { Code = "ABC-005" });
            db.SaveChanges();
        }

        async Task<List<string?>> Matching(MovieGridFilter filter) =>
            Codes(await service.GetRangeAsync(filter, CodeAscending, 0, 10));

        Assert.Equal(["ABC-001"], await Matching(new(NfoDriftKinds: [NfoDriftKind.JavbuddyChanged])));
        Assert.Equal(["ABC-004"], await Matching(new(NfoDriftKinds: [NfoDriftKind.Unreadable])));
        Assert.Equal(["ABC-001", "ABC-002"], await Matching(new(NfoDriftKinds: [NfoDriftKind.JavbuddyChanged, NfoDriftKind.ExternalEdit])));
        Assert.Equal(["ABC-001", "ABC-002", "ABC-003", "ABC-004"], await Matching(new(NfoDriftKinds: MovieFilterOptions.NfoDriftKinds)));
        Assert.Equal(["ABC-001", "ABC-003"], await Matching(new(
            Features: [MovieFeatureFilterOption.Favorites],
            NfoDriftKinds: [NfoDriftKind.JavbuddyChanged, NfoDriftKind.ExternalEdit, NfoDriftKind.BothChanged])));
    }

    [Fact]
    public async Task NfoDriftCountsAndMovieIds_FollowTheFilterAndKinds()
    {
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "ABC-001", NfoDriftKind = NfoDriftKind.JavbuddyChanged, IsFavorite = true },
                new Movie { Code = "ABC-002", NfoDriftKind = NfoDriftKind.JavbuddyChanged },
                new Movie { Code = "ABC-003", NfoDriftKind = NfoDriftKind.ExternalEdit, IsFavorite = true },
                new Movie { Code = "ABC-004", NfoDriftKind = NfoDriftKind.BothChanged, IsFavorite = true },
                new Movie { Code = "ABC-005", IsFavorite = true });
            db.SaveChanges();
        }
        var favoritesWithDrift = new MovieGridFilter(Features: [MovieFeatureFilterOption.Favorites], NfoDriftKinds: MovieFilterOptions.NfoDriftKinds);

        var counts = await service.GetNfoDriftCountsAsync(favoritesWithDrift);
        var ids = await service.GetNfoDriftMovieIdsAsync(favoritesWithDrift, [NfoDriftKind.JavbuddyChanged, NfoDriftKind.BothChanged]);

        Assert.Equal(1, counts[NfoDriftKind.JavbuddyChanged]);
        Assert.Equal(1, counts[NfoDriftKind.ExternalEdit]);
        Assert.Equal(1, counts[NfoDriftKind.BothChanged]);
        Assert.False(counts.ContainsKey(NfoDriftKind.None));
        using var verify = factory.CreateDbContext();
        Assert.Equal(
            verify.Movies.Where(m => m.Code == "ABC-001" || m.Code == "ABC-004").Select(m => m.Id).OrderBy(i => i),
            ids.Order());
    }

    [Fact]
    public async Task DownloadingCodes_IncludeOnlyActiveOrCompletedTorrents()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-001" };
            db.Movies.Add(movie);
            db.TorrentDownloads.AddRange(
                new TorrentDownload { Movie = movie, MovieCode = "ABC-001", Status = TorrentDownloadStatus.Downloading },
                new TorrentDownload { Movie = movie, MovieCode = "ABC-002", Status = TorrentDownloadStatus.Completed },
                new TorrentDownload { Movie = movie, MovieCode = "ABC-003", Status = TorrentDownloadStatus.Removed });
            db.SaveChanges();
        }

        var codes = await service.GetDownloadingCodesAsync();

        Assert.True(codes.SetEquals(["ABC-001", "ABC-002"]));
        Assert.Contains("abc-001", codes);
    }

    private static void AddMovie(AppDbContext db, string code, params Tag[] tags)
    {
        var movie = new Movie { Code = code, MetaGenres = string.Join(", ", tags.Select(t => t.Name)) };
        db.Movies.Add(movie);
        foreach (var tag in tags)
        {
            db.MovieTags.Add(new MovieTag { Movie = movie, Tag = tag });
        }
    }

    private static List<string?> Codes(IEnumerable<Movie> movies) => movies.Select(m => m.Code).ToList();

    [Fact]
    public async Task GetMissingPageAsync_ReturnsOnlyMissingMovies_WithJustTheColumnsThePageAndTheGrabNeed()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "MIS-001", Status = MovieStatus.Missing, MetaTitle = "Title", MetaStudio = "S1", MetaReleaseDate = new DateTime(2026, 1, 2), MetaFetchedAt = new DateTime(2026, 2, 3), MetaDescription = "long description", MetaActresses = "A B" },
                new Movie { Code = "MIS-002", Status = MovieStatus.Missing },
                new Movie { Code = "GOT-001", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var page = await new MovieGridQueryService(factory).GetMissingPageAsync(sortDescending: true, skip: 0, take: 10);
        var missing = page.Movies;

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["MIS-001", "MIS-002"], missing.Select(m => m.Code).Order());
        var first = missing.Single(m => m.Code == "MIS-001");
        Assert.NotEqual(0, first.Id);
        Assert.Equal("Title", first.DisplayName);
        Assert.Equal("S1", first.MetaStudio);
        Assert.Equal(new DateTime(2026, 1, 2), first.MetaReleaseDate);
        Assert.NotNull(first.MetaFetchedAt);
        Assert.Null(first.MetaDescription);
        Assert.Null(first.MetaActresses);
        Assert.Null(missing.Single(m => m.Code == "MIS-002").MetaFetchedAt);
    }

    [Theory]
    [InlineData(true, new[] { "NEW-001", "MID-001", "MID-002", "OLD-001", "NOD-001" })]
    [InlineData(false, new[] { "NOD-001", "OLD-001", "MID-001", "MID-002", "NEW-001" })]
    public async Task GetMissingPageAsync_SortsByReleaseDate_UndatedOldest_TiesInInsertOrder(bool descending, string[] expected)
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "MID-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2022, 1, 1) },
                new Movie { Code = "NOD-001", Status = MovieStatus.Missing },
                new Movie { Code = "NEW-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2024, 1, 1) },
                new Movie { Code = "MID-002", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2022, 1, 1) },
                new Movie { Code = "OLD-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2019, 1, 1) });
            await db.SaveChangesAsync();
        }

        var page = await new MovieGridQueryService(factory).GetMissingPageAsync(descending, skip: 0, take: 10);

        Assert.Equal(expected, page.Movies.Select(m => m.Code));
    }

    [Fact]
    public async Task GetMissingPageAsync_ReturnsJustTheWindow_WithTheFullTotal()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 1; i <= 10; i++)
            {
                db.Movies.Add(new Movie { Code = $"MIS-{i:000}", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2000 + i, 1, 1) });
            }
            db.Movies.Add(new Movie { Code = "GOT-001", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var page = await new MovieGridQueryService(factory).GetMissingPageAsync(sortDescending: false, skip: 3, take: 4);

        Assert.Equal(10, page.TotalCount);
        Assert.Equal(["MIS-004", "MIS-005", "MIS-006", "MIS-007"], page.Movies.Select(m => m.Code));
    }

    [Fact]
    public async Task ActorIds_MatchMoviesWithAnySelectedActor()
    {
        int a1, a2;
        using (var db = factory.CreateDbContext())
        {
            var actor1 = new Actor { FirstName = "A1" };
            var actor2 = new Actor { FirstName = "A2" };
            var both = new Movie { Code = "ABC-001", Status = MovieStatus.Got };
            var one = new Movie { Code = "ABC-002", Status = MovieStatus.Got };
            both.MovieActors.Add(new MovieActor { Actor = actor1 });
            both.MovieActors.Add(new MovieActor { Actor = actor2 });
            one.MovieActors.Add(new MovieActor { Actor = actor2 });
            db.Movies.AddRange(both, one, new Movie { Code = "ABC-003", Status = MovieStatus.Got });
            db.SaveChanges();
            (a1, a2) = (actor1.Id, actor2.Id);
        }

        Assert.Equal(["ABC-001"], (await service.GetRangeAsync(new MovieGridFilter(ActorIds: [a1]), CodeAscending, 0, 10)).Select(m => m.Code));
        Assert.Equal(["ABC-001", "ABC-002"], (await service.GetRangeAsync(new MovieGridFilter(ActorIds: [a1, a2]), CodeAscending, 0, 10)).Select(m => m.Code));
        Assert.Equal(3, await service.CountAsync(new MovieGridFilter(ActorIds: [])));
    }

    [Fact]
    public async Task GetWithFilesAsync_ListsOnlyGotMoviesWithAFile_AndOffersOnlyTheirActors()
    {
        using (var db = factory.CreateDbContext())
        {
            var got = new Movie { Code = "ABC-001", Status = MovieStatus.Got };
            var missingWithFile = new Movie { Code = "ABC-002", Status = MovieStatus.Missing };
            var gotWithoutFile = new Movie { Code = "ABC-003", Status = MovieStatus.Got };
            got.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Zed" } });
            got.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Amy" } });
            missingWithFile.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Missy" } });
            gotWithoutFile.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Nofile" } });
            db.Movies.AddRange(got, missingWithFile, gotWithoutFile);
            db.SaveChanges();
            db.MovieFiles.AddRange(
                new MovieFile { MovieId = got.Id, FileName = "ABC-001.mp4" },
                new MovieFile { MovieId = missingWithFile.Id, FileName = "ABC-002.mp4" });
            db.SaveChanges();
        }

        Assert.Equal(["ABC-001"], (await service.GetWithFilesAsync(new MovieGridFilter(), CodeAscending)).Select(m => m.Code));
        Assert.Empty(await service.GetWithFilesAsync(new MovieGridFilter(Status: MovieStatus.Missing), CodeAscending));
        Assert.Equal(["Amy", "Zed"], (await service.GetActorOptionsWithFilesAsync()).Select(a => a.Name));
    }

    [Fact]
    public async Task GetActorOptionsAsync_MatchesTheSummaryActors()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-001", Status = MovieStatus.Missing };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Zed" } });
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Amy" } });
            db.Movies.Add(movie);
            db.Actors.Add(new Actor { FirstName = "Unlinked" });
            db.SaveChanges();
        }

        var options = await service.GetActorOptionsAsync();

        Assert.Equal(["Amy", "Zed"], options.Select(a => a.Name));
        Assert.Equal((await service.GetSummaryAsync(new MovieGridFilter())).Actors, options);
    }

    [Fact]
    public async Task Summary_OffersEveryActorLinkedToAMovie_ByName()
    {
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-001", Status = MovieStatus.Missing };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Zed" } });
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Amy" } });
            db.Movies.Add(movie);
            db.Actors.Add(new Actor { FirstName = "Unlinked" });
            db.SaveChanges();
        }

        var summary = await service.GetSummaryAsync(new MovieGridFilter(Text: "nothing matches this"));

        Assert.Equal(["Amy", "Zed"], summary.Actors.Select(a => a.Name));
    }
}
