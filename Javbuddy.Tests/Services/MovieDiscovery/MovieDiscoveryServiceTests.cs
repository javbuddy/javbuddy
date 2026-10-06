using Javbuddy.Models;
using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.MovieDiscovery;

public class MovieDiscoveryServiceTests
{
    private static DiscoveredMovieItem Item(
        string code,
        string title = "Title",
        IReadOnlyList<string>? galleryImageUrls = null,
        IReadOnlyList<string>? actressNames = null,
        bool isUpcoming = false,
        DateTime? releaseDate = null) =>
        new(code, title, "S1 NO.1 STYLE", "https://example.com/cover.jpg", galleryImageUrls ?? [], actressNames ?? [], releaseDate, isUpcoming);

    [Fact]
    public async Task RunScanAsync_NewCode_InsertsCandidateAsNew()
    {
        using var factory = new TestDbContextFactory();
        var source = Substitute.For<IStudioDiscoverySource>();
        source.SourceName.Returns("S1");
        source.ScanAsync(Arg.Any<CancellationToken>()).Returns([Item("SIVR-501", galleryImageUrls: ["https://example.com/one.jpg", "https://example.com/two.jpg"])]);

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([source], factory, movieAddService, TimeProvider.System);

        var result = await service.RunScanAsync();

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(0, result.AlreadyTrackedCount);

        await using var db = await factory.CreateDbContextAsync();
        var candidate = await db.DiscoveredMovieCandidates.SingleAsync();
        Assert.Equal("SIVR-501", candidate.Code);
        Assert.Equal("S1", candidate.SourceName);
        Assert.Equal(DiscoveredMovieStatus.New, candidate.Status);
        Assert.Equal(["https://example.com/one.jpg", "https://example.com/two.jpg"], candidate.GalleryImageUrls);
    }

    [Fact]
    public async Task RunScanAsync_CodeAlreadyInLibrary_MarksExistingCandidateAddedAndDoesNotCreateNew()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-501" });
            await db.SaveChangesAsync();
        }

        var source = Substitute.For<IStudioDiscoverySource>();
        source.SourceName.Returns("S1");
        source.ScanAsync(Arg.Any<CancellationToken>()).Returns([Item("SIVR-501")]);

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([source], factory, movieAddService, TimeProvider.System);

        var result = await service.RunScanAsync();

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(0, result.NewCount);
        Assert.Equal(1, result.AlreadyTrackedCount);

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.False(await db2.DiscoveredMovieCandidates.AnyAsync());
    }

    [Fact]
    public async Task RunScanAsync_SeenAgainAfterDismissed_DoesNotResetStatusToNew()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DiscoveredMovieCandidates.Add(new DiscoveredMovieCandidate
            {
                Code = "SIVR-501",
                Title = "Old title",
                Studio = "S1 NO.1 STYLE",
                SourceName = "S1",
                FirstSeenAt = DateTime.UtcNow.AddDays(-7),
                LastSeenAt = DateTime.UtcNow.AddDays(-7),
                Status = DiscoveredMovieStatus.Dismissed,
            });
            await db.SaveChangesAsync();
        }

        var source = Substitute.For<IStudioDiscoverySource>();
        source.SourceName.Returns("S1");
        source.ScanAsync(Arg.Any<CancellationToken>()).Returns([Item("SIVR-501", title: "New title")]);

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([source], factory, movieAddService, TimeProvider.System);

        var result = await service.RunScanAsync();

        Assert.Equal(0, result.NewCount);

        await using var db2 = await factory.CreateDbContextAsync();
        var candidate = await db2.DiscoveredMovieCandidates.SingleAsync();
        Assert.Equal(DiscoveredMovieStatus.Dismissed, candidate.Status);
        Assert.Equal("New title", candidate.Title); // metadata still refreshed
    }

    [Fact]
    public async Task RunScanAsync_OneSourceThrows_StillReturnsResultsFromOtherSources()
    {
        using var factory = new TestDbContextFactory();

        var failingSource = Substitute.For<IStudioDiscoverySource>();
        failingSource.SourceName.Returns("Broken");
        failingSource.ScanAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<DiscoveredMovieItem>>(_ => throw new InvalidOperationException("boom"));

        var workingSource = Substitute.For<IStudioDiscoverySource>();
        workingSource.SourceName.Returns("S1");
        workingSource.ScanAsync(Arg.Any<CancellationToken>()).Returns([Item("SIVR-501")]);

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([failingSource, workingSource], factory, movieAddService, TimeProvider.System);

        var result = await service.RunScanAsync();

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.NewCount);
    }

    [Fact]
    public async Task RunScanAsync_CancelledDuringASourceScan_PropagatesInsteadOfBeingLoggedAsASourceFailure()
    {
        using var factory = new TestDbContextFactory();
        using var cts = new CancellationTokenSource();

        var source = Substitute.For<IStudioDiscoverySource>();
        source.SourceName.Returns("S1");
        source.ScanAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<DiscoveredMovieItem>>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var service = new MovieDiscoveryService([source], factory, Substitute.For<IMovieAddService>(), TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunScanAsync(ct: cts.Token));
    }

    [Fact]
    public async Task GetCandidateSectionsAsync_OnlyReturnsNewOrderedByMostRecentReleaseDateFirstWithinSection()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DiscoveredMovieCandidates.AddRange(
                new DiscoveredMovieCandidate { Code = "A", Title = "A", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.New, ReleaseDate = new DateTime(2026, 12, 1) },
                new DiscoveredMovieCandidate { Code = "B", Title = "B", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.New, ReleaseDate = new DateTime(2026, 9, 1) },
                new DiscoveredMovieCandidate { Code = "C", Title = "C", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.New, ReleaseDate = null },
                new DiscoveredMovieCandidate { Code = "D", Title = "D", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.Dismissed, ReleaseDate = new DateTime(2026, 1, 1) },
                new DiscoveredMovieCandidate { Code = "E", Title = "E", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.Added, ReleaseDate = new DateTime(2026, 1, 1) });
            await db.SaveChangesAsync();
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        var sections = await service.GetCandidateSectionsAsync();

        var section = Assert.Single(sections);
        Assert.Equal("S1", section.SourceName);
        Assert.Equal(["A", "B", "C"], section.Candidates.Select(c => c.Candidate.Code));
    }

    [Fact]
    public async Task GetCandidateSectionsAsync_GroupsByStudioAndOrdersSectionsByFreshestReleaseFirst()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DiscoveredMovieCandidates.AddRange(
                new DiscoveredMovieCandidate { Code = "OLD-1", Title = "T", Studio = "Moodyz", SourceName = "Moodyz", Status = DiscoveredMovieStatus.New, ReleaseDate = new DateTime(2026, 1, 1) },
                new DiscoveredMovieCandidate { Code = "NEW-1", Title = "T", Studio = "S1 NO.1 STYLE", SourceName = "S1", Status = DiscoveredMovieStatus.New, ReleaseDate = new DateTime(2026, 9, 1) },
                new DiscoveredMovieCandidate { Code = "UPCOMING-1", Title = "T", Studio = "Undated Studio", SourceName = "Undated", Status = DiscoveredMovieStatus.New, ReleaseDate = null });
            await db.SaveChangesAsync();
        }

        var s1Source = Substitute.For<IStudioDiscoverySource>();
        s1Source.SourceName.Returns("S1");
        s1Source.Logo.Returns(new StudioLogo("/studio-logos/s1.png", 205, 161));

        var moodyzSource = Substitute.For<IStudioDiscoverySource>();
        moodyzSource.SourceName.Returns("Moodyz");
        moodyzSource.Logo.Returns((StudioLogo?)null);

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([s1Source, moodyzSource], factory, movieAddService, TimeProvider.System);

        var sections = await service.GetCandidateSectionsAsync();

        // Freshest known release first (S1 Sept > Moodyz Jan); a studio with only undated
        // candidates has no known release date, so it sorts last.
        Assert.Equal(["S1", "Moodyz", "Undated"], sections.Select(s => s.SourceName));
        Assert.Equal("S1 NO.1 STYLE", sections[0].StudioDisplayName);
        Assert.Equal(new StudioLogo("/studio-logos/s1.png", 205, 161), sections[0].Logo);
        Assert.Null(sections[1].Logo); // registered source with no logo configured
        Assert.Null(sections[2].Logo); // "Undated" isn't a registered source at all
    }

    [Fact]
    public async Task GetCandidateSectionsAsync_ResolvesActressNamesAgainstTrackedActors()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Tou", LastName = "Shiraishi", JapaneseNameKanji = "白石透羽" });
            db.DiscoveredMovieCandidates.Add(new DiscoveredMovieCandidate
            {
                Code = "SIVR-501",
                Title = "T",
                Studio = "S1",
                SourceName = "S1",
                Status = DiscoveredMovieStatus.New,
                ActressNames = ["白石透羽", "Someone Untracked"],
            });
            await db.SaveChangesAsync();
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        var sections = await service.GetCandidateSectionsAsync();

        var actresses = Assert.Single(Assert.Single(sections).Candidates).Actresses;
        Assert.Equal(2, actresses.Count);

        var tracked = actresses.Single(a => a.Name == "白石透羽");
        Assert.NotNull(tracked.ActorId);
        Assert.Equal("Shiraishi Tou", tracked.ActorDisplayName);

        var untracked = actresses.Single(a => a.Name == "Someone Untracked");
        Assert.Null(untracked.ActorId);
        Assert.Null(untracked.ActorDisplayName);
    }

    [Fact]
    public async Task AddCandidateAsync_CallsMovieAddServiceAndMarksCandidateAdded()
    {
        using var factory = new TestDbContextFactory();
        int candidateId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var candidate = new DiscoveredMovieCandidate { Code = "SIVR-501", Title = "T", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.New };
            db.DiscoveredMovieCandidates.Add(candidate);
            await db.SaveChangesAsync();
            candidateId = candidate.Id;
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("SIVR-501", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Code = "SIVR-501" }, false, true, null));

        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        await service.AddCandidateAsync(candidateId);

        await movieAddService.Received(1).AddAsync("SIVR-501", Arg.Any<CancellationToken>());

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.DiscoveredMovieCandidates.SingleAsync(c => c.Id == candidateId);
        Assert.Equal(DiscoveredMovieStatus.Added, updated.Status);
    }

    [Fact]
    public async Task AddCandidateAsync_MetadataNotFound_BackfillsMovieFromCandidateData()
    {
        using var factory = new TestDbContextFactory();
        int candidateId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var candidate = new DiscoveredMovieCandidate
            {
                Code = "SIVR-501",
                Title = "Candidate Title",
                Studio = "S1 NO.1 STYLE",
                SourceName = "S1",
                CoverImageUrl = "https://example.com/cover.jpg",
                ReleaseDate = new DateTime(2026, 12, 1),
                ActressNames = ["Actress One", "Actress Two"],
                Status = DiscoveredMovieStatus.New,
            };
            db.DiscoveredMovieCandidates.Add(candidate);

            // Simulates what the real IMovieAddService.AddAsync already persisted before
            // returning MetadataFound: false — a bare Movie row with no metadata yet.
            var movie = new Movie { Code = "SIVR-501" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            candidateId = candidate.Id;
            movieId = movie.Id;
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("SIVR-501", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Id = movieId, Code = "SIVR-501" }, false, false, "no metadata found"));

        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        await service.AddCandidateAsync(candidateId);

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Candidate Title", updated.MetaTitle);
        Assert.Equal("S1 NO.1 STYLE", updated.MetaStudio);
        Assert.Equal(new DateTime(2026, 12, 1), updated.MetaReleaseDate);
        Assert.Equal("https://example.com/cover.jpg", updated.MetaCoverUrl);
        Assert.Equal("https://example.com/cover.jpg", updated.MetaBackdropUrl);
        Assert.Equal("Actress One, Actress Two", updated.MetaActresses);
        Assert.Equal("S1", updated.MetaSourceName);
        Assert.NotNull(updated.MetaFetchedAt);
    }

    [Fact]
    public async Task AddCandidateAsync_CandidateOnlyHasActresses_LinksTrackedActressesAndDoesNotLinkMaleActors()
    {
        using var factory = new TestDbContextFactory();
        int candidateId;
        int movieId;
        int femaleActorId;
        int maleActorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var female = new Actor { FirstName = "Tou", LastName = "Shiraishi", JapaneseNameKanji = "白石透羽" };
            var male = new Actor { FirstName = "Ken", LastName = "Shimizu", JapaneseNameKanji = "しみけん" };
            db.Actors.AddRange(female, male);
            await db.SaveChangesAsync();
            femaleActorId = female.Id;
            maleActorId = male.Id;

            var candidate = new DiscoveredMovieCandidate
            {
                Code = "SIVR-501",
                Title = "Candidate Title",
                Studio = "S1 NO.1 STYLE",
                SourceName = "S1",
                // Discovery scraper specifically extracted only female actresses and excluded male actors:
                ActressNames = ["白石透羽"],
                Status = DiscoveredMovieStatus.New,
            };
            db.DiscoveredMovieCandidates.Add(candidate);

            var movie = new Movie { Code = "SIVR-501" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            candidateId = candidate.Id;
            movieId = movie.Id;
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("SIVR-501", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Id = movieId, Code = "SIVR-501" }, false, false, "no metadata found"));

        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        await service.AddCandidateAsync(candidateId);

        await using var db2 = await factory.CreateDbContextAsync();
        var links = await db2.MovieActors.Where(ma => ma.MovieId == movieId).ToListAsync();
        var linkedActorId = Assert.Single(links).ActorId;
        Assert.Equal(femaleActorId, linkedActorId);
        Assert.DoesNotContain(links, l => l.ActorId == maleActorId);

        var movieResult = await db2.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("白石透羽", movieResult.MetaActresses);
        Assert.False(movieResult.HasUnmatchedActors);
    }

    [Fact]
    public async Task AddCandidateAsync_MetadataFound_DoesNotOverwriteRealMetadataWithCandidateData()
    {
        using var factory = new TestDbContextFactory();
        int candidateId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var candidate = new DiscoveredMovieCandidate
            {
                Code = "SIVR-501",
                Title = "Candidate Title",
                Studio = "S1",
                SourceName = "S1",
                Status = DiscoveredMovieStatus.New,
            };
            db.DiscoveredMovieCandidates.Add(candidate);

            var movie = new Movie { Code = "SIVR-501", MetaTitle = "Real javinizer-go Title" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            candidateId = candidate.Id;
            movieId = movie.Id;
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("SIVR-501", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Id = movieId, Code = "SIVR-501" }, false, true, null));

        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        await service.AddCandidateAsync(candidateId);

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Real javinizer-go Title", updated.MetaTitle);
    }

    [Fact]
    public async Task DismissCandidateAsync_MarksCandidateDismissed()
    {
        using var factory = new TestDbContextFactory();
        int candidateId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var candidate = new DiscoveredMovieCandidate { Code = "SIVR-501", Title = "T", Studio = "S1", SourceName = "S1", Status = DiscoveredMovieStatus.New };
            db.DiscoveredMovieCandidates.Add(candidate);
            await db.SaveChangesAsync();
            candidateId = candidate.Id;
        }

        var movieAddService = Substitute.For<IMovieAddService>();
        var service = new MovieDiscoveryService([], factory, movieAddService, TimeProvider.System);

        await service.DismissCandidateAsync(candidateId);

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.DiscoveredMovieCandidates.SingleAsync(c => c.Id == candidateId);
        Assert.Equal(DiscoveredMovieStatus.Dismissed, updated.Status);
    }
}
