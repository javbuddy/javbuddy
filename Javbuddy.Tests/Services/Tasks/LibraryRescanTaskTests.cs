using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class LibraryRescanTaskTests
{
    private static ILocalLibraryClient CreateLocalLibraryClient(
        bool anyRootReachable = true,
        List<string>? folderCodes = null,
        Func<string, LocalLookupResult>? metadataLookup = null)
    {
        var client = Substitute.For<ILocalLibraryClient>();
        client.AnyRootReachableAsync(Arg.Any<CancellationToken>()).Returns(anyRootReachable);
        client.ListMovieCodesAsync(Arg.Any<CancellationToken>()).Returns(folderCodes ?? []);
        client.TryGetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => metadataLookup?.Invoke(ci.ArgAt<string>(0)) ?? new LocalLookupResult(false, null, null, null));
        client.RefreshMediaInfoOnlyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        client.RefreshSubtitleFlagOnlyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        client.RefreshTrailerFlagOnlyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        client.RefreshLocalMetadataIfNfoChangedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        client.SyncImagesSignatureAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));
        return client;
    }

    private static IProgress<TaskProgress> NoopProgress => new Progress<TaskProgress>();

    [Fact]
    public async Task RunAsync_NewFolderNotTracked_ImportsAsGotAndAppliesLocalMetadata()
    {
        using var factory = new TestDbContextFactory();
        var client = CreateLocalLibraryClient(
            folderCodes: ["ABC-123"],
            metadataLookup: code => new LocalLookupResult(true, "/media/" + code, new LocalMovieMetadata { Title = "Discovered Title" }, null));

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal("ABC-123", movie.Code);
        Assert.Equal(MovieStatus.Got, movie.Status);
        Assert.Equal("Discovered Title", movie.MetaTitle);
        Assert.Contains("1 new movie(s) imported", summary);
    }

    [Fact]
    public async Task RunAsync_NewFolderPreviouslyDeleted_ClearsDeletionHistory()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await DeletedMovieHistory.RecordAsync(db, new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            await DeletedMovieHistory.RecordAsync(db, new Movie { Code = "XYZ-999", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var client = CreateLocalLibraryClient(folderCodes: ["abc123"]);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal("abc123", (await verifyDb.Movies.SingleAsync()).Code);
        Assert.Equal("XYZ-999", (await verifyDb.DeletedMovies.SingleAsync()).Code);
    }

    [Fact]
    public async Task RunAsync_FolderAlreadyTracked_IsNotReimported()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
        }

        var client = CreateLocalLibraryClient(folderCodes: ["ABC-123"]);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verifyDb.Movies.CountAsync());
        Assert.Equal(MovieStatus.Missing, (await verifyDb.Movies.SingleAsync()).Status);
        Assert.Contains("0 new movie(s) imported", summary);
    }

    [Fact]
    public async Task RunAsync_GotMovieFolderNoLongerFound_RevertsToMissingAndClearsLocalFields()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaTitle = "Still here, descriptively",
                LocalFileSizeBytes = 5_000_000_000,
                MediaWidth = 1920,
                MediaHeight = 1080,
                MediaHasSubtitleFile = true,
                MediaScannedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // The movie's folder no longer shows up in the current listing.
        var client = CreateLocalLibraryClient(folderCodes: []);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var movie = await verifyDb.Movies.SingleAsync();
        Assert.Equal(MovieStatus.Missing, movie.Status);
        Assert.Null(movie.LocalFileSizeBytes);
        Assert.Null(movie.MediaWidth);
        Assert.Null(movie.MediaHeight);
        Assert.False(movie.MediaHasSubtitleFile);
        Assert.Null(movie.MediaScannedAt);
        // Descriptive metadata isn't tied to the file's presence — left alone.
        Assert.Equal("Still here, descriptively", movie.MetaTitle);
        Assert.Contains("1 movie(s) reverted to Missing", summary);
    }

    [Fact]
    public async Task RunAsync_GotMovieFolderStillPresent_IsNotReverted()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var client = CreateLocalLibraryClient(folderCodes: ["ABC-123"]);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(MovieStatus.Got, (await verifyDb.Movies.SingleAsync()).Status);
    }

    [Fact]
    public async Task RunAsync_NoRootReachable_SkipsImportAndRevert_ButStillProbes()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        // If this were treated as "an empty library", the Got movie above would be wrongly reverted.
        var client = CreateLocalLibraryClient(anyRootReachable: false, folderCodes: []);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(MovieStatus.Got, (await verifyDb.Movies.SingleAsync()).Status);
        Assert.Contains("no configured local library root is currently reachable", summary);
        await client.DidNotReceive().ListMovieCodesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CallsMediaInfoAndNfoAndImagesSyncForEveryGotMovie_NotMissing()
    {
        // The retry/change-eligibility decisions (has the video/nfo/images actually changed since
        // last time?) now live inside LocalLibraryClient's own methods — see LocalLibraryClientTests
        // — not as a query-level filter here. The task's only job is: call these for every Got
        // movie, regardless of prior scan state, and never for a Missing movie (no local file to
        // check at all).
        using var factory = new TestDbContextFactory();
        int gotId, missingId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var got = new Movie { Code = "AAA-001", Status = MovieStatus.Got, MediaScannedAt = DateTime.UtcNow };
            var missing = new Movie { Code = "AAA-002", Status = MovieStatus.Missing };
            db.Movies.AddRange(got, missing);
            await db.SaveChangesAsync();
            gotId = got.Id;
            missingId = missing.Id;
        }

        var client = CreateLocalLibraryClient(folderCodes: ["AAA-001", "AAA-002"]);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        await client.Received(1).RefreshMediaInfoOnlyAsync(gotId, Arg.Any<CancellationToken>());
        await client.Received(1).RefreshTrailerFlagOnlyAsync(gotId, Arg.Any<CancellationToken>());
        await client.Received(1).RefreshLocalMetadataIfNfoChangedAsync(gotId, Arg.Any<CancellationToken>());
        await client.Received(1).SyncImagesSignatureAsync(gotId, Arg.Any<CancellationToken>());
        await client.DidNotReceive().RefreshMediaInfoOnlyAsync(missingId, Arg.Any<CancellationToken>());
        await client.DidNotReceive().RefreshTrailerFlagOnlyAsync(missingId, Arg.Any<CancellationToken>());
        await client.DidNotReceive().RefreshLocalMetadataIfNfoChangedAsync(missingId, Arg.Any<CancellationToken>());
        await client.DidNotReceive().SyncImagesSignatureAsync(missingId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SyncsSubtitleFlagForEveryGotMovie_EvenOneAlreadySuccessfullyScanned()
    {
        // The bug this test guards against: a movie whose MediaInfo probe already succeeded before
        // subtitle detection existed (MediaScannedAt set, no error) was never getting its subtitle
        // flag rechecked, because that only ran inside RefreshMediaInfoOnlyAsync — which is gated to
        // movies still eligible for a MediaInfo retry. Subtitle syncing must not share that gate.
        using var factory = new TestDbContextFactory();
        int alreadyScannedId, missingId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var alreadyScanned = new Movie { Code = "AAA-001", Status = MovieStatus.Got, MediaScannedAt = DateTime.UtcNow };
            var missing = new Movie { Code = "AAA-002", Status = MovieStatus.Missing };
            db.Movies.AddRange(alreadyScanned, missing);
            await db.SaveChangesAsync();
            alreadyScannedId = alreadyScanned.Id;
            missingId = missing.Id;
        }

        var client = CreateLocalLibraryClient(folderCodes: ["AAA-001", "AAA-002"]);
        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await client.Received(1).RefreshSubtitleFlagOnlyAsync(alreadyScannedId, Arg.Any<CancellationToken>());
        await client.DidNotReceive().RefreshSubtitleFlagOnlyAsync(missingId, Arg.Any<CancellationToken>());
        Assert.Contains("synced subtitle flag for 1/1 Got movies", summary);
    }

    [Fact]
    public async Task RunAsync_OneMovieProbeThrowsUnauthorizedAccess_OthersStillGetProbed()
    {
        // A folder that exists but is unreadable (a
        // permission-denied entry) throws UnauthorizedAccessException
        // out of RefreshMediaInfoOnlyAsync. That must not abort probing for the rest of the batch.
        using var factory = new TestDbContextFactory();
        int brokenId, healthyId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var broken = new Movie { Code = "AAA-001", Status = MovieStatus.Got };
            var healthy = new Movie { Code = "AAA-002", Status = MovieStatus.Got };
            db.Movies.AddRange(broken, healthy);
            await db.SaveChangesAsync();
            brokenId = broken.Id;
            healthyId = healthy.Id;
        }

        var client = CreateLocalLibraryClient(folderCodes: ["AAA-001", "AAA-002"]);
        client.RefreshMediaInfoOnlyAsync(brokenId, Arg.Any<CancellationToken>())
            .Returns<Task<LocalRefreshResult>>(_ => throw new UnauthorizedAccessException("Access to the path is denied."));
        client.RefreshMediaInfoOnlyAsync(healthyId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(true, null));

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.Contains("probed MediaInfo for 1/2 Got movies", summary);
    }

    [Fact]
    public async Task RunAsync_WhenNfoSyncServiceProvided_DetectsConflicts()
    {
        using var factory = new TestDbContextFactory();
        var client = CreateLocalLibraryClient(folderCodes: []);
        var nfoSync = Substitute.For<INfoSyncService>();
        nfoSync.DetectAllMovieConflictsAsync(Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new ActorNfoConflictBatchResult(5, 2, []));

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance, nfoSync);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await nfoSync.Received(1).DetectAllMovieConflictsAsync(Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());
        Assert.Contains("2 .nfo conflict(s) detected", summary);
    }

    [Fact]
    public async Task RunAsync_WhenNewMoviesImported_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        var client = CreateLocalLibraryClient(
            folderCodes: ["NEW-001"],
            metadataLookup: code => new LocalLookupResult(true, "/media/" + code, new LocalMovieMetadata { Title = "Discovered Title" }, null));

        var notifier = new MovieChangeNotifier();
        var notifiedCount = 0;
        notifier.Changed += () => notifiedCount++;

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance, movieChangeNotifier: notifier);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.True(notifiedCount >= 1);
    }

    [Fact]
    public async Task RunAsync_WhenMoviesReverted_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "GONE-001", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var client = CreateLocalLibraryClient(folderCodes: []);
        var notifier = new MovieChangeNotifier();
        var notifiedCount = 0;
        notifier.Changed += () => notifiedCount++;

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance, movieChangeNotifier: notifier);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.True(notifiedCount >= 1);
    }

    [Fact]
    public async Task RunAsync_ThrottlesNotificationsDuringBatchImport()
    {
        using var factory = new TestDbContextFactory();
        var codes = Enumerable.Range(1, 10).Select(i => $"CODE-{i:D3}").ToList();
        var client = CreateLocalLibraryClient(folderCodes: codes);

        var notifier = new MovieChangeNotifier();
        var notifiedCount = 0;
        notifier.Changed += () => notifiedCount++;

        var task = new LibraryRescanTask(
            factory,
            client,
            NullLogger<LibraryRescanTask>.Instance,
            movieChangeNotifier: notifier,
            movieChangeNotifyInterval: TimeSpan.Zero);

        await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.True(notifiedCount > 1);
    }

    [Fact]
    public async Task RunAsync_OnCompletion_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        var client = CreateLocalLibraryClient(folderCodes: []);

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance, movieChangeNotifier: notifier);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.True(notified);
    }
}
