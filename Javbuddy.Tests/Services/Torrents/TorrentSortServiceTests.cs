using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentSortServiceTests
{
    private static TorrentSortService CreateService(
        TestDbContextFactory factory,
        IJavinizerClient javinizerClient,
        ILocalLibraryClient? localLibraryClient = null,
        TorrentChangeNotifier? changeNotifier = null,
        IJellyfinClient? jellyfinClient = null,
        IPathMappingService? pathMappingService = null,
        IQBittorrentClient? qBittorrentClient = null,
        MovieChangeNotifier? movieChangeNotifier = null) =>
        new(
            javinizerClient,
            localLibraryClient ?? Substitute.For<ILocalLibraryClient>(),
            jellyfinClient ?? Substitute.For<IJellyfinClient>(),
            pathMappingService ?? CreatePassthroughPathMappingService(),
            qBittorrentClient ?? Substitute.For<IQBittorrentClient>(),
            factory,
            changeNotifier ?? new TorrentChangeNotifier(),
            NullLogger<TorrentSortService>.Instance,
            movieChangeNotifier);

    /// <summary>A no-op IPathMappingService substitute that returns whatever path it was given
    /// unchanged, so tests that don't care about path translation (e.g. the Jellyfin sync tests
    /// below) can match Jellyfin library locations directly against the organize destination.</summary>
    private static IPathMappingService CreatePassthroughPathMappingService()
    {
        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateJavinizerPathToAppPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());
        pathMappingService.TranslateToAppPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());
        return pathMappingService;
    }

    private static (IJavinizerClient JavinizerClient, ILocalLibraryClient LocalLibraryClient) SetUpOrganizedJob(string jobId, int movieId, bool localMatchFound)
    {
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.OrganizeAsync(jobId, Arg.Any<OrganizePreviewRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerOrganizeResult(true, null));
        javinizerClient.GetBatchJobAsync(jobId, true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));
        javinizerClient.GetBatchJobAsync(jobId, false, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Status = JavinizerJobStatus.Organized }, null));

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.RefreshLocalMetadataAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new LocalRefreshResult(localMatchFound, localMatchFound ? null : "No local folder found for this code."));

        return (javinizerClient, localLibraryClient);
    }

    [Fact]
    public async Task OrganizeAsync_OnSuccessWithLocalMatch_SetsSortedAtAndMarksMovieGot_AndNotifiesChange()
    {
        using var factory = new TestDbContextFactory();
        int movieId, torrentId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;

            var torrent = new TorrentDownload { MovieId = movieId, MovieCode = "ABC-123", JavinizerBatchJobId = "job-1" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            torrentId = torrent.Id;
        }

        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: true);
        var changeNotifier = new TorrentChangeNotifier();
        var torrentNotified = false;
        changeNotifier.Changed += () => torrentNotified = true;

        var movieChangeNotifier = new MovieChangeNotifier();
        var movieNotified = false;
        movieChangeNotifier.Changed += () => movieNotified = true;

        var service = CreateService(factory, javinizerClient, localLibraryClient, changeNotifier, movieChangeNotifier: movieChangeNotifier);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        Assert.True(torrentNotified);
        Assert.True(movieNotified);

        await using var readDb = await factory.CreateDbContextAsync();
        var updatedTorrent = await readDb.TorrentDownloads.FindAsync(torrentId);
        Assert.NotNull(updatedTorrent!.SortedAt);
        var updatedMovie = await readDb.Movies.FindAsync(movieId);
        Assert.Equal(MovieStatus.Got, updatedMovie!.Status);
    }

    [Fact]
    public async Task OrganizeAsync_OnSuccessWithoutLocalMatch_SetsSortedAt_ButLeavesMovieStatusUnchanged()
    {
        using var factory = new TestDbContextFactory();
        int movieId, torrentId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;

            var torrent = new TorrentDownload { MovieId = movieId, MovieCode = "ABC-123", JavinizerBatchJobId = "job-1" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            torrentId = torrent.Id;
        }

        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);

        await using var readDb = await factory.CreateDbContextAsync();
        var updatedTorrent = await readDb.TorrentDownloads.FindAsync(torrentId);
        Assert.NotNull(updatedTorrent!.SortedAt);
        var updatedMovie = await readDb.Movies.FindAsync(movieId);
        Assert.Equal(MovieStatus.Missing, updatedMovie!.Status);
    }

    private static async Task<(int MovieId, int TorrentId)> SetUpOrganizedTorrentAsync(TestDbContextFactory factory, string jobId = "job-1")
    {
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
        TorrentDownload torrent;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", JavinizerBatchJobId = jobId };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
        }

        return (movie.Id, torrent.Id);
    }

    private static JellyfinVirtualFolderDto Library(string itemId, params string[] locations) =>
        new() { Name = itemId, ItemId = itemId, Locations = locations.ToList() };

    [Fact]
    public async Task OrganizeAsync_DestinationMatchesJellyfinLibrary_RefreshesThatLibrary()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinLibrariesResult(true, [Library("lib-1", "/media/dest")], null));
        jellyfinClient.RefreshItemAsync("lib-1", Arg.Any<CancellationToken>())
            .Returns(new MediaServerActionResult(true, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient, jellyfinClient: jellyfinClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest/movie1");

        Assert.True(result.Success);
        await jellyfinClient.Received(1).RefreshItemAsync("lib-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_DestinationMatchesNoLibrary_DoesNotRefresh_ButStillSucceeds()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinLibrariesResult(true, [Library("lib-1", "/other/library")], null));

        var service = CreateService(factory, javinizerClient, localLibraryClient, jellyfinClient: jellyfinClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await jellyfinClient.DidNotReceive().RefreshItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_GetLibrariesFails_DoesNotRefresh_ButStillSucceeds()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinLibrariesResult(false, null, "Jellyfin is not configured."));

        var service = CreateService(factory, javinizerClient, localLibraryClient, jellyfinClient: jellyfinClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await jellyfinClient.DidNotReceive().RefreshItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_TwoLibrariesWithOverlappingLocations_LongestMatchWins()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinLibrariesResult(true, [Library("lib-broad", "/media"), Library("lib-specific", "/media/dest")], null));
        jellyfinClient.RefreshItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaServerActionResult(true, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient, jellyfinClient: jellyfinClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest/movie1");

        Assert.True(result.Success);
        await jellyfinClient.Received(1).RefreshItemAsync("lib-specific", Arg.Any<CancellationToken>());
        await jellyfinClient.DidNotReceive().RefreshItemAsync("lib-broad", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_JellyfinSyncPathTranslationThrows_IsSwallowed_OrganizeStillSucceeds()
    {
        // Regression test: TrySyncJellyfinLibraryAsync's doc comment promises it
        // never throws, but had no try/catch. An exception from any of its dependencies (here,
        // path translation) must not propagate out of OrganizeAsync and crash the circuit.
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateJavinizerPathToAppPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("boom"));

        var jellyfinClient = Substitute.For<IJellyfinClient>();

        var service = CreateService(factory, javinizerClient, localLibraryClient, jellyfinClient: jellyfinClient, pathMappingService: pathMappingService);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await jellyfinClient.DidNotReceive().GetLibrariesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_UnchangedDefaultPosterWithFalseCropFlag_RestoresCropFlagBeforeOrganizing()
    {
        // Javinizer-go can report ShouldCropPoster = false for the untouched, scraped
        // default poster (see the poster-editor bug). Javbuddy must force it back to true before
        // organizing so the on-disk poster still gets cropped.
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "default.jpg", OriginalPosterUrl = "default.jpg", ShouldCropPoster = false };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                }
            }, null));
        javinizerClient.UpdateResultAsync("job-1", "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(true, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await javinizerClient.Received(1).UpdateResultAsync(
            "job-1", "res-1", Arg.Is<MovieViewDto>(m => m.ShouldCropPoster == true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_ReplacedPoster_ClearsCropFlagBeforeOrganizing()
    {
        // A user-picked screenshot must never be auto-cropped, regardless of the flag javinizer-go
        // last reported for it.
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "shot2.jpg", OriginalPosterUrl = "default.jpg", ShouldCropPoster = true };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                }
            }, null));
        javinizerClient.UpdateResultAsync("job-1", "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(true, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await javinizerClient.Received(1).UpdateResultAsync(
            "job-1", "res-1", Arg.Is<MovieViewDto>(m => m.ShouldCropPoster == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_CropFlagAlreadyCorrect_SkipsUpdate()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "default.jpg", OriginalPosterUrl = "default.jpg", ShouldCropPoster = true };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                }
            }, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await javinizerClient.DidNotReceive().UpdateResultAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_ExcludedResult_SkipsCropFlagUpdate()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "default.jpg", OriginalPosterUrl = "default.jpg", ShouldCropPoster = false };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                },
                Excluded = new Dictionary<string, bool> { ["res-1"] = true }
            }, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await javinizerClient.DidNotReceive().UpdateResultAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_CropFlagUpdateFails_AbortsOrganize()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "default.jpg", OriginalPosterUrl = "default.jpg", ShouldCropPoster = false };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                }
            }, null));
        javinizerClient.UpdateResultAsync("job-1", "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(false, "javinizer-go rejected the update."));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.False(result.Success);
        Assert.Equal("javinizer-go rejected the update.", result.ErrorMessage);
        await javinizerClient.DidNotReceive().OrganizeAsync(Arg.Any<string>(), Arg.Any<OrganizePreviewRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizeAsync_BatchJobLoadFails_AbortsOrganize()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(false, null, "javinizer-go is unreachable."));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.False(result.Success);
        Assert.Equal("javinizer-go is unreachable.", result.ErrorMessage);
        await javinizerClient.DidNotReceive().OrganizeAsync(Arg.Any<string>(), Arg.Any<OrganizePreviewRequestDto>(), Arg.Any<CancellationToken>());
    }

    private static async Task<int> SetUpTorrentAsync(TestDbContextFactory factory, string? hash, string? contentPath, string? savePath = null)
    {
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
        TorrentDownload torrent;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", Hash = hash, ContentPath = contentPath, SavePath = savePath };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
        }

        return torrent.Id;
    }

    [Fact]
    public async Task PreviewCleanupAsync_NoKnownPath_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var torrentId = await SetUpTorrentAsync(factory, hash: "h1", contentPath: null, savePath: null);

        var service = CreateService(factory, Substitute.For<IJavinizerClient>());
        var result = await service.PreviewCleanupAsync(torrentId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PreviewCleanupAsync_PathDoesNotExist_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var missingPath = Path.Combine(Path.GetTempPath(), "javbuddy-cleanup-test-missing-" + Guid.NewGuid());
        var torrentId = await SetUpTorrentAsync(factory, hash: "h1", contentPath: missingPath);

        var service = CreateService(factory, Substitute.For<IJavinizerClient>());
        var result = await service.PreviewCleanupAsync(torrentId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PreviewCleanupAsync_DirectoryWithNestedFile_ListsEntriesRelativeToRoot()
    {
        using var factory = new TestDbContextFactory();
        var root = Path.Combine(Path.GetTempPath(), "javbuddy-cleanup-test-" + Guid.NewGuid());
        var nestedDir = Path.Combine(root, "subfolder");
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(root, "movie.mp4"), "data");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "ad.txt"), "data");

        try
        {
            var torrentId = await SetUpTorrentAsync(factory, hash: "h1", contentPath: root);

            var service = CreateService(factory, Substitute.For<IJavinizerClient>());
            var result = await service.PreviewCleanupAsync(torrentId);

            Assert.True(result.Success);
            Assert.Equal(root, result.ResolvedPath);
            Assert.Contains("movie.mp4", result.EntryPaths!);
            Assert.Contains("subfolder/", result.EntryPaths!);
            Assert.Contains(Path.Combine("subfolder", "ad.txt").Replace('\\', '/'), result.EntryPaths!.Select(e => e.Replace('\\', '/')));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewCleanupAsync_SingleFile_ReturnsThatFile()
    {
        using var factory = new TestDbContextFactory();
        var file = Path.Combine(Path.GetTempPath(), "javbuddy-cleanup-test-" + Guid.NewGuid() + ".mp4");
        await File.WriteAllTextAsync(file, "data");

        try
        {
            var torrentId = await SetUpTorrentAsync(factory, hash: "h1", contentPath: file);

            var service = CreateService(factory, Substitute.For<IJavinizerClient>());
            var result = await service.PreviewCleanupAsync(torrentId);

            Assert.True(result.Success);
            Assert.Equal([Path.GetFileName(file)], result.EntryPaths);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task CleanupSourceAsync_RowNotFound_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Substitute.For<IJavinizerClient>());

        var result = await service.CleanupSourceAsync(-1);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CleanupSourceAsync_NoHash_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var torrentId = await SetUpTorrentAsync(factory, hash: null, contentPath: "/media/x");

        var service = CreateService(factory, Substitute.For<IJavinizerClient>());
        var result = await service.CleanupSourceAsync(torrentId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CleanupSourceAsync_Success_DeletesWithFiles_AndMarksRemoved()
    {
        using var factory = new TestDbContextFactory();
        var torrentId = await SetUpTorrentAsync(factory, hash: "abc123", contentPath: "/media/x");

        var qBittorrentClient = Substitute.For<IQBittorrentClient>();
        qBittorrentClient.DeleteTorrentAsync("abc123", deleteFiles: true, Arg.Any<CancellationToken>()).Returns(true);

        var service = CreateService(factory, Substitute.For<IJavinizerClient>(), qBittorrentClient: qBittorrentClient);
        var result = await service.CleanupSourceAsync(torrentId);

        Assert.True(result.Success);
        await qBittorrentClient.Received(1).DeleteTorrentAsync("abc123", deleteFiles: true, Arg.Any<CancellationToken>());

        await using var readDb = await factory.CreateDbContextAsync();
        var updated = await readDb.TorrentDownloads.FindAsync(torrentId);
        Assert.NotNull(updated!.RemovedFromClientAt);
        Assert.Equal(TorrentDownloadStatus.Removed, updated.Status);
    }

    [Fact]
    public async Task CleanupSourceAsync_ClientFails_ReturnsFailure_AndDoesNotMarkRemoved()
    {
        using var factory = new TestDbContextFactory();
        var torrentId = await SetUpTorrentAsync(factory, hash: "abc123", contentPath: "/media/x");

        var qBittorrentClient = Substitute.For<IQBittorrentClient>();
        qBittorrentClient.DeleteTorrentAsync("abc123", deleteFiles: true, Arg.Any<CancellationToken>()).Returns(false);

        var service = CreateService(factory, Substitute.For<IJavinizerClient>(), qBittorrentClient: qBittorrentClient);
        var result = await service.CleanupSourceAsync(torrentId);

        Assert.False(result.Success);

        await using var readDb = await factory.CreateDbContextAsync();
        var updated = await readDb.TorrentDownloads.FindAsync(torrentId);
        Assert.Null(updated!.RemovedFromClientAt);
    }

    [Fact]
    public async Task PreviewWithEditsAsync_SendsDestinationAndUnsavedMovie()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.PreviewOrganizeAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(new JavinizerOrganizePreviewResult(true, new OrganizePreviewResponseDto(), null));
        var service = CreateService(factory, javinizerClient);

        await service.PreviewWithEditsAsync(torrentId, "res-1", "/out", new MovieViewDto { Title = "X" });

        await javinizerClient.Received().PreviewOrganizeAsync("job-1", "res-1",
            Arg.Is<OrganizePreviewRequestDto>(r => r.Movie!.Title == "X" && r.Destination == "/out"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OrganizePreviewRequestDto_OmitsMovieWhenNull()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new OrganizePreviewRequestDto { Destination = "/out" });

        Assert.DoesNotContain("\"movie\"", json);
    }

    [Fact]
    public async Task RescrapeWithScrapersAsync_SendsForcedRescrapeLimitedToScrapers()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.RescrapeAsync(default!, default!, default!, default).ReturnsForAnyArgs(new JavinizerRescrapeResult(true, null));
        var service = CreateService(factory, javinizerClient);

        await service.RescrapeWithScrapersAsync(torrentId, "res-1", "ABC-123", ["dmm"]);

        await javinizerClient.Received().RescrapeAsync("job-1", "res-1",
            Arg.Is<BatchRescrapeRequestDto>(r => r.Force && r.ManualSearchInput == "ABC-123" && r.SelectedScrapers!.SequenceEqual(new[] { "dmm" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverrideFieldAsync_And_GetResultSourcesAsync_UseTheTorrentsJob()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory, "job-7");
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.OverrideFieldAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(new JavinizerFieldOverrideResult(true, new FieldOverrideResponseDto(), null));
        javinizerClient.GetResultSourcesAsync(default!, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [], null));
        var service = CreateService(factory, javinizerClient);

        await service.OverrideFieldAsync(torrentId, "res-1", "maker", "dmm");
        await service.GetResultSourcesAsync(torrentId, "res-1");

        await javinizerClient.Received().OverrideFieldAsync("job-7", "res-1", "maker", "dmm", Arg.Any<CancellationToken>());
        await javinizerClient.Received().GetResultSourcesAsync("job-7", "res-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverrideFieldAsync_NoJob_ReturnsFailureWithoutCallingJavinizer()
    {
        using var factory = new TestDbContextFactory();
        var torrentId = await SetUpTorrentAsync(factory, null, null);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        var service = CreateService(factory, javinizerClient);

        var result = await service.OverrideFieldAsync(torrentId, "res-1", "maker", "dmm");

        Assert.False(result.Success);
        await javinizerClient.DidNotReceiveWithAnyArgs().OverrideFieldAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task OrganizeAsync_LeavesManuallyCroppedPosterFlagAlone()
    {
        // A manual crop (javinizer-go poster-crop) stores PosterCropBounds and clears the
        // auto-crop flag on purpose; the "restore the crop flag" must not undo it.
        using var factory = new TestDbContextFactory();
        var (movieId, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var (javinizerClient, localLibraryClient) = SetUpOrganizedJob("job-1", movieId, localMatchFound: false);

        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            PosterUrl = "default.jpg",
            OriginalPosterUrl = "default.jpg",
            ShouldCropPoster = false,
            PosterCropBounds = new CropBoundsDto { X = 0, Y = 0, Width = 100, Height = 150 }
        };
        javinizerClient.GetBatchJobAsync("job-1", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Movie = movie }
                }
            }, null));

        var service = CreateService(factory, javinizerClient, localLibraryClient);
        var result = await service.OrganizeAsync(torrentId, "/media/dest");

        Assert.True(result.Success);
        await javinizerClient.DidNotReceiveWithAnyArgs().UpdateResultAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CropPosterAsync_ConvertsNormalizedRectToClampedPixels()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.CropPosterAsync("job-1", "res-1", Arg.Any<PosterCropRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterCropResult(true, new PosterCropResponseDto(), null));
        var service = CreateService(factory, javinizerClient);

        await service.CropPosterAsync(torrentId, "res-1", new NormalizedCropRect(0.5, 0.1, 0.6, 0.95), 800, 538);

        await javinizerClient.Received().CropPosterAsync("job-1", "res-1",
            Arg.Is<PosterCropRequestDto>(r => r.X == 400 && r.Y == 54 && r.Width == 400 && r.Height == 484), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPosterCropSourceAsync_ReadsDimensionsWithSkia()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        using var bitmap = new SkiaSharp.SKBitmap(40, 60);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetTempPosterAsync("job-1", "ABC-123", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBytesResult(true, encoded.ToArray(), "image/jpeg", null));
        var service = CreateService(factory, javinizerClient);

        var (source, error) = await service.GetPosterCropSourceAsync(torrentId, "ABC-123");

        Assert.Null(error);
        Assert.Equal((40, 60, "image/jpeg"), (source!.Width, source.Height, source.ContentType));
    }

    [Fact]
    public async Task GetPosterCropSourceAsync_UnreadableBytes_ReturnsError()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetTempPosterAsync("job-1", "ABC-123", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBytesResult(true, [1, 2, 3], "image/jpeg", null));
        var service = CreateService(factory, javinizerClient);

        var (source, error) = await service.GetPosterCropSourceAsync(torrentId, "ABC-123");

        Assert.Null(source);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetPosterCropSourceAsync_NoFullSizePoster_FallsBackToTheCroppedOne()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        using var bitmap = new SkiaSharp.SKBitmap(20, 30);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetTempPosterAsync("job-1", "ABC-123", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBytesResult(false, null, null, "javinizer-go returned 404: not found", NotFound: true));
        javinizerClient.GetTempPosterAsync("job-1", "ABC-123", false, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBytesResult(true, encoded.ToArray(), "image/jpeg", null));
        var service = CreateService(factory, javinizerClient);

        var (source, error) = await service.GetPosterCropSourceAsync(torrentId, "ABC-123");

        Assert.Null(error);
        Assert.Equal((20, 30, false), (source!.Width, source.Height, source.IsFullSize));
    }

    [Fact]
    public async Task GetPosterCropSourceAsync_OtherFailure_DoesNotFallBack()
    {
        using var factory = new TestDbContextFactory();
        var (_, torrentId) = await SetUpOrganizedTorrentAsync(factory);
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.GetTempPosterAsync("job-1", "ABC-123", true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBytesResult(false, null, null, "javinizer-go returned 500: boom"));
        var service = CreateService(factory, javinizerClient);

        var (source, error) = await service.GetPosterCropSourceAsync(torrentId, "ABC-123");

        Assert.Null(source);
        Assert.Contains("500", error);
        await javinizerClient.DidNotReceive().GetTempPosterAsync(Arg.Any<string>(), Arg.Any<string>(), false, Arg.Any<CancellationToken>());
    }
}
