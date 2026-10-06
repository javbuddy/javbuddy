using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class ImageCacheTaskTests
{
    private sealed class TempCacheRoot : ILocalImageCache, IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "javbuddy-tests-cache-" + Guid.NewGuid().ToString("N"));

        public int QualityFull => ImageConverter.WebPQualityFull;

        public int QualityThumb => ImageConverter.WebPQualityThumb;

        public TempCacheRoot() => Directory.CreateDirectory(RootPath);

        // Deliberately unsharded (unlike the real LocalImageCache) — a test double only needs a
        // stable, unique path per StorageId, not the real sharding scheme.
        public string GetStoragePath(Guid storageId) => Path.Combine(RootPath, storageId.ToString("N") + ".webp");

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static IProgress<TaskProgress> NoopProgress => new Progress<TaskProgress>();

    private static ILocalLibraryClient CreateLocalLibraryClient()
    {
        var client = Substitute.For<ILocalLibraryClient>();
        client.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());
        client.ListExtraFanartFileNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<string>());
        return client;
    }

    // Each default stub is only applied when the caller didn't already supply (and presumably
    // pre-configure) that dependency itself — NSubstitute has "most recently configured wins" for
    // an overlapping matcher, so unconditionally re-stubbing an Arg.Any<> catch-all here would
    // silently clobber a caller's own more specific setup (e.g. ListExtraFanartFileNamesAsync for
    // one particular code) done before calling this helper.
    private static ImageCacheTask CreateTask(
        TestDbContextFactory factory,
        TempCacheRoot cache,
        ILocalImageCacheService? movieImageCacheService = null,
        IActorImageCacheService? actorImageCacheService = null,
        ILocalLibraryClient? localLibraryClient = null,
        IActorPhotoService? actorPhotoService = null)
    {
        if (movieImageCacheService is null)
        {
            movieImageCacheService = Substitute.For<ILocalImageCacheService>();
            movieImageCacheService.GetOrCreateBothAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(((string?)null, (string?)null));
        }

        if (actorImageCacheService is null)
        {
            actorImageCacheService = Substitute.For<IActorImageCacheService>();
            actorImageCacheService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(((string?)null, (string?)null));
        }

        if (localLibraryClient is null)
        {
            localLibraryClient = CreateLocalLibraryClient();
            localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>())
                .Returns(new List<string>());
            localLibraryClient.ListExtraFanartFileNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<string>());
        }

        if (actorPhotoService is null)
        {
            actorPhotoService = Substitute.For<IActorPhotoService>();
            actorPhotoService.GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(((string?)null, (string?)null));
        }

        return new ImageCacheTask(localLibraryClient, movieImageCacheService, actorImageCacheService, cache, factory, actorPhotoService: actorPhotoService);
    }

    [Fact]
    public async Task RunAsync_CachesEveryTrackedMovieAndActor()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.Actors.Add(new Actor { FirstName = "Someone" });
            db.SaveChanges();
        }

        var movieService = Substitute.For<ILocalImageCacheService>();
        var actorService = Substitute.For<IActorImageCacheService>();
        var task = CreateTask(factory, cache, movieService, actorService);

        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await movieService.Received(1).GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>());
        await movieService.Received(1).GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>());
        await actorService.Received(1).GetOrCreateBothAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Contains("1 movies / 1 actors", summary);
    }

    [Fact]
    public async Task RunAsync_CachesEveryActorPhoto()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        int photoId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Someone" };
            db.Actors.Add(actor);
            db.SaveChanges();
            var photo = new ActorPhoto { ActorId = actor.Id };
            db.ActorPhotos.Add(photo);
            db.SaveChanges();
            photoId = photo.Id;
        }

        var photoService = Substitute.For<IActorPhotoService>();
        photoService.GetOrCreateBothAsync(photoId, Arg.Any<CancellationToken>())
            .Returns(("/path/thumb.webp", "/path/full.webp"));
        var task = CreateTask(factory, cache, actorPhotoService: photoService);

        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await photoService.Received(1).GetOrCreateBothAsync(photoId, Arg.Any<CancellationToken>());
        Assert.Contains("1 photos", summary);
        Assert.Contains("2 images cached/up to date", summary);
    }

    [Fact]
    public async Task RunAsync_ActorPhotoFailure_SkipsPhotoAndReportsFailure()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        int photoId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Someone" };
            db.Actors.Add(actor);
            db.SaveChanges();
            var photo = new ActorPhoto { ActorId = actor.Id };
            db.ActorPhotos.Add(photo);
            db.SaveChanges();
            photoId = photo.Id;
        }

        var photoService = Substitute.For<IActorPhotoService>();
        photoService.GetOrCreateBothAsync(photoId, Arg.Any<CancellationToken>())
            .Returns<(string?, string?)>(_ => throw new IOException("Disk failure"));
        var task = CreateTask(factory, cache, actorPhotoService: photoService);

        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.Contains("1 photos", summary);
        Assert.Contains("2 failed", summary);
    }

    [Fact]
    public async Task RunAsync_ActorImageDecodeFailure_SkipsThatActorAndContinuesWithTheRest()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        int badActorId, goodActorId;
        using (var db = factory.CreateDbContext())
        {
            var bad = new Actor { FirstName = "Bad" };
            var good = new Actor { FirstName = "Good" };
            db.Actors.AddRange(bad, good);
            db.SaveChanges();
            badActorId = bad.Id;
            goodActorId = good.Id;
        }

        var actorService = Substitute.For<IActorImageCacheService>();
        actorService.GetOrCreateBothAsync(badActorId, Arg.Any<CancellationToken>())
            .Returns<(string?, string?)>(_ => throw new InvalidOperationException("Could not decode image: bad.jpg"));
        actorService.GetOrCreateBothAsync(goodActorId, Arg.Any<CancellationToken>())
            .Returns(("thumb-path", "full-path"));

        var task = CreateTask(factory, cache, actorImageCacheService: actorService);

        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await actorService.Received(1).GetOrCreateBothAsync(goodActorId, Arg.Any<CancellationToken>());
        Assert.Contains("2 actors", summary);
        Assert.Contains("failed", summary);
    }

    [Fact]
    public async Task RunAsync_MovieImageDecodeFailure_SkipsThatRoleAndContinuesWithTheRest()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.SaveChanges();
        }

        var movieService = Substitute.For<ILocalImageCacheService>();
        movieService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
            .Returns<(string?, string?)>(_ => throw new InvalidOperationException("Could not decode image: poster.jpg"));
        movieService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>())
            .Returns(("thumb-path", "full-path"));

        var task = CreateTask(factory, cache, movieImageCacheService: movieService);

        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        await movieService.Received(1).GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>());
        Assert.Contains("failed", summary);
    }

    [Fact]
    public async Task RunAsync_PrunesCachedImagesForAMovieNoLongerTracked()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var storageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.CachedImages.Add(new CachedImage { Code = "GONE-001", Role = "poster", Variant = "thumb", StorageId = storageId });
            db.SaveChanges();
        }

        var task = CreateTask(factory, cache);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.CachedImages);
        Assert.Contains("pruned 1 orphaned rows", summary);
    }

    [Fact]
    public async Task RunAsync_PrunesActorImagesForAnActorNoLongerTracked()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        using (var db = factory.CreateDbContext())
        {
            db.ActorImages.Add(new ActorImage { ActorId = 999, SourceMovieCode = "ABC-123", Variant = "thumb", StorageId = Guid.NewGuid() });
            db.SaveChanges();
        }

        var task = CreateTask(factory, cache);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.ActorImages);
        Assert.Contains("pruned 1 orphaned rows", summary);
    }

    [Fact]
    public async Task RunAsync_PrunesBothKindsInOnePass_WithACombinedRowCount()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        using (var db = factory.CreateDbContext())
        {
            db.CachedImages.Add(new CachedImage { Code = "GONE-001", Role = "poster", Variant = "thumb", StorageId = Guid.NewGuid() });
            db.ActorImages.Add(new ActorImage { ActorId = 999, SourceMovieCode = "ABC-123", Variant = "thumb", StorageId = Guid.NewGuid() });
            db.SaveChanges();
        }

        var task = CreateTask(factory, cache);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.CachedImages);
        Assert.Empty(verifyDb.ActorImages);
        Assert.Contains("pruned 2 orphaned rows", summary);
    }

    [Fact]
    public async Task RunAsync_SweepsAStrayFileNeitherTableReferences_ButKeepsBothLiveKinds()
    {
        // The core reason these two tasks were combined: a single sweep computed from BOTH tables'
        // live StorageIds at once, instead of two separate sweeps each having to separately query
        // the other task's table to avoid deleting its files.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();

        var liveMovieImageId = Guid.NewGuid();
        var liveActorImageId = Guid.NewGuid();
        var strayId = Guid.NewGuid();

        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.Actors.Add(new Actor { FirstName = "Someone" });
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = "poster", Variant = "thumb", StorageId = liveMovieImageId });
            db.ActorImages.Add(new ActorImage { ActorId = 1, SourceMovieCode = "ABC-123", Variant = "thumb", StorageId = liveActorImageId });
            db.SaveChanges();
        }

        await File.WriteAllTextAsync(cache.GetStoragePath(liveMovieImageId), "movie image bytes");
        await File.WriteAllTextAsync(cache.GetStoragePath(liveActorImageId), "actor image bytes");
        await File.WriteAllTextAsync(cache.GetStoragePath(strayId), "orphaned bytes");

        // Its local source still exists (not the sourceless-poster case covered by a dedicated
        // test below), so the poster row must not be pruned as stale.
        var movieImageCacheService = Substitute.For<ILocalImageCacheService>();
        movieImageCacheService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
            .Returns(("thumb-path", "full-path"));
        movieImageCacheService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var task = CreateTask(factory, cache, movieImageCacheService: movieImageCacheService);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        Assert.True(File.Exists(cache.GetStoragePath(liveMovieImageId)), "a live movie image must survive the sweep");
        Assert.True(File.Exists(cache.GetStoragePath(liveActorImageId)), "a live actor image must survive the sweep");
        Assert.False(File.Exists(cache.GetStoragePath(strayId)), "a stray file referenced by neither table should be swept");
        Assert.Contains("1 orphaned files", summary);
    }

    [Fact]
    public async Task RunAsync_SweepsAnAnimatedWebpPreview_ButKeepsItsWebmVideo()
    {
        // A preview row now expects a .webm under its storage id, so the animated WebP
        // written there before is stale even though its storage id is still referenced.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        ILocalImageCache imageCache = cache;

        var preview = new CachedImage { Code = "ABC-123", Role = "scene", Index = 1, Variant = "preview", StorageId = Guid.NewGuid() };
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.CachedImages.Add(preview);
            db.SaveChanges();
        }
        var legacyPath = imageCache.GetStoragePath(preview.StorageId);
        var videoPath = imageCache.GetStoragePath(preview);
        await File.WriteAllTextAsync(legacyPath, "animated webp bytes");
        await File.WriteAllTextAsync(videoPath, "webm bytes");

        var movieImageCacheService = Substitute.For<ILocalImageCacheService>();
        movieImageCacheService.GetOrCreateBothAsync(default!, default!, default, default).ReturnsForAnyArgs(((string?)null, (string?)null));

        var summary = await CreateTask(factory, cache, movieImageCacheService: movieImageCacheService).RunAsync(CancellationToken.None, NoopProgress);

        Assert.Equal(".webm", Path.GetExtension(videoPath));
        Assert.True(File.Exists(videoPath), "the preview video must survive the sweep");
        Assert.False(File.Exists(legacyPath), "the old animated WebP preview should be swept");
        Assert.Contains("1 orphaned files", summary);
    }

    [Fact]
    public async Task RunAsync_PosterWithNoLocalSourceOnAStillTrackedMovie_IsPruned()
    {
        // The movie itself is still tracked, but its poster.jpg was deleted on disk — the stale
        // CachedImages row/file must be pruned even though the code stays live.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var storageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = LocalImageCacheService.RolePoster, Variant = "thumb", StorageId = storageId });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(storageId), "stale poster bytes");

        var movieImageCacheService = Substitute.For<ILocalImageCacheService>();
        movieImageCacheService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null)); // no local source: poster.jpg is gone
        movieImageCacheService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null));

        var task = CreateTask(factory, cache, movieImageCacheService: movieImageCacheService);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.CachedImages);
        Assert.False(File.Exists(cache.GetStoragePath(storageId)), "the stale poster file should be swept");
        Assert.Contains("pruned 1 orphaned rows", summary);
    }

    [Fact]
    public async Task RunAsync_ExtrafanartBeyondCurrentLiveCount_IsPruned()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = LocalImageCacheService.RoleExtraFanart, Index = 0, Variant = "thumb", StorageId = Guid.NewGuid() });
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = LocalImageCacheService.RoleExtraFanart, Index = 1, Variant = "thumb", StorageId = Guid.NewGuid() });
            db.SaveChanges();
        }

        var localLibraryClient = CreateLocalLibraryClient();
        // Only one extrafanart file exists live now (index 0) — index 1's row is stale.
        localLibraryClient.ListExtraFanartFileNamesAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "fanart1.jpg" });

        var task = CreateTask(factory, cache, localLibraryClient: localLibraryClient);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        var remaining = Assert.Single(verifyDb.CachedImages);
        Assert.Equal(0, remaining.Index);
    }

    [Fact]
    public async Task RunAsync_MissingMovieWithCustomOrRemoteSourceUrl_IsNotPrunedAsSourceless()
    {
        // A Missing movie with a remote or custom cropped poster has SourceUrl set.
        // It has no local source file, but must NEVER be pruned as sourceless by ImageCacheTask.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var storageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "MISSING-001", Status = MovieStatus.Missing });
            db.CachedImages.Add(new CachedImage
            {
                Code = "MISSING-001",
                Role = LocalImageCacheService.RolePoster,
                Variant = "thumb",
                StorageId = storageId,
                SourceUrl = "custom",
            });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(storageId), "custom cropped poster bytes");

        var movieImageCacheService = Substitute.For<ILocalImageCacheService>();
        movieImageCacheService.GetOrCreateBothAsync("MISSING-001", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null)); // no local source: Missing movie

        var task = CreateTask(factory, cache, movieImageCacheService: movieImageCacheService);
        var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        var kept = Assert.Single(verifyDb.CachedImages);
        Assert.Equal("MISSING-001", kept.Code);
        Assert.True(File.Exists(cache.GetStoragePath(storageId)), "custom cropped poster file must be preserved");
    }

    [Fact]
    public async Task RunAsync_WhenAllConfiguredRootsAreOffline_SkipsSourcelessPruning()
    {
        // When configured media library roots are unmounted/offline, sourceless images must not be wiped.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var storageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.CachedImages.Add(new CachedImage
            {
                Code = "ABC-123",
                Role = LocalImageCacheService.RolePoster,
                Variant = "thumb",
                StorageId = storageId,
            });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(storageId), "live poster bytes");

        var localLibraryClient = CreateLocalLibraryClient();
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "/nonexistent/unmounted/share/path" });

        var movieImageCacheService = Substitute.For<ILocalImageCacheService>();
        movieImageCacheService.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)null)); // offline share -> returns null

        var task = CreateTask(factory, cache, movieImageCacheService: movieImageCacheService, localLibraryClient: localLibraryClient);
        await task.RunAsync(CancellationToken.None, NoopProgress);

        using var verifyDb = factory.CreateDbContext();
        var kept = Assert.Single(verifyDb.CachedImages);
        Assert.Equal("ABC-123", kept.Code);
        Assert.True(File.Exists(cache.GetStoragePath(storageId)), "cached poster must be preserved when roots are offline");
    }
}
