using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.LocalLibrary;

public class LocalLibraryClientTests
{
    [Fact]
    public void LocalLibraryEnvConfig_GetRootPaths_ReadsIndexedArray()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths:0"] = "/media/jav1",
            ["LocalLibrary:RootPaths:1"] = "/media/jav2",
            ["LocalLibrary:RootPaths:2"] = "  /media/jav3  "
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var paths = LocalLibraryEnvConfig.GetRootPaths(config);

        Assert.Equal(3, paths.Count);
        Assert.Equal("/media/jav1", paths[0]);
        Assert.Equal("/media/jav2", paths[1]);
        Assert.Equal("/media/jav3", paths[2]);
        Assert.True(LocalLibraryEnvConfig.IsSet(config));
    }

    [Fact]
    public void LocalLibraryEnvConfig_GetRootPaths_ReadsJsonArrayString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths"] = "[\"/media/movies1\", \"/media/movies2\"]"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var paths = LocalLibraryEnvConfig.GetRootPaths(config);

        Assert.Equal(2, paths.Count);
        Assert.Equal("/media/movies1", paths[0]);
        Assert.Equal("/media/movies2", paths[1]);
        Assert.True(LocalLibraryEnvConfig.IsSet(config));
    }

    [Fact]
    public void LocalLibraryEnvConfig_GetRootPaths_ReadsCommaDelimitedString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths"] = "D:\\media\\jav, D:\\media\\vr"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var paths = LocalLibraryEnvConfig.GetRootPaths(config);

        Assert.Equal(2, paths.Count);
        Assert.Equal("D:\\media\\jav", paths[0]);
        Assert.Equal("D:\\media\\vr", paths[1]);
        Assert.True(LocalLibraryEnvConfig.IsSet(config));
    }

    [Fact]
    public void LocalLibraryEnvConfig_GetRootPaths_ReadsNewlineDelimitedString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths"] = "D:\\media\\jav\nD:\\media\\vr\r\nD:\\media\\extra"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var paths = LocalLibraryEnvConfig.GetRootPaths(config);

        Assert.Equal(3, paths.Count);
        Assert.Equal("D:\\media\\jav", paths[0]);
        Assert.Equal("D:\\media\\vr", paths[1]);
        Assert.Equal("D:\\media\\extra", paths[2]);
        Assert.True(LocalLibraryEnvConfig.IsSet(config));
    }

    [Fact]
    public void LocalLibraryEnvConfig_GetRootPaths_EmptyReturnsEmptyList()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths"] = ""
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var paths = LocalLibraryEnvConfig.GetRootPaths(config);

        Assert.Empty(paths);
        Assert.False(LocalLibraryEnvConfig.IsSet(config));
    }

    [Fact]
    public async Task GetRootPathsAsync_PrefersEnvOverDb()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.LocalLibrarySettings.Add(new LocalLibrarySettings { RootPaths = "/db/path1,/db/path2" });
            await db.SaveChangesAsync();
        }

        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths:0"] = "/env/path1",
            ["LocalLibrary:RootPaths:1"] = "/env/path2"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var client = new LocalLibraryClient(factory, config, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IMediaInfoProber>(), NullLogger<LocalLibraryClient>.Instance);
        var paths = await client.GetRootPathsAsync();

        Assert.Equal(2, paths.Count);
        Assert.Equal("/env/path1", paths[0]);
        Assert.Equal("/env/path2", paths[1]);
    }

    [Fact]
    public async Task GetRootPathsAsync_FallsBackToDbWhenEnvNotSet()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.LocalLibrarySettings.Add(new LocalLibrarySettings { RootPaths = "/db/path1\n/db/path2" });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().Build();
        var client = new LocalLibraryClient(factory, config, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IMediaInfoProber>(), NullLogger<LocalLibraryClient>.Instance);
        var paths = await client.GetRootPathsAsync();

        Assert.Equal(2, paths.Count);
        Assert.Equal("/db/path1", paths[0]);
        Assert.Equal("/db/path2", paths[1]);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a,b,c", 3)]
    [InlineData("a;b;c", 3)]
    [InlineData("a\nb\nc", 3)]
    [InlineData("[\"a\",\"b\"]", 2)]
    public void ParseRootPaths_ParsesVariousFormats(string? raw, int expectedCount)
    {
        var result = LocalLibraryClient.ParseRootPaths(raw);
        Assert.Equal(expectedCount, result.Count);
    }

    /// <summary>A temp library root that cleans itself up, for the filesystem-touching tests below.</summary>
    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public string AddMovieFolder(string folderName)
        {
            var folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static LocalLibraryClient CreateClientFor(TestDbContextFactory factory, TempRoot root, IMediaInfoProber? mediaInfoProber = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalLibrary:RootPaths:0"] = root.Path })
            .Build();
        return new LocalLibraryClient(factory, config, new MemoryCache(new MemoryCacheOptions()), mediaInfoProber ?? Substitute.For<IMediaInfoProber>(), NullLogger<LocalLibraryClient>.Instance);
    }

    [Fact]
    public async Task SaveRawNfoAsync_OverwritesNfoAndReturnsPreviousContent_StrayBakIsNotPickedUpAsTheNfo()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        // A non-code file name, so FindNfoFile falls back to its "*.nfo" enumeration — which
        // must still ignore a "movie.nfo.bak" left by older versions (and any stray "movie.nfo.tmp").
        var nfoPath = Path.Combine(folder, "movie.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie>old</movie>");
        var client = CreateClientFor(factory, root);

        Assert.Equal("<movie>old</movie>", await client.SaveRawNfoAsync("ABC-123", "<movie>new</movie>"));
        Assert.False(File.Exists(nfoPath + ".bak"));
        await File.WriteAllTextAsync(nfoPath + ".bak", "<movie>legacy backup</movie>");
        await File.WriteAllTextAsync(nfoPath + ".tmp", "<movie>stray</movie>");

        Assert.Equal("<movie>new</movie>", await client.GetRawNfoAsync("ABC-123"));
        Assert.Equal(nfoPath, await client.ResolveNfoFilePathAsync("ABC-123"));
    }

    [Fact]
    public async Task SaveRawNfoAsync_ReadOnlyNfo_ReturnsNullWithoutWriting()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var nfoPath = Path.Combine(folder, "ABC-123.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie>old</movie>");
        new FileInfo(nfoPath).IsReadOnly = true;
        var client = CreateClientFor(factory, root);

        try
        {
            Assert.Null(await client.SaveRawNfoAsync("ABC-123", "<movie>new</movie>"));
            Assert.Equal(
                ConditionalNfoSaveResult.NotFoundOrUnwritable,
                await client.SaveRawNfoIfUnchangedAsync("ABC-123", "<movie>old</movie>", "<movie>new</movie>"));
            Assert.Equal("<movie>old</movie>", await File.ReadAllTextAsync(nfoPath));
        }
        finally
        {
            new FileInfo(nfoPath).IsReadOnly = false;
        }
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_ReturnsTheFirstCandidateThatExists()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "folder.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.png"), "x");

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveFirstExistingLocalFilePathAsync("ABC-123", ["poster.jpg", "poster.png", "folder.jpg"]);

        // Preference order comes from the candidate list, not from what the directory happens to
        // list first — poster.png wins over folder.jpg even though poster.jpg is missing.
        Assert.Equal(Path.Combine(folder, "poster.png"), path);
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_MatchesTheMovieFolderCaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("abc-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveFirstExistingLocalFilePathAsync("ABC-123", ["poster.jpg"]);

        // Compared case-insensitively on purpose: which of the two spellings comes back depends on
        // whether the platform's own Directory.Exists fast path matched (Windows) or the
        // case-insensitive folder-index fallback did (Linux). Either is a correct resolution.
        Assert.Equal(Path.Combine(folder, "poster.jpg"), path, ignoreCase: true);
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_MatchesAFolderWithDifferentZeroPadding()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("SAVR-01195");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveFirstExistingLocalFilePathAsync("SAVR-1195", ["poster.jpg"]);

        Assert.Equal(Path.Combine(folder, "poster.jpg"), path);
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_DoesNotFalsePositiveOnUnrelatedCodesThatShareATrailingDigit()
    {
        // "E2E-IMPORT-1" and "E2E-001" both reduce to the same distributor-stripped canonical
        // key ("E2E-1"), but they are not the same release with different zero-padding — the
        // zero-padding fallback must not treat them as a match.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("E2E-IMPORT-1");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveFirstExistingLocalFilePathAsync("E2E-001", ["poster.jpg"]);

        Assert.Null(path);
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_NoMatchingFile_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        root.AddMovieFolder("ABC-123");

        var client = CreateClientFor(factory, root);

        Assert.Null(await client.ResolveFirstExistingLocalFilePathAsync("ABC-123", ["poster.jpg", "folder.jpg"]));
        Assert.Null(await client.ResolveFirstExistingLocalFilePathAsync("NOPE-001", ["poster.jpg"]));
    }

    [Fact]
    public async Task ResolveFirstExistingLocalFilePathAsync_DisallowedFileNames_AreNeverServed()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.nfo"), "x");

        var client = CreateClientFor(factory, root);

        Assert.Null(await client.ResolveFirstExistingLocalFilePathAsync("ABC-123", ["ABC-123.nfo", "../secret.jpg"]));
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_PrefersWebmMovieFileOverMp4Trailer()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.webm");
        await File.WriteAllTextAsync(moviePath, "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123-trailer.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        await prober.Received(1).ProbeAsync(moviePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_SuffixedWebmFileName_StillExcludesTrailer()
    {
        // The movie file isn't
        // an exact "{code}.ext" match (here, suffixed by video-interpolation processing), so this
        // only resolves correctly via the fallback path once the trailer is excluded as a
        // candidate — with the trailer still in the running, the fallback could return either file
        // depending on enumeration order.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.RIFE3.1.webm");
        await File.WriteAllTextAsync(moviePath, "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123-trailer.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        await prober.Received(1).ProbeAsync(moviePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_SidecarSubtitleFile_SetsMediaHasSubtitleFile()
    {
        // The subtitle file isn't a
        // plain "{code}.srt" either — it's named after whatever transcription tool produced it.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-159");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-159.mkv"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-159.whisper large v3.en.srt"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-159" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_NoSubtitleFile_LeavesMediaHasSubtitleFileFalse()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.False(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshSubtitleFlagOnlyAsync_SidecarSubtitleFile_SetsMediaHasSubtitleFile()
    {
        // Unlike RefreshMediaInfoOnlyAsync above, this never touches MediaInfoProber at all — it's
        // meant to be cheap enough to rerun for the whole library on every scheduled scan.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-159");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-159.mkv"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-159.whisper large v3.en.srt"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-159" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);

        var result = await client.RefreshSubtitleFlagOnlyAsync(movieId);

        Assert.True(result.Success);
        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshSubtitleFlagOnlyAsync_AlreadyScannedMovieGetsANewSubtitleFile_FlagStillUpdates()
    {
        // The exact bug this method was added to fix: a movie whose MediaInfo probe already
        // succeeded (MediaScannedAt set, no error) used to never get its subtitle flag rechecked,
        // because that only happened inside RefreshMediaInfoOnlyAsync, which LibraryRescanTask only
        // calls for movies still eligible for a MediaInfo retry.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.mp4"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.srt"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaScannedAt = DateTime.UtcNow, MediaHasSubtitleFile = false };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);

        await client.RefreshSubtitleFlagOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshSubtitleFlagOnlyAsync_NoLocalFolder_ReturnsFailureAndLeavesFlagUntouched()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "NOPE-001", MediaHasSubtitleFile = true };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);

        var result = await client.RefreshSubtitleFlagOnlyAsync(movieId);

        Assert.False(result.Success);
        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task ResolveMovieFolderPathAsync_ReturnsTheMoviesFolder_WhenItExists()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveMovieFolderPathAsync("ABC-123");

        Assert.Equal(folder, path, ignoreCase: true);
    }

    [Fact]
    public async Task ResolveMovieFolderPathAsync_ReturnsNull_WhenNoFolderMatches()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var client = CreateClientFor(factory, root);
        var path = await client.ResolveMovieFolderPathAsync("ZZZ-999");

        Assert.Null(path);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_OnlyTrailerPresent_FindsNoVideoFile()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("MDVR-099");
        await File.WriteAllTextAsync(Path.Combine(folder, "MDVR-099-trailer.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "MDVR-099" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        var client = CreateClientFor(factory, root, prober);

        var result = await client.RefreshMediaInfoOnlyAsync(movieId);

        Assert.False(result.Success);
        Assert.Equal("No video file found in the local folder.", result.ErrorMessage);
        await prober.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_NoVideoFile_SettlesAsWarningNotRetryableError()
    {
        // LibraryRescanTask retries any movie where MediaScannedAt is null or MediaScanError is
        // set — a movie whose folder genuinely has no video file yet would otherwise be retried
        // forever. This should record it as MediaScanWarning instead, with MediaScannedAt set and
        // MediaScanError left null, so the rescan task's own retry filter settles on it.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        root.AddMovieFolder("KAVR-438");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "KAVR-438", MediaScanError = "stale error from a previous real probe attempt" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var updated = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.NotNull(updated.MediaScannedAt);
        Assert.Null(updated.MediaScanError);
        Assert.Equal("No video file found in the local folder.", updated.MediaScanWarning);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_AlreadySucceededAndFileUnchanged_SkipsExpensiveProbe()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.mp4");
        await File.WriteAllTextAsync(moviePath, "x");
        var info = new FileInfo(moviePath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                MediaScannedAt = DateTime.UtcNow,
                MediaScanError = null,
                LocalFileSizeBytes = info.Length,
                MediaVideoFileLastWriteUtc = info.LastWriteTimeUtc,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        var client = CreateClientFor(factory, root, prober);

        var result = await client.RefreshMediaInfoOnlyAsync(movieId);

        Assert.True(result.Success);
        await prober.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_FileChangedSinceLastSuccess_ReProbesAndAppliesNewResolution()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.mp4");
        await File.WriteAllTextAsync(moviePath, "a bigger 4k file");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            // Stored size deliberately doesn't match the real file on disk — simulates the file
            // having been replaced (e.g. a 1080p rip swapped for a 4K one) since the last probe.
            var movie = new Movie
            {
                Code = "ABC-123",
                MediaScannedAt = DateTime.UtcNow,
                MediaScanError = null,
                LocalFileSizeBytes = 1,
                MediaVideoFileLastWriteUtc = DateTime.UtcNow.AddDays(-30),
                MediaWidth = 1920,
                MediaHeight = 1080,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 3840, Height = 2160 });
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        await prober.Received(1).ProbeAsync(moviePath, Arg.Any<CancellationToken>());
        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(3840, verifyDb.Movies.Single(m => m.Id == movieId).MediaWidth);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_FileAddedAtNeverObserved_SetsItFromVideoFileCreationTime()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.mp4");
        await File.WriteAllTextAsync(moviePath, "x");
        var creationTimeUtc = new FileInfo(moviePath).CreationTimeUtc;

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080 });
        var client = CreateClientFor(factory, root, prober);
        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(creationTimeUtc, verifyDb.Movies.Single(m => m.Id == movieId).FileAddedAt);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_FileAddedAtAlreadySet_NotOverwrittenByLaterReplaceProbe()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var moviePath = Path.Combine(folder, "ABC-123.mp4");
        await File.WriteAllTextAsync(moviePath, "a bigger 4k file");
        var originalFileAddedAt = DateTime.UtcNow.AddYears(-2);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            // Stored size deliberately doesn't match the real file — simulates a video-file
            // upgrade/replacement since the last probe, which must not reset FileAddedAt.
            var movie = new Movie
            {
                Code = "ABC-123",
                MediaScannedAt = DateTime.UtcNow,
                MediaScanError = null,
                LocalFileSizeBytes = 1,
                MediaVideoFileLastWriteUtc = DateTime.UtcNow.AddDays(-30),
                FileAddedAt = originalFileAddedAt,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 3840, Height = 2160 });
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(originalFileAddedAt, verifyDb.Movies.Single(m => m.Id == movieId).FileAddedAt);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_MultipleVersions_ProbesAllFilesAndPopulatesMovieFiles()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var origPath = Path.Combine(folder, "ABC-123.mp4");
        var rifePath = Path.Combine(folder, "ABC-123-RIFE-3.1.mkv");
        await File.WriteAllTextAsync(origPath, "original file content");
        await File.WriteAllTextAsync(rifePath, "rife 60fps file content here");
        var origInfo = new FileInfo(origPath);
        var rifeInfo = new FileInfo(rifePath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(origPath, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080, FrameRate = 29.97, VideoCodec = "AVC" });
        prober.ProbeAsync(rifePath, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080, FrameRate = 59.94, VideoCodec = "HEVC" });

        var client = CreateClientFor(factory, root, prober);
        var result = await client.RefreshMediaInfoOnlyAsync(movieId);

        Assert.True(result.Success);
        await prober.Received(1).ProbeAsync(origPath, Arg.Any<CancellationToken>());
        await prober.Received(1).ProbeAsync(rifePath, Arg.Any<CancellationToken>());

        using var verifyDb = factory.CreateDbContext();
        var movieFromDb = verifyDb.Movies.Include(m => m.MovieFiles).Single(m => m.Id == movieId);

        Assert.Equal(2, movieFromDb.FileCount);
        Assert.Equal(2, movieFromDb.MovieFiles.Count);
        Assert.Equal(origInfo.Length + rifeInfo.Length, movieFromDb.LocalFileSizeBytes);
        Assert.Equal("ABC-123.mp4", movieFromDb.MediaVideoFileName);

        var origFile = movieFromDb.MovieFiles.Single(f => f.FileName == "ABC-123.mp4");
        Assert.Equal("Original", origFile.VersionTag);
        Assert.True(origFile.IsPrimary);
        Assert.Equal("AVC", origFile.VideoCodec);
        Assert.Equal(origInfo.Length, origFile.FileSizeBytes);

        var rifeFile = movieFromDb.MovieFiles.Single(f => f.FileName == "ABC-123-RIFE-3.1.mkv");
        Assert.Equal("RIFE-3.1", rifeFile.VersionTag);
        Assert.False(rifeFile.IsPrimary);
        Assert.Equal("HEVC", rifeFile.VideoCodec);
        Assert.Equal(rifeInfo.Length, rifeFile.FileSizeBytes);
    }

    [Fact]
    public async Task RefreshLocalMetadataAsync_MultipleVersions_ProbesEachFileOnlyOnce()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var originalPath = Path.Combine(folder, "ABC-123.mp4");
        var rifePath = Path.Combine(folder, "ABC-123-RIFE-3.1.mkv");
        await File.WriteAllTextAsync(originalPath, "original file content");
        await File.WriteAllTextAsync(rifePath, "interpolated file content");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080 });
        var client = CreateClientFor(factory, root, prober);

        var result = await client.RefreshLocalMetadataAsync(movieId);

        Assert.True(result.Success);
        await prober.Received(1).ProbeAsync(originalPath, Arg.Any<CancellationToken>());
        await prober.Received(1).ProbeAsync(rifePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_MultipleVersions_OneFileChanged_OnlyReprobesChangedFile()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var origPath = Path.Combine(folder, "ABC-123.mp4");
        var rifePath = Path.Combine(folder, "ABC-123-RIFE-3.1.mkv");
        await File.WriteAllTextAsync(origPath, "original file content");
        await File.WriteAllTextAsync(rifePath, "rife 60fps file content here");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(origPath, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080 });
        prober.ProbeAsync(rifePath, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080 });

        var client = CreateClientFor(factory, root, prober);
        await client.RefreshMediaInfoOnlyAsync(movieId);

        // Modify only the RIFE file
        await File.AppendAllTextAsync(rifePath, " modified bytes for upscale v2");
        File.SetLastWriteTimeUtc(rifePath, DateTime.UtcNow.AddMinutes(5));
        var newRifeLength = new FileInfo(rifePath).Length;

        prober.ClearReceivedCalls();
        prober.ProbeAsync(rifePath, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 3840, Height = 2160 });

        var result = await client.RefreshMediaInfoOnlyAsync(movieId);

        Assert.True(result.Success);
        await prober.DidNotReceive().ProbeAsync(origPath, Arg.Any<CancellationToken>());
        await prober.Received(1).ProbeAsync(rifePath, Arg.Any<CancellationToken>());

        using var verifyDb = factory.CreateDbContext();
        var movieFromDb = verifyDb.Movies.Include(m => m.MovieFiles).Single(m => m.Id == movieId);
        var updatedRifeFile = movieFromDb.MovieFiles.Single(f => f.FileName == "ABC-123-RIFE-3.1.mkv");
        Assert.Equal(3840, updatedRifeFile.Width);
        Assert.Equal(newRifeLength, updatedRifeFile.FileSizeBytes);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_MultipleVersions_FileDeleted_RemovesFromMovieFiles()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var origPath = Path.Combine(folder, "ABC-123.mp4");
        var rifePath = Path.Combine(folder, "ABC-123-RIFE-3.1.mkv");
        await File.WriteAllTextAsync(origPath, "original file content");
        await File.WriteAllTextAsync(rifePath, "rife 60fps file content here");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 1920, Height = 1080 });

        var client = CreateClientFor(factory, root, prober);
        await client.RefreshMediaInfoOnlyAsync(movieId);

        // Delete the original file; now RIFE should remain and become primary
        File.Delete(origPath);

        var result = await client.RefreshMediaInfoOnlyAsync(movieId);

        Assert.True(result.Success);
        using var verifyDb = factory.CreateDbContext();
        var movieFromDb = verifyDb.Movies.Include(m => m.MovieFiles).Single(m => m.Id == movieId);
        Assert.Equal(1, movieFromDb.FileCount);
        Assert.Single(movieFromDb.MovieFiles);
        var remaining = movieFromDb.MovieFiles.Single();
        Assert.Equal("ABC-123-RIFE-3.1.mkv", remaining.FileName);
        Assert.True(remaining.IsPrimary);
        Assert.Equal("ABC-123-RIFE-3.1.mkv", movieFromDb.MediaVideoFileName);
    }

    [Fact]
    public async Task RefreshSubtitleFlagOnlyAsync_FlagFlipsToTrue_SetsFlag()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.srt"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaHasSubtitleFile = false };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshSubtitleFlagOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshSubtitleFlagOnlyAsync_FlagFlipsToFalse_ClearsFlag()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        root.AddMovieFolder("ABC-123");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaHasSubtitleFile = true };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshSubtitleFlagOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.False(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasSubtitleFile);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_TrailerFilePresent_SetsMediaHasTrailerFile()
    {
        // A "-trailer" file alongside the movie file.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.webm"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123-trailer.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasTrailerFile);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_NoTrailerFile_LeavesMediaHasTrailerFileFalse()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MediaProbeResult.Failed("not probed in this test"));
        var client = CreateClientFor(factory, root, prober);

        await client.RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.False(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasTrailerFile);
    }

    [Fact]
    public async Task RefreshTrailerFlagOnlyAsync_FlagFlipsToTrue_SetsFlag()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123-trailer.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaHasTrailerFile = false };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshTrailerFlagOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.True(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasTrailerFile);
    }

    [Fact]
    public async Task RefreshTrailerFlagOnlyAsync_FlagFlipsToFalse_ClearsFlag()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        root.AddMovieFolder("ABC-123");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaHasTrailerFile = true };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshTrailerFlagOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.False(verifyDb.Movies.Single(m => m.Id == movieId).MediaHasTrailerFile);
    }

    private const string SampleNfo = "<movie><title>{0}</title></movie>";

    [Fact]
    public async Task RefreshLocalMetadataIfNfoChangedAsync_FirstObservation_EstablishesBaselineWithoutApplying()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "ABC-123.nfo"), string.Format(SampleNfo, "From NFO"));

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            // Currently sourced from javinizer-go — a first-observation baseline must not clobber this.
            var movie = new Movie { Code = "ABC-123", MetaTitle = "From javinizer-go", MetaSourceName = "javinizer-go" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var movie2 = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.Equal("From javinizer-go", movie2.MetaTitle);
        Assert.NotNull(movie2.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task RefreshLocalMetadataIfNfoChangedAsync_NfoEditedSinceBaseline_PreservesAuthoritativeJavbuddyMetadata()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var nfoPath = Path.Combine(folder, "ABC-123.nfo");
        await File.WriteAllTextAsync(nfoPath, string.Format(SampleNfo, "Original Title"));

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            // A baseline from an earlier run, deliberately in the past relative to the edit below.
            var movie = new Movie { Code = "ABC-123", MetaTitle = "Authoritative In Javbuddy", MediaNfoLastWriteUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        await Task.Delay(10); // ensure a distinct, later last-write time than the baseline above
        await File.WriteAllTextAsync(nfoPath, string.Format(SampleNfo, "Edited Title On Disk"));

        var client = CreateClientFor(factory, root);
        await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var updated = verifyDb.Movies.Single(m => m.Id == movieId);
        // Javbuddy metadata is authoritative and must not be overwritten by .nfo on disk
        Assert.Equal("Authoritative In Javbuddy", updated.MetaTitle);
        Assert.True(updated.MediaNfoLastWriteUtc > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task RefreshLocalMetadataIfNfoChangedAsync_UnpopulatedMovie_PopulatesMetadataFromNfo()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var nfoPath = Path.Combine(folder, "ABC-123.nfo");
        await File.WriteAllTextAsync(nfoPath, string.Format(SampleNfo, "Title From NFO"));

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MediaNfoLastWriteUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var updated = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.Equal("Title From NFO", updated.MetaTitle);
    }

    [Fact]
    public async Task RefreshLocalMetadataIfNfoChangedAsync_Unchanged_NoOp()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var nfoPath = Path.Combine(folder, "ABC-123.nfo");
        await File.WriteAllTextAsync(nfoPath, string.Format(SampleNfo, "Some Title"));
        var writeTimeUtc = File.GetLastWriteTimeUtc(nfoPath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", MetaTitle = "Some Title", MediaNfoLastWriteUtc = writeTimeUtc };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var movie2 = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.Equal("Some Title", movie2.MetaTitle);
        Assert.Equal(writeTimeUtc, movie2.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task SyncImagesSignatureAsync_FirstObservation_EstablishesBaseline()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.SyncImagesSignatureAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.NotNull(verifyDb.Movies.Single(m => m.Id == movieId).MediaImagesSignature);
    }

    [Fact]
    public async Task SyncImagesSignatureAsync_PosterReplacedSinceBaseline_SignatureChanges()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var posterPath = Path.Combine(folder, "poster.jpg");
        await File.WriteAllTextAsync(posterPath, "original poster bytes");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.SyncImagesSignatureAsync(movieId); // establishes the baseline
        var baselineSignature = factory.CreateDbContext().Movies.Single(m => m.Id == movieId).MediaImagesSignature;

        await Task.Delay(10);
        await File.WriteAllTextAsync(posterPath, "a completely different, longer replacement poster");

        await client.SyncImagesSignatureAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.NotEqual(baselineSignature, verifyDb.Movies.Single(m => m.Id == movieId).MediaImagesSignature);
    }

    [Fact]
    public async Task SyncImagesSignatureAsync_Unchanged_SignatureStaysStable()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var client = CreateClientFor(factory, root);
        await client.SyncImagesSignatureAsync(movieId); // establishes the baseline
        var baselineSignature = factory.CreateDbContext().Movies.Single(m => m.Id == movieId).MediaImagesSignature;

        await client.SyncImagesSignatureAsync(movieId); // nothing changed since

        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(baselineSignature, verifyDb.Movies.Single(m => m.Id == movieId).MediaImagesSignature);
    }

    [Fact]
    public async Task TryGetMetadataAsync_ParsesCompleteActorMetadataFromNfo()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("SIVR-505");

        var nfoContent = """
            <movie>
                <title>SIVR-505 Title</title>
                <actor>
                    <name>Araki Noa</name>
                    <role>Actress</role>
                    <type>Actor</type>
                    <thumb>https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg</thumb>
                    <altname>新木希空</altname>
                    <aliases>
                        <alias>あずき</alias>
                        <alias>Azuki</alias>
                    </aliases>
                </actor>
                <actor>
                    <name>Yua Mikami</name>
                    <altname>三上悠亜</altname>
                    <aliases>鬼頭桃菜, Kito Momona</aliases>
                </actor>
                <actor>
                    <name>   </name>
                </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "SIVR-505.nfo"), nfoContent);

        var client = CreateClientFor(factory, root);
        var lookup = await client.TryGetMetadataAsync("SIVR-505");

        Assert.True(lookup.Found);
        Assert.NotNull(lookup.Metadata);
        var meta = lookup.Metadata!;

        Assert.Equal(2, meta.Actors.Count);
        Assert.Equal(["Araki Noa", "Yua Mikami"], meta.Actresses);

        var a1 = meta.Actors[0];
        Assert.Equal("Araki Noa", a1.Name);
        Assert.Equal("Actress", a1.Role);
        Assert.Equal("Actor", a1.Type);
        Assert.Equal("https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg", a1.Thumb);
        Assert.Equal("新木希空", a1.AltName);
        Assert.Equal(["あずき", "Azuki"], a1.Aliases);

        var a2 = meta.Actors[1];
        Assert.Equal("Yua Mikami", a2.Name);
        Assert.Null(a2.Role);
        Assert.Null(a2.Type);
        Assert.Null(a2.Thumb);
        Assert.Equal("三上悠亜", a2.AltName);
        Assert.Equal(["鬼頭桃菜", "Kito Momona"], a2.Aliases);
    }

    [Fact]
    public async Task GetMovieActorsAsync_ReturnsActorsFromNfo()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("MIDE-400");

        var nfoContent = """
            <movie>
                <actor>
                    <name>Nishinomiya Konomi</name>
                    <role>Actress</role>
                    <thumb></thumb>
                </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "MIDE-400.nfo"), nfoContent);

        var client = CreateClientFor(factory, root);
        var actors = await client.GetMovieActorsAsync("MIDE-400");

        var actor = Assert.Single(actors);
        Assert.Equal("Nishinomiya Konomi", actor.Name);
        Assert.Equal("Actress", actor.Role);
        Assert.Null(actor.Thumb);
    }

    [Fact]
    public async Task GetMovieActorsAsync_ClearsDuplicatedAltNamesAcrossActorsInSameMovie()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("MULTI-001");

        var nfoContent = """
            <movie>
                <actor>
                    <name>Gamma Beta</name>
                    <altname>橋本ありな</altname>
                </actor>
                <actor>
                    <name>Yamagishi Ayaka</name>
                    <altname>橋本ありな</altname>
                </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "MULTI-001.nfo"), nfoContent);

        var client = CreateClientFor(factory, root);
        var actors = await client.GetMovieActorsAsync("MULTI-001");

        Assert.Equal(2, actors.Count);
        Assert.Equal("Gamma Beta", actors[0].Name);
        Assert.Null(actors[0].AltName);
        Assert.Equal("Yamagishi Ayaka", actors[1].Name);
        Assert.Null(actors[1].AltName);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_MatchesUnderscoreVariantAndCandidateNames()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("T28-520");
        var actorsDir = Path.Combine(folder, ".actors");
        Directory.CreateDirectory(actorsDir);

        var imagePath = Path.Combine(actorsDir, "Hosisaki_Remi.jpg");
        await File.WriteAllTextAsync(imagePath, "x");

        var client = CreateClientFor(factory, root);

        // Should resolve when passing "Hosisaki Remi"
        var resolved = await client.ResolveActorImagePathAsync("T28-520", "Hosisaki Remi");
        Assert.Equal(imagePath, resolved);

        // Should resolve when candidate list contains Japanese Kanji, swapped name, or display name
        var resolvedCandidates = await client.ResolveActorImagePathAsync("T28-520", ["星崎レミ", "Remi Hosisaki", "Hosisaki Remi"]);
        Assert.Equal(imagePath, resolvedCandidates);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_MatchesHyphensDotsAndSwappedOrderAutomatically()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        var actorsDir = Path.Combine(folder, ".actors");
        Directory.CreateDirectory(actorsDir);

        // File uses hyphenated swapped order: Remi-Hosisaki.jpg
        var hyphenFile = Path.Combine(actorsDir, "Remi-Hosisaki.jpg");
        await File.WriteAllTextAsync(hyphenFile, "x");

        var client = CreateClientFor(factory, root);

        // Caller only provides "Hosisaki Remi"
        var resolved = await client.ResolveActorImagePathAsync("ABC-123", "Hosisaki Remi");
        Assert.Equal(hyphenFile, resolved);

        // File uses dotted swapped order: Remi.Hosisaki.jpg
        File.Delete(hyphenFile);
        var dotFile = Path.Combine(actorsDir, "Remi.Hosisaki.png");
        await File.WriteAllTextAsync(dotFile, "x");

        var dotResolved = await client.ResolveActorImagePathAsync("ABC-123", "Hosisaki Remi");
        Assert.Equal(dotFile, dotResolved);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_MatchesCaseInsensitiveAndNonHiddenActorDirectory()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("DEF-456");

        // Directory named "actors" without leading dot
        var actorsDir = Path.Combine(folder, "actors");
        Directory.CreateDirectory(actorsDir);

        var imageFile = Path.Combine(actorsDir, "Alpha.jpg");
        await File.WriteAllTextAsync(imageFile, "x");

        var client = CreateClientFor(factory, root);
        var resolved = await client.ResolveActorImagePathAsync("DEF-456", "Alpha");
        Assert.Equal(imageFile, resolved);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_EnrichesFromNfo_WhenCallerHasRomanizedNameAndFileIsJapanese()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("SIVR-505");
        var actorsDir = Path.Combine(folder, ".actors");
        Directory.CreateDirectory(actorsDir);

        // File on disk has Japanese characters
        var imageFile = Path.Combine(actorsDir, "新木希空.jpg");
        await File.WriteAllTextAsync(imageFile, "x");

        // NFO maps Romanized name "Araki Noa" to Japanese altname "新木希空"
        var nfoContent = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SIVR-505</title>
              <actor>
                <name>Araki Noa</name>
                <altname>新木希空</altname>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "SIVR-505.nfo"), nfoContent);

        var client = CreateClientFor(factory, root);

        // Caller only provides "Araki Noa"
        var resolved = await client.ResolveActorImagePathAsync("SIVR-505", "Araki Noa");
        Assert.Equal(imageFile, resolved);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_MatchesSubdirectoryNamedAfterActor()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("GHI-789");
        var subDir = Path.Combine(folder, ".actors", "Gamma Beta");
        Directory.CreateDirectory(subDir);

        var imageFile = Path.Combine(subDir, "photo.jpg");
        await File.WriteAllTextAsync(imageFile, "x");

        var client = CreateClientFor(factory, root);
        var resolved = await client.ResolveActorImagePathAsync("GHI-789", "Gamma Beta");
        Assert.Equal(imageFile, resolved);
    }

    [Fact]
    public async Task FindMovieFolderAsync_ResolvesNestedDirectoriesAndDiscoversCodes()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        // Nested directory structure: root/S1/BBB-002
        var studioDir = Path.Combine(root.Path, "S1");
        var movieDir = Path.Combine(studioDir, "BBB-002");
        Directory.CreateDirectory(movieDir);
        await File.WriteAllTextAsync(Path.Combine(movieDir, "BBB-002.nfo"), "<movie></movie>");

        var client = CreateClientFor(factory, root);

        // FindMovieFolderAsync should find nested directory
        var resolvedDir = await client.ResolveLiveLocalFilePathAsync("BBB-002", "poster.jpg");
        // Also ListMovieCodesAsync should discover BBB-002
        var codes = await client.ListMovieCodesAsync();
        Assert.Contains("BBB-002", codes);
    }

    [Fact]
    public async Task ResolveActorImagePathAsync_MatchesActorFileLayouts()
    {
        // Typical .actors layouts, recreated in a temp root.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        foreach (var (code, fileName) in new[]
        {
            ("AAA-001", "Alpha.jpg"),
            ("BBB-002", "Beta_Gamma.jpg"),
            ("BBB-002", "Gamma_Beta.jpg"),
            ("CCC-003", "Delta Epsilon.jpg"),
        })
        {
            var actorsDir = Path.Combine(root.Path, code, ".actors");
            Directory.CreateDirectory(actorsDir);
            await File.WriteAllTextAsync(Path.Combine(actorsDir, fileName), "jpg");
        }

        var client = CreateClientFor(factory, root);

        // 1. AAA-001 has .actors/Alpha.jpg
        var tsubomi = await client.ResolveActorImagePathAsync("AAA-001", "Alpha");
        Assert.NotNull(tsubomi);
        Assert.True(File.Exists(tsubomi));
        Assert.Equal("Alpha.jpg", Path.GetFileName(tsubomi));

        // 2. BBB-002 has .actors/Beta_Gamma.jpg and .actors/Gamma_Beta.jpg
        var arina1 = await client.ResolveActorImagePathAsync("BBB-002", "Gamma Beta");
        Assert.NotNull(arina1);
        Assert.True(File.Exists(arina1));

        var arina2 = await client.ResolveActorImagePathAsync("BBB-002", "Beta Gamma");
        Assert.NotNull(arina2);
        Assert.True(File.Exists(arina2));

        // 3. CCC-003 has .actors/Delta Epsilon.jpg
        var momose = await client.ResolveActorImagePathAsync("CCC-003", "Delta Epsilon");
        Assert.NotNull(momose);
        Assert.True(File.Exists(momose));
        Assert.Equal("Delta Epsilon.jpg", Path.GetFileName(momose));
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_DetectsEachVersionsVrFormat_AndThePrimarysBecomesTheMovies()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("SIVR-059");
        await File.WriteAllTextAsync(Path.Combine(folder, "SIVR-059.mp4"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "SIVR-059-4K.3d.htab.mp4"), "x");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SIVR-059" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        prober.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Success = true, Width = 3840, Height = 1920 });
        await CreateClientFor(factory, root, prober).RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        var reloaded = verifyDb.Movies.Include(m => m.MovieFiles).Single(m => m.Id == movieId);
        var files = reloaded.MovieFiles.ToDictionary(f => f.FileName);
        Assert.Equal(("Original", "VR180 SBS"), (files["SIVR-059.mp4"].VersionTag, files["SIVR-059.mp4"].VrType));
        Assert.Equal(("4K", "3D HTAB"), (files["SIVR-059-4K.3d.htab.mp4"].VersionTag, files["SIVR-059-4K.3d.htab.mp4"].VrType));
        Assert.Equal("VR180 SBS", reloaded.VrType);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_UnchangedFiles_StillGetTheirFormat_AndAPinnedOneIsKept()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("SIVR-059");
        var flatPath = Path.Combine(folder, "SIVR-059.3d.hsbs.mp4");
        var pinnedPath = Path.Combine(folder, "SIVR-059-B_180_sbs.mp4");
        await File.WriteAllTextAsync(flatPath, "x");
        await File.WriteAllTextAsync(pinnedPath, "x");
        var flatInfo = new FileInfo(flatPath);
        var pinnedInfo = new FileInfo(pinnedPath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            // Probed before: no format yet, the old version tag, and nothing changed on disk.
            var movie = new Movie { Code = "SIVR-059", MediaScannedAt = DateTime.UtcNow };
            movie.MovieFiles.Add(new MovieFile
            {
                FileName = flatInfo.Name,
                VersionTag = "3d.hsbs",
                IsPrimary = true,
                FileSizeBytes = flatInfo.Length,
                LastWriteUtc = flatInfo.LastWriteTimeUtc,
                MediaScannedAt = DateTime.UtcNow,
                Width = 1920,
                Height = 1080,
            });
            movie.MovieFiles.Add(new MovieFile
            {
                FileName = pinnedInfo.Name,
                VersionTag = "B",
                FileSizeBytes = pinnedInfo.Length,
                LastWriteUtc = pinnedInfo.LastWriteTimeUtc,
                MediaScannedAt = DateTime.UtcNow,
                VrType = null,
                VrTypePinned = true,
            });
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var prober = Substitute.For<IMediaInfoProber>();
        await CreateClientFor(factory, root, prober).RefreshMediaInfoOnlyAsync(movieId);

        await prober.DidNotReceiveWithAnyArgs().ProbeAsync(default!, default);
        using var verifyDb = factory.CreateDbContext();
        var reloaded = verifyDb.Movies.Include(m => m.MovieFiles).Single(m => m.Id == movieId);
        var files = reloaded.MovieFiles.ToDictionary(f => f.FileName);
        Assert.Equal(("Original", "3D HSBS"), (files[flatInfo.Name].VersionTag, files[flatInfo.Name].VrType));
        Assert.Equal(("B", null), (files[pinnedInfo.Name].VersionTag, files[pinnedInfo.Name].VrType));
        Assert.Equal("3D HSBS", reloaded.VrType);
    }

    [Fact]
    public async Task RefreshMediaInfoOnlyAsync_NoVideoFileAnyMore_ClearsTheMoviesFormat()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        root.AddMovieFolder("SIVR-059");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SIVR-059", VrType = "VR180 SBS" };
            movie.MovieFiles.Add(new MovieFile { FileName = "SIVR-059.mp4", IsPrimary = true, VrType = "VR180 SBS" });
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        await CreateClientFor(factory, root).RefreshMediaInfoOnlyAsync(movieId);

        using var verifyDb = factory.CreateDbContext();
        Assert.Null(verifyDb.Movies.Single(m => m.Id == movieId).VrType);
    }
}
