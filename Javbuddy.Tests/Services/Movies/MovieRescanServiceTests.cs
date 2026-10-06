using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class MovieRescanServiceTests
{
    private static MovieRescanService CreateService(
        TestDbContextFactory factory,
        ILocalLibraryClient? localLibraryClient = null,
        IJavinizerClient? javinizerClient = null,
        IJellyfinClient? jellyfinClient = null,
        INfoSyncService? nfoSyncService = null,
        ITrickplayTrigger? trickplayTrigger = null,
        MovieChangeNotifier? movieChangeNotifier = null,
        TaskActivityTracker? activities = null)
    {
        if (localLibraryClient is null)
        {
            localLibraryClient = Substitute.For<ILocalLibraryClient>();
            localLibraryClient.TryGetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), false)
                .Returns(new LocalLookupResult(false, null, null, null));
            localLibraryClient.RefreshMediaInfoOnlyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new LocalRefreshResult(true, null));
            localLibraryClient.SyncImagesSignatureAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new LocalRefreshResult(true, null));
            localLibraryClient.AnyRootReachableAsync(Arg.Any<CancellationToken>())
                .Returns(true);
        }

        if (javinizerClient is null)
        {
            javinizerClient = Substitute.For<IJavinizerClient>();
            javinizerClient.GetBaseUrlAsync(Arg.Any<CancellationToken>())
                .Returns((string?)null);
            javinizerClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new JavinizerScrapeResult(false, null, "not configured"));
        }

        if (jellyfinClient is null)
        {
            jellyfinClient = Substitute.For<IJellyfinClient>();
            jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>())
                .Returns(false);
            jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>())
                .Returns(new List<string>());
            jellyfinClient.LookupInSelectedLibrariesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null));
        }

        if (nfoSyncService is null)
        {
            nfoSyncService = Substitute.For<INfoSyncService>();
            nfoSyncService.CheckMovieNfoConflictAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ci => new ActorNfoConflictCheckResult(ci.ArgAt<int>(0), "", false, null));
        }

        return new MovieRescanService(
            factory,
            localLibraryClient,
            javinizerClient,
            jellyfinClient,
            nfoSyncService,
            trickplayTrigger ?? Substitute.For<ITrickplayTrigger>(),
            movieChangeNotifier ?? new MovieChangeNotifier(),
            NullLogger<MovieRescanService>.Instance,
            activities);
    }

    [Fact]
    public async Task RescanAsync_UnknownMovieId_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.RescanAsync(999);

        Assert.False(result.Success);
        Assert.Equal("Movie not found.", result.Message);
    }

    [Fact]
    public async Task RescanAsync_LocalFound_ProbesMediaInfoAndUpdatesStatusGot()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(true, "/media/ABC-123", new LocalMovieMetadata { VideoFileName = "ABC-123.mp4", VideoFileSizeBytes = 1000 }, null));
        localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await using var db = await factory.CreateDbContextAsync();
                var m = await db.Movies.FindAsync([movieId]);
                if (m is not null)
                {
                    m.LocalFileSizeBytes = 1000;
                    m.MediaVideoFileName = "ABC-123.mp4";
                    await db.SaveChangesAsync();
                }
                return new LocalRefreshResult(true, null);
            });
        localLibraryClient.SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));

        var service = CreateService(factory, localLibraryClient: localLibraryClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.LocalFound);
        Assert.True(result.MediaInfoProbed);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(MovieStatus.Got, updated.Status);
        Assert.Equal(1000, updated.LocalFileSizeBytes);
        Assert.Equal("ABC-123.mp4", updated.MediaVideoFileName);
    }

    [Fact]
    public async Task RescanAsync_LocalFound_QueuesMissingTrickplay()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, LocalFileSizeBytes = 1000 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(true, "/media/ABC-123", new LocalMovieMetadata { VideoFileName = "ABC-123.mp4", VideoFileSizeBytes = 1000 }, null));
        localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        localLibraryClient.SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        var trickplayTrigger = Substitute.For<ITrickplayTrigger>();
        trickplayTrigger.OnRescanAsync(movieId, Arg.Any<CancellationToken>()).Returns(true);

        var service = CreateService(factory, localLibraryClient: localLibraryClient, trickplayTrigger: trickplayTrigger);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.TrickplayQueued);
        await trickplayTrigger.Received(1).OnRescanAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescanAsync_NoLocalFile_DoesNotQueueTrickplay()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        var trickplayTrigger = Substitute.For<ITrickplayTrigger>();

        var result = await CreateService(factory, trickplayTrigger: trickplayTrigger).RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.False(result.TrickplayQueued);
        await trickplayTrigger.DidNotReceive().OnRescanAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescanAsync_UnpopulatedMovie_AppliesDescriptiveMetadataFromLocalNfo()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localMetadata = new LocalMovieMetadata
        {
            Title = "Local Title",
            Plot = "Local Plot",
            Actresses = ["Yua Mikami"],
            VideoFileName = "ABC-123.mp4",
            VideoFileSizeBytes = 5000
        };

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(true, "/media/ABC-123", localMetadata, null));
        localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        localLibraryClient.SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));

        var service = CreateService(factory, localLibraryClient: localLibraryClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.NfoApplied);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Local Title", updated.MetaTitle);
        Assert.Equal("Local Plot", updated.MetaDescription);
        Assert.Equal("Local", updated.MetaSourceName);
        Assert.Equal("Yua Mikami", updated.MetaActresses);
    }

    [Fact]
    public async Task RescanAsync_MissingMovieTransitionsToGot_OverwritesExistingMetadataFromNfo()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing,
                MetaTitle = "ABC-123 Old Javinizer Title",
                MetaSourceName = "javinizer-go",
                MetaActresses = "Old Actress"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localMetadata = new LocalMovieMetadata
        {
            Title = "Clean Nfo Title",
            Plot = "Nfo Plot",
            Actresses = ["New Actress"],
            VideoFileName = "ABC-123.mp4",
            VideoFileSizeBytes = 5000
        };

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(true, "/media/ABC-123", localMetadata, null));
        localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await using var db = await factory.CreateDbContextAsync();
                var m = await db.Movies.FindAsync([movieId]);
                if (m is not null)
                {
                    m.LocalFileSizeBytes = 5000;
                    m.MediaVideoFileName = "ABC-123.mp4";
                    await db.SaveChangesAsync();
                }
                return new LocalRefreshResult(true, null);
            });
        localLibraryClient.SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));

        var service = CreateService(factory, localLibraryClient: localLibraryClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.NfoApplied);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(MovieStatus.Got, updated.Status);
        Assert.Equal("Clean Nfo Title", updated.MetaTitle);
        Assert.Equal("Nfo Plot", updated.MetaDescription);
        Assert.Equal("New Actress", updated.MetaActresses);
        Assert.Equal("Local", updated.MetaSourceName);
    }

    [Fact]
    public async Task RescanAsync_AuthoritativeJavbuddyMetadata_PreservesExistingMetadataWhenNfoExists()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaTitle = "Authoritative In Javbuddy",
                MetaSourceName = "javinizer-go",
                MetaActresses = "Original Actress"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localMetadata = new LocalMovieMetadata
        {
            Title = "Different Title From Nfo",
            Plot = "Different Plot",
            Actresses = ["Different Actress"]
        };

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(true, "/media/ABC-123", localMetadata, null));
        localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        localLibraryClient.SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));

        var actorNfoSync = Substitute.For<INfoSyncService>();
        var service = CreateService(factory, localLibraryClient: localLibraryClient, nfoSyncService: actorNfoSync);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.False(result.NfoApplied);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Authoritative In Javbuddy", updated.MetaTitle);
        Assert.Equal("Original Actress", updated.MetaActresses);
        await actorNfoSync.Received(1).CheckMovieNfoConflictAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescanAsync_MissingLocalFolder_WhenRootReachable_RevertsGotMovieToMissingAndClearsFileData()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                LocalFileSizeBytes = 9999,
                MediaVideoFileName = "old.mp4"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(false, null, null, null));
        localLibraryClient.AnyRootReachableAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var service = CreateService(factory, localLibraryClient: localLibraryClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.False(result.LocalFound);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(MovieStatus.Missing, updated.Status);
        Assert.Null(updated.LocalFileSizeBytes);
        Assert.Null(updated.MediaVideoFileName);
    }

    [Fact]
    public async Task RescanAsync_LocalMetadataPresent_DoesNotQueryJavinizerGo()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaTitle = "Local Title",
                MetaSourceName = "Local"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetBaseUrlAsync(Arg.Any<CancellationToken>())
            .Returns("http://javinizer:8080");

        var service = CreateService(factory, javinizerClient: javinizerClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.False(result.ExternalMetadataApplied);
        await javinizerClient.DidNotReceive().ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescanAsync_NonLocalMetadata_JavinizerConfigured_QueriesAndAppliesJavinizerMetadata()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing,
                MetaSourceName = "javinizer-go"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetBaseUrlAsync(Arg.Any<CancellationToken>())
            .Returns("http://javinizer:8080");
        javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(true, new MovieViewDto
            {
                Title = "Scraped Title",
                Description = "Scraped Plot",
                Maker = "Scraped Maker",
                SourceName = "javinizer-go",
                Actresses = [new ActressViewDto { FirstName = "Yua", LastName = "Mikami" }]
            }, null));

        var service = CreateService(factory, javinizerClient: javinizerClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.ExternalMetadataApplied);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Scraped Title", updated.MetaTitle);
        Assert.Equal("Scraped Plot", updated.MetaDescription);
        Assert.Equal("Scraped Maker", updated.MetaStudio);
        Assert.Equal("Mikami Yua", updated.MetaActresses);
    }

    [Fact]
    public async Task RescanAsync_JavinizerGenreNameContainsAComma_ResolvesToOneTag()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing,
                MetaSourceName = "javinizer-go"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetBaseUrlAsync(Arg.Any<CancellationToken>())
            .Returns("http://javinizer:8080");
        javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(true, new MovieViewDto
            {
                Title = "Scraped Title",
                SourceName = "javinizer-go",
                Genres = [new GenreViewDto { Name = "Nasty, hardcore" }, new GenreViewDto { Name = "VR" }]
            }, null));

        var service = CreateService(factory, javinizerClient: javinizerClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.ExternalMetadataApplied);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var tags = await verifyDb.Tags.OrderBy(t => t.Name).ToListAsync();
        Assert.Equal(["Nasty, hardcore", "VR"], tags.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task RescanAsync_SynchronizesActorRelationships()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);

            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing,
                MetaActresses = "Yua Mikami",
                MetaSourceName = "Local"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
            actorId = actor.Id;
        }

        var service = CreateService(factory);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.ActorsSynchronized);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var link = await verifyDb.MovieActors.FirstOrDefaultAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId);
        Assert.NotNull(link);
    }

    [Fact]
    public async Task RescanAsync_JellyfinEnabledAndMatched_LinksMovieAndMarksGot()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "JAV" });
        jellyfinClient.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, new List<MediaServerItemDto>
            {
                new JellyfinItemDto { Id = "jf-123", ServerId = "srv-456", LibraryName = "JAV" }
            }, null));

        var service = CreateService(factory, jellyfinClient: jellyfinClient);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(result.JellyfinLinked);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updated = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(MovieStatus.Got, updated.Status);
        Assert.Equal("jf-123", updated.JellyfinItemId);
        Assert.Equal("srv-456", updated.JellyfinServerId);
        Assert.Equal("JAV", updated.JellyfinLibraryName);
        Assert.NotNull(updated.JellyfinCheckedAt);
    }

    [Fact]
    public async Task RescanAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = CreateService(factory, movieChangeNotifier: notifier);
        var result = await service.RescanAsync(movieId);

        Assert.True(result.Success);
        Assert.True(notified);
    }
}
