using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

public class ActorDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverFromMoviesAsync_DiscoversNewActorsWithJapaneseKanjiAndDownloadsRemoteImageAtImportTime()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Araki Noa",
                    AltName = "新木希空",
                    Thumb = "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg"
                }
            ]);
        localLibraryClient.ResolveActorImagePathAsync("SIVR-505", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));
        imageCacheService.DownloadAndCacheRemoteImageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("/cache/thumb.webp", "/cache/full.webp"));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);
        var result = await service.DiscoverFromMoviesAsync();

        Assert.Equal(1, result.Found);
        Assert.Equal(0, result.AlreadyTracked);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.ImagesFound);
        Assert.Equal(["Araki Noa"], result.DiscoveredNames);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.MovieActors).SingleAsync();
        Assert.Equal("Noa", actor.FirstName);
        Assert.Equal("Araki", actor.LastName);
        Assert.Equal("新木希空", actor.JapaneseNameKanji);

        await imageCacheService.Received(1).DownloadAndCacheRemoteImageAsync(actor.Id, "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg", Arg.Any<CancellationToken>());

        var link = Assert.Single(actor.MovieActors);
        Assert.Equal("SIVR-505", (await verifyDb.Movies.FindAsync(link.MovieId))!.Code);
    }

    [Fact]
    public async Task DiscoverFromMoviesAsync_DoesNotDuplicateExistingActorMatchedByJapaneseName_AndEnrichesMissingMetadata()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Noa", LastName = "Araki" }); // Missing Kanji
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "新木希空" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "新木希空",
                    AltName = "Araki Noa",
                    Thumb = "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg"
                }
            ]);
        localLibraryClient.ResolveActorImagePathAsync("SIVR-505", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);
        var result = await service.DiscoverFromMoviesAsync();

        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.AlreadyTracked);
        Assert.Equal(0, result.Added);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.MovieActors).SingleAsync();
        Assert.Equal("Noa", actor.FirstName);
        Assert.Equal("Araki", actor.LastName);
        Assert.Equal("新木希空", actor.JapaneseNameKanji);
        Assert.Single(actor.MovieActors);
    }

    [Fact]
    public async Task DiscoverFromMoviesAsync_DoesNotDownloadRemoteImage_WhenLocalActorImageFileExists()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Araki Noa",
                    Thumb = "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg"
                }
            ]);
        // A local file exists in .actors/
        localLibraryClient.ResolveActorImagePathAsync("SIVR-505", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("/media/SIVR-505/.actors/Araki Noa.jpg");

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(("/cache/thumb.webp", "/cache/full.webp"));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);
        var result = await service.DiscoverFromMoviesAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.ImagesFound);

        // Remote download should NOT be called when local image exists
        await imageCacheService.DidNotReceive().DownloadAndCacheRemoteImageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscoverFromMoviesAsync_MatchesExistingActorByAlias_DoesNotCreateDuplicate()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Alternate Stage Name" });
            db.Actors.Add(actor);
            db.Movies.Add(new Movie { Code = "MOV-999", MetaActresses = "Alternate Stage Name" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("MOV-999", Arg.Any<CancellationToken>())
            .Returns([]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);
        var result = await service.DiscoverFromMoviesAsync();

        Assert.Equal(1, result.AlreadyTracked);
        Assert.Equal(0, result.Added);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actors = await verifyDb.Actors.ToListAsync();
        Assert.Single(actors);
        Assert.Equal("Yua", actors[0].FirstName);
        Assert.Equal("Mikami", actors[0].LastName);
    }

    [Fact]
    public async Task ScanFromMoviesAsync_ReturnsCandidates_WithoutWritingToDatabase()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Araki Noa",
                    AltName = "新木希空",
                    Thumb = "https://example.com/thumb.jpg"
                }
            ]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var scan = await service.ScanFromMoviesAsync();

        Assert.Equal(1, scan.TotalFound);
        Assert.Equal(0, scan.AlreadyTracked);
        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal("Araki Noa", candidate.DisplayName);
        Assert.Equal("新木希空", candidate.JapaneseKanji);

        // Verify DB was NOT mutated with new actors
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Empty(await verifyDb.Actors.ToListAsync());
    }

    [Fact]
    public async Task ImportCandidatesAsync_ImportsOnlySelectedCandidates()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa, Second Actress" });
            await db.SaveChangesAsync();
        }

        var candidate1 = DiscoveredActorCandidate.FromName("Araki Noa", "SIVR-505");
        candidate1.JapaneseKanji = "新木希空";
        candidate1.Thumb = "https://example.com/thumb.jpg";

        var candidate2 = DiscoveredActorCandidate.FromName("Second Actress", "SIVR-505");

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        // Import only candidate 1
        var importResult = await service.ImportCandidatesAsync([candidate1]);

        Assert.Equal(1, importResult.ImportedCount);
        Assert.Equal(["Araki Noa"], importResult.ImportedNames);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actors = await verifyDb.Actors.ToListAsync();
        var imported = Assert.Single(actors);
        Assert.Equal("Araki Noa", imported.DisplayName);
        Assert.Equal("新木希空", imported.JapaneseNameKanji);
    }

    [Fact]
    public async Task ImportCandidatesAsync_ContinuesAfterDuplicateNameFailure_AndReportsTheRejectedActor()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);
        var result = await service.ImportCandidatesAsync([
            DiscoveredActorCandidate.FromName("Before Actor", null),
            DiscoveredActorCandidate.FromName("Duplicate Actor", null),
            DiscoveredActorCandidate.FromName("Duplicate Actor ", null),
            DiscoveredActorCandidate.FromName("After Actor", null)
        ]);

        Assert.Equal(3, result.ImportedCount);
        Assert.Equal(["Before Actor", "Duplicate Actor", "After Actor"], result.ImportedNames);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("Duplicate Actor", failure.ActorName);
        Assert.Equal("An actor with the same first and last name already exists.", failure.Reason);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actorNames = (await verifyDb.Actors.OrderBy(actor => actor.Id).ToListAsync())
            .Select(actor => actor.DisplayName)
            .ToList();
        Assert.Equal(["Before Actor", "Duplicate Actor", "After Actor"], actorNames);
    }

    [Fact]
    public async Task ScanFromMoviesAsync_WhenLocalHeadshotExists_PopulatesHasLocalHeadshotAndLocalHeadshotMovieCode()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Araki Noa",
                    Thumb = ".actors/Araki Noa.jpg"
                }
            ]);
        localLibraryClient.ResolveActorImagePathAsync("SIVR-505", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("/path/to/SIVR-505/.actors/Araki Noa.jpg");

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var scan = await service.ScanFromMoviesAsync();

        var candidate = Assert.Single(scan.Candidates);
        Assert.True(candidate.HasLocalHeadshot);
        Assert.Equal("SIVR-505", candidate.LocalHeadshotMovieCode);
        Assert.True(candidate.HasImage);
        Assert.Equal("Local (.nfo)", candidate.Source);
    }

    [Fact]
    public async Task ScanAsync_WithLocalLibrarySource_ReturnsLocalLibraryCandidates()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SIVR-505", MetaActresses = "Araki Noa" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SIVR-505", Arg.Any<CancellationToken>())
            .Returns([]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var scan = await service.ScanAsync(ActorDiscoverySource.LocalLibrary);

        Assert.Equal(1, scan.TotalFound);
        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal("Araki Noa", candidate.DisplayName);
        Assert.Equal("Local (cast)", candidate.Source);
    }

    [Fact]
    public async Task ImportCandidatesAsync_DoesNotCreateAnyActorAliases()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var candidate = DiscoveredActorCandidate.FromMetadata(new LocalActorMetadata
        {
            Name = "Araki Noa",
            AltName = "新木希空",
            Aliases = ["Spurious Alias", "Other Name"]
        }, "SIVR-505");

        var result = await service.ImportCandidatesAsync([candidate]);

        Assert.Equal(1, result.ImportedCount);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).SingleAsync();
        Assert.Equal("Araki Noa", actor.DisplayName);
        Assert.Empty(actor.Aliases);
        Assert.Equal(0, await verifyDb.ActorAliases.CountAsync());
    }

    [Fact]
    public async Task ScanFromMoviesAsync_DoesNotMergeCandidatesWithDifferentLatinNamesEvenWithDuplicateJapaneseAltName()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "MOV-001" });
            db.Movies.Add(new Movie { Code = "MOV-002" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("MOV-001", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Hashimoto Arina",
                    AltName = "橋本ありな"
                }
            ]);
        localLibraryClient.GetMovieActorsAsync("MOV-002", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Yamagishi Ayaka",
                    AltName = "橋本ありな"
                }
            ]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var scan = await service.ScanFromMoviesAsync();

        Assert.Equal(2, scan.TotalFound);
        Assert.Equal(2, scan.Candidates.Count);

        var c1 = Assert.Single(scan.Candidates, c => c.DisplayName == "Hashimoto Arina");
        var c2 = Assert.Single(scan.Candidates, c => c.DisplayName == "Yamagishi Ayaka");
        Assert.Equal(["MOV-001"], c1.MovieCodes.ToList());
        Assert.Equal(["MOV-002"], c2.MovieCodes.ToList());
    }

    [Fact]
    public async Task ScanFromMoviesAsync_ConsolidatesCandidatesWithReversedLatinNames()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "MOV-001" });
            db.Movies.Add(new Movie { Code = "MOV-002" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("MOV-001", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata { Name = "Kana Yura" }
            ]);
        localLibraryClient.GetMovieActorsAsync("MOV-002", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata { Name = "Yura Kana" }
            ]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var scan = await service.ScanFromMoviesAsync();

        Assert.Equal(1, scan.TotalFound);
        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal(2, candidate.MovieCount);
        Assert.Contains("MOV-001", candidate.MovieCodes);
        Assert.Contains("MOV-002", candidate.MovieCodes);
    }

    [Fact]
    public async Task ScanFromMoviesAsync_ReportsProgressAcrossPhases()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "MOV-001", MetaActresses = "Actor One" });
            db.Movies.Add(new Movie { Code = "MOV-002", MetaActresses = "Actor Two" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var progressReports = new List<TaskProgress>();
        var progress = new ImmediateProgress<TaskProgress>(progressReports.Add);

        var result = await service.ScanFromMoviesAsync(progress);

        Assert.Equal(2, result.TotalFound);
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Stage == "Scanning movies");
        Assert.Contains(progressReports, p => p.Stage == "Consolidating candidates");
        Assert.Contains(progressReports, p => p.Stage == "Resolving headshots");
        Assert.Contains(progressReports, p => p.Stage == "Scan complete");
    }

    [Fact]
    public async Task ScanFromMoviesAsync_ThrowsWhenCancelled()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "MOV-001", MetaActresses = "Actor One" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ScanFromMoviesAsync(null, cts.Token));
    }

    [Fact]
    public async Task ImportCandidatesAsync_ReportsProgressAcrossPhases()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        var progressReports = new List<TaskProgress>();
        var progress = new ImmediateProgress<TaskProgress>(progressReports.Add);

        var candidates = new List<DiscoveredActorCandidate>
        {
            DiscoveredActorCandidate.FromName("Actor One", "MOV-001"),
            DiscoveredActorCandidate.FromName("Actor Two", "MOV-002")
        };

        var result = await service.ImportCandidatesAsync(candidates, progress);

        Assert.Equal(2, result.ImportedCount);
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Stage == "Importing actors");
        Assert.Contains(progressReports, p => p.Stage == "Importing headshots");
        Assert.Contains(progressReports, p => p.Stage == "Import complete");
    }

    [Fact]
    public async Task ImportCandidatesAsync_ThrowsWhenCancelled_AndPreservesPriorCommittedActors()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var imageCacheService = Substitute.For<IActorImageCacheService>();

        var service = new ActorDiscoveryService(factory, localLibraryClient, imageCacheService);

        using var cts = new CancellationTokenSource();

        var progressMock = Substitute.For<IProgress<TaskProgress>>();
        progressMock.When(p => p.Report(Arg.Is<TaskProgress>(tp => tp.Current == 2 && tp.Stage == "Importing actors")))
            .Do(_ => cts.Cancel());

        var candidates = new List<DiscoveredActorCandidate>
        {
            DiscoveredActorCandidate.FromName("First Actor", "MOV-001"),
            DiscoveredActorCandidate.FromName("Second Actor", "MOV-002")
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ImportCandidatesAsync(candidates, progressMock, cts.Token));

        await using var verifyDb = await factory.CreateDbContextAsync();
        var actors = await verifyDb.Actors.ToListAsync();
        Assert.Single(actors);
        Assert.Equal("First Actor", actors[0].DisplayName);
    }
}
