using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.VideoRepair;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.VideoRepair;

public class VideoRepairServiceTests : IDisposable
{
    private readonly string tempFolder = Directory.CreateTempSubdirectory("javbuddy-videorepair-test-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task GetRepairCandidatesAsync_MultiVersionMovie_ReturnsAllVersionsOrdered()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Id = 1,
                Code = "ABC-123",
                Status = MovieStatus.Got,
                HasBrokenBFrames = true,
                MediaDurationSeconds = 7200,
            };
            movie.MovieFiles.Add(new MovieFile
            {
                Id = 10,
                MovieId = 1,
                FileName = "ABC-123-4K.mp4",
                VersionTag = "4K",
                FileSizeBytes = 8_000_000_000,
                Width = 3840,
                Height = 2160,
                IsPrimary = false,
            });
            movie.MovieFiles.Add(new MovieFile
            {
                Id = 11,
                MovieId = 1,
                FileName = "ABC-123.mp4",
                VersionTag = "Original",
                FileSizeBytes = 4_000_000_000,
                Width = 1920,
                Height = 1080,
                IsPrimary = true,
            });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var localLibrary = Substitute.For<ILocalLibraryClient>();
        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var jobTracker = Substitute.For<IVideoRepairJobTracker>();

        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);
        var candidates = await service.GetRepairCandidatesAsync(1);

        Assert.Equal(3, candidates.Count);
        Assert.Equal("Original", candidates[0].VersionTag);
        Assert.True(candidates[0].IsPrimary);
        Assert.Equal("4K", candidates[1].VersionTag);
        Assert.True(candidates[0].HasBrokenBFrames);

        // Trailing entry repairs every version in one job (MovieFileId null).
        Assert.Equal("All versions", candidates[2].VersionTag);
        Assert.Null(candidates[2].MovieFileId);
        Assert.Equal(12_000_000_000, candidates[2].FileSizeBytes);
    }

    [Fact]
    public async Task RepairAsync_MovieNotFound_ReturnsFailed()
    {
        using var dbFactory = new TestDbContextFactory();
        var localLibrary = Substitute.For<ILocalLibraryClient>();
        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var jobTracker = Substitute.For<IVideoRepairJobTracker>();

        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);
        var result = await service.RepairAsync(999);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task RepairAsync_FolderNotFound_ReturnsFailed()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Id = 1, Code = "NO-DIR-001", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }

        var localLibrary = Substitute.For<ILocalLibraryClient>();
        localLibrary.ResolveMovieFolderPathAsync("NO-DIR-001", Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var jobTracker = Substitute.For<IVideoRepairJobTracker>();

        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);
        var result = await service.RepairAsync(1);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task RepairAsync_SuccessfulRemux_RotatesFileRefreshesMediaInfoAndClearsBrokenFlag()
    {
        using var dbFactory = new TestDbContextFactory();
        var originalFile = Path.Combine(tempFolder, "HMN-168.mp4");
        await File.WriteAllBytesAsync(originalFile, [1, 2, 3, 4, 5]);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Id = 1,
                Code = "HMN-168",
                Status = MovieStatus.Got,
                HasBrokenBFrames = true,
                MediaVideoFileName = "HMN-168.mp4",
                MediaDurationSeconds = 120,
            });
            await db.SaveChangesAsync();
        }

        var localLibrary = Substitute.For<ILocalLibraryClient>();
        localLibrary.ResolveMovieFolderPathAsync("HMN-168", Arg.Any<CancellationToken>())
            .Returns(tempFolder);

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.RemuxDts2PtsAsync(
            Arg.Is<string>(s => s == originalFile),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<IProgress<FfmpegProgress>>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var staging = callInfo.ArgAt<string>(1);
                await File.WriteAllBytesAsync(staging, [10, 20, 30, 40, 50, 60]);
                return FfmpegRunResult.Ok();
            });

        ffmpegClient.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 120, VideoCodec = "h264" });

        var jobTracker = Substitute.For<IVideoRepairJobTracker>();
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);

        var result = await service.RepairAsync(1);

        Assert.True(result.Success);
        Assert.Equal(originalFile, result.OutputPath);
        Assert.True(File.Exists(originalFile));
        // Repaired content was rotated into place
        Assert.Equal(6, (await File.ReadAllBytesAsync(originalFile)).Length);
        Assert.False(File.Exists(originalFile + ".bak"));

        // Refreshed local media info
        await localLibrary.Received(1).RefreshMediaInfoOnlyAsync(1, Arg.Any<CancellationToken>());

        // Cleared HasBrokenBFrames flag
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = await db.Movies.FindAsync(1);
            Assert.False(movie!.HasBrokenBFrames);
        }
    }

    [Fact]
    public async Task RepairAsync_RemuxFails_DeletesStagingFileAndLeavesOriginalIntact()
    {
        using var dbFactory = new TestDbContextFactory();
        var originalFile = Path.Combine(tempFolder, "FAIL-001.mp4");
        await File.WriteAllBytesAsync(originalFile, [1, 2, 3]);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Id = 1,
                Code = "FAIL-001",
                Status = MovieStatus.Got,
                HasBrokenBFrames = true,
                MediaVideoFileName = "FAIL-001.mp4",
                MediaDurationSeconds = 60,
            });
            await db.SaveChangesAsync();
        }

        var localLibrary = Substitute.For<ILocalLibraryClient>();
        localLibrary.ResolveMovieFolderPathAsync("FAIL-001", Arg.Any<CancellationToken>())
            .Returns(tempFolder);

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.RemuxDts2PtsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<IProgress<FfmpegProgress>>(),
            Arg.Any<CancellationToken>())
            .Returns(FfmpegRunResult.Failed("Bitstream filter error"));

        var jobTracker = Substitute.For<IVideoRepairJobTracker>();
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);

        var result = await service.RepairAsync(1);

        Assert.False(result.Success);
        Assert.Contains("Bitstream filter error", result.ErrorMessage);
        Assert.True(File.Exists(originalFile));
        Assert.Equal(3, (await File.ReadAllBytesAsync(originalFile)).Length);
        Assert.Empty(Directory.GetFiles(tempFolder, "*.repairing.*"));

        // HasBrokenBFrames remained unchanged
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = await db.Movies.FindAsync(1);
            Assert.True(movie!.HasBrokenBFrames);
        }
    }

    private async Task<(TestDbContextFactory Factory, ILocalLibraryClient Library, IFfmpegClient Ffmpeg)> SetUpTwoVersionMovieAsync()
    {
        var dbFactory = new TestDbContextFactory();
        await File.WriteAllBytesAsync(Path.Combine(tempFolder, "TWO-001.mp4"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(tempFolder, "TWO-001-4K.mp4"), [1, 2, 3, 4]);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = new Movie { Id = 1, Code = "TWO-001", Status = MovieStatus.Got, HasBrokenBFrames = true, MediaDurationSeconds = 60 };
            movie.MovieFiles.Add(new MovieFile { Id = 10, MovieId = 1, FileName = "TWO-001.mp4", VersionTag = "Original", IsPrimary = true });
            movie.MovieFiles.Add(new MovieFile { Id = 11, MovieId = 1, FileName = "TWO-001-4K.mp4", VersionTag = "4K" });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var localLibrary = Substitute.For<ILocalLibraryClient>();
        localLibrary.ResolveMovieFolderPathAsync("TWO-001", Arg.Any<CancellationToken>()).Returns(tempFolder);

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.RemuxDts2PtsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await File.WriteAllBytesAsync(callInfo.ArgAt<string>(1), [9, 9, 9, 9, 9, 9]);
                return FfmpegRunResult.Ok();
            });
        ffmpegClient.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 60, VideoCodec = "h264" });

        return (dbFactory, localLibrary, ffmpegClient);
    }

    [Fact]
    public async Task RepairAsync_OneVersionOfMultiVersionMovie_KeepsBrokenFlagForUntouchedVersions()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        var result = await service.RepairAsync(1, movieFileId: 11);

        Assert.True(result.Success);
        Assert.Equal(6, (await File.ReadAllBytesAsync(Path.Combine(tempFolder, "TWO-001-4K.mp4"))).Length);
        Assert.Equal(3, (await File.ReadAllBytesAsync(Path.Combine(tempFolder, "TWO-001.mp4"))).Length);
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.True((await db.Movies.FindAsync(1))!.HasBrokenBFrames);
    }

    [Fact]
    public async Task RepairAsync_AllVersions_RepairsEveryFileClearsFlagAndRefreshesOnce()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        var result = await service.RepairAsync(1);

        Assert.True(result.Success);
        Assert.Equal(6, (await File.ReadAllBytesAsync(Path.Combine(tempFolder, "TWO-001.mp4"))).Length);
        Assert.Equal(6, (await File.ReadAllBytesAsync(Path.Combine(tempFolder, "TWO-001-4K.mp4"))).Length);
        await localLibrary.Received(1).RefreshMediaInfoOnlyAsync(1, Arg.Any<CancellationToken>());
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.False((await db.Movies.FindAsync(1))!.HasBrokenBFrames);
    }

    [Fact]
    public async Task RepairAsync_SecondVersionFails_KeepsFlagButStillRefreshesMediaInfo()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        var calls = 0;
        ffmpegClient.RemuxDts2PtsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                if (++calls == 2) return FfmpegRunResult.Failed("boom");
                await File.WriteAllBytesAsync(callInfo.ArgAt<string>(1), [9, 9, 9, 9, 9, 9]);
                return FfmpegRunResult.Ok();
            });
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        var result = await service.RepairAsync(1);

        Assert.False(result.Success);
        Assert.Contains("boom", result.ErrorMessage);
        await localLibrary.Received(1).RefreshMediaInfoOnlyAsync(1, Arg.Any<CancellationToken>());
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.True((await db.Movies.FindAsync(1))!.HasBrokenBFrames);
    }

    [Fact]
    public async Task RepairAsync_UnknownMovieFileId_FailsInsteadOfRepairingAnotherFile()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        var result = await service.RepairAsync(1, movieFileId: 999);

        Assert.False(result.Success);
        Assert.Contains("no longer exists", result.ErrorMessage);
        await ffmpegClient.DidNotReceiveWithAnyArgs().RemuxDts2PtsAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task RepairAsync_MediaInfoRefreshThrows_StillReportsSuccessAndClearsFlag()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        localLibrary.RefreshMediaInfoOnlyAsync(1, Arg.Any<CancellationToken>())
            .Returns<Task<LocalRefreshResult>>(_ => throw new InvalidOperationException("db down"));
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        var result = await service.RepairAsync(1);

        Assert.True(result.Success);
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.False((await db.Movies.FindAsync(1))!.HasBrokenBFrames);
    }

    [Fact]
    public async Task RepairAsync_StagingFileName_IsIgnoredByVideoFileDiscovery()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        List<string>? seenDuringRemux = null;
        ffmpegClient.RemuxDts2PtsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await File.WriteAllBytesAsync(callInfo.ArgAt<string>(1), [9, 9]);
                seenDuringRemux = MovieVersionParser.FindVideoFiles(tempFolder);
                return FfmpegRunResult.Ok();
            });
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, Substitute.For<IVideoRepairJobTracker>());

        await service.RepairAsync(1, movieFileId: 10);

        Assert.NotNull(seenDuringRemux);
        Assert.Equal(2, seenDuringRemux.Count);
        Assert.DoesNotContain(seenDuringRemux, f => f.Contains(".repairing."));
    }

    [Fact]
    public async Task StartRepairJobAsync_OtherFileAlreadyRunning_Throws()
    {
        var (dbFactory, localLibrary, ffmpegClient) = await SetUpTwoVersionMovieAsync();
        using var _ = dbFactory;
        var running = new VideoRepairJob { MovieId = 1, MovieFileId = 10, MovieCode = "TWO-001", FileName = "TWO-001.mp4" };
        var jobTracker = Substitute.For<IVideoRepairJobTracker>();
        jobTracker.GetOrStart(1, Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<IProgress<FfmpegProgress>, CancellationToken, Task<VideoRepairResult>>>())
            .Returns(running);
        var service = new VideoRepairService(dbFactory, localLibrary, ffmpegClient, jobTracker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartRepairJobAsync(1, movieFileId: 11));

        Assert.Contains("already running", ex.Message);
    }
}
