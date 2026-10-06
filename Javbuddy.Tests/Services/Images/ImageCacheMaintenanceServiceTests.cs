using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Images;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Images;

public class ImageCacheMaintenanceServiceTests
{
    private sealed class TempCacheRoot : ILocalImageCache, IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "javbuddy-tests-cache-" + Guid.NewGuid().ToString("N"));

        public int QualityFull => ImageConverter.WebPQualityFull;

        public int QualityThumb => ImageConverter.WebPQualityThumb;

        public ImageCacheSettings Settings { get; init; } = ImageCacheSettings.Default;

        public TempCacheRoot() => Directory.CreateDirectory(RootPath);

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

    [Fact]
    public async Task GetStatsAsync_CountsRowsAndOnDiskFiles()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();

        var movieImageId = Guid.NewGuid();
        var actorImageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = "poster", Variant = "thumb", StorageId = movieImageId });
            db.ActorImages.Add(new ActorImage { ActorId = 1, SourceMovieCode = "ABC-123", Variant = "thumb", StorageId = actorImageId });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(movieImageId), "abcdefghij"); // 10 bytes
        await File.WriteAllTextAsync(cache.GetStoragePath(actorImageId), "abcde"); // 5 bytes

        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        var stats = await service.GetStatsAsync();

        Assert.Equal(1, stats.MovieImageRows);
        Assert.Equal(0, stats.ActorImageRows);
        Assert.Equal(2, stats.FileCount);
        Assert.Equal(15, stats.TotalBytes);
    }

    [Fact]
    public async Task GetStatsAsync_CountsPreviewVideos()
    {
        // Scene/highlight hover previews are .webm files in the same cache.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        ILocalImageCache imageCache = cache;

        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid()), "abcdefghij"); // 10 bytes
        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid(), "preview"), "abcde"); // 5 bytes

        var stats = await new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker()).GetStatsAsync();

        Assert.Equal(2, stats.FileCount);
        Assert.Equal(15, stats.TotalBytes);
    }

    [Fact]
    public async Task GetStatsAsync_SplitsStillImagesFromPreviewVideosAndTimelines()
    {
        // The .webm previews are reported apart from the .webp stills, so
        // are the .jpg DeoVR timeline mosaics.
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        ILocalImageCache imageCache = cache;

        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid()), "abcdefghij"); // 10 bytes
        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid()), "abc"); // 3 bytes
        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid(), "preview"), "abcde"); // 5 bytes
        await File.WriteAllTextAsync(imageCache.GetStoragePath(Guid.NewGuid(), DeoVrTimeline.CacheVariant), "abcdefg"); // 7 bytes

        var stats = await new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker()).GetStatsAsync();

        Assert.Equal(2, stats.StillImageFileCount);
        Assert.Equal(13, stats.StillImageBytes);
        Assert.Equal(1, stats.PreviewFileCount);
        Assert.Equal(5, stats.PreviewBytes);
        Assert.Equal(1, stats.TimelineFileCount);
        Assert.Equal(7, stats.TimelineBytes);
        Assert.Equal(4, stats.FileCount);
        Assert.Equal(25, stats.TotalBytes);
    }

    [Fact]
    public async Task PurgeAsync_RemovesTimelineMosaics()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        ILocalImageCache imageCache = cache;
        var path = imageCache.GetStoragePath(Guid.NewGuid(), DeoVrTimeline.CacheVariant);
        await File.WriteAllTextAsync(path, "mosaic");

        await new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker()).PurgeAsync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task PurgeAsync_RemovesCacheFilesButRetainsActorImageMetadata()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();

        var movieImageId = Guid.NewGuid();
        var actorImageId = Guid.NewGuid();
        var actorPhotoThumbId = Guid.NewGuid();
        var actorPhotoFullId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" });
            db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = "poster", Variant = "thumb", StorageId = movieImageId });
            db.ActorImages.Add(new ActorImage { ActorId = 1, SourceMovieCode = "ABC-123", Variant = "thumb", StorageId = actorImageId });
            db.ActorPhotos.Add(new ActorPhoto { ActorId = 1, ThumbStorageId = actorPhotoThumbId, FullStorageId = actorPhotoFullId });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(movieImageId), "movie bytes");
        await File.WriteAllTextAsync(cache.GetStoragePath(actorImageId), "actor bytes");
        await File.WriteAllTextAsync(cache.GetStoragePath(actorPhotoThumbId), "photo thumb");
        await File.WriteAllTextAsync(cache.GetStoragePath(actorPhotoFullId), "photo full");

        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        await service.PurgeAsync();

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.CachedImages);
        Assert.Single(verifyDb.ActorImages);
        Assert.Single(verifyDb.ActorPhotos);
        Assert.False(File.Exists(cache.GetStoragePath(movieImageId)));
        Assert.False(File.Exists(cache.GetStoragePath(actorImageId)));
        Assert.False(File.Exists(cache.GetStoragePath(actorPhotoThumbId)));
        Assert.False(File.Exists(cache.GetStoragePath(actorPhotoFullId)));

        var stats = await service.GetStatsAsync();
        Assert.Equal(0, stats.FileCount);
        Assert.Equal(0, stats.TotalBytes);
    }

    [Fact]
    public async Task EnforceSizeLimitAsync_EvictsOldestFileAndItsRow()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot { Settings = ImageCacheSettings.Default with { MaxSizeBytes = 10 } };
        var oldStorageId = Guid.NewGuid();
        var newStorageId = Guid.NewGuid();
        using (var db = factory.CreateDbContext())
        {
            db.CachedImages.Add(new CachedImage { Code = "OLD-001", Role = "poster", Variant = "thumb", StorageId = oldStorageId });
            db.CachedImages.Add(new CachedImage { Code = "NEW-001", Role = "poster", Variant = "thumb", StorageId = newStorageId });
            db.SaveChanges();
        }
        await File.WriteAllTextAsync(cache.GetStoragePath(oldStorageId), "12345678");
        await File.WriteAllTextAsync(cache.GetStoragePath(newStorageId), "abcdefgh");
        File.SetLastWriteTimeUtc(cache.GetStoragePath(oldStorageId), DateTime.UtcNow.AddMinutes(-1));

        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        await service.EnforceSizeLimitAsync(0);

        Assert.False(File.Exists(cache.GetStoragePath(oldStorageId)));
        Assert.True(File.Exists(cache.GetStoragePath(newStorageId)));
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.DoesNotContain(verifyDb.CachedImages, image => image.StorageId == oldStorageId);
        Assert.Contains(verifyDb.CachedImages, image => image.StorageId == newStorageId);
    }

    [Fact]
    public async Task EnforceSizeLimitAsync_EvictsOldestFilesDownToTargetRatio()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot { Settings = ImageCacheSettings.Default with { MaxSizeBytes = 100 } };
        var storageIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < storageIds.Count; i++)
        {
            var path = cache.GetStoragePath(storageIds[i]);
            await File.WriteAllTextAsync(path, new string('x', 20));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i - storageIds.Count));
        }

        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        await service.EnforceSizeLimitAsync(0);

        // 120 bytes against a 100-byte limit: evicting to the limit would drop only the oldest file,
        // evicting to 90% of it drops the two oldest (80 bytes left).
        Assert.False(File.Exists(cache.GetStoragePath(storageIds[0])));
        Assert.False(File.Exists(cache.GetStoragePath(storageIds[1])));
        Assert.All(storageIds.Skip(2), storageId => Assert.True(File.Exists(cache.GetStoragePath(storageId))));
    }

    [Fact]
    public async Task EnforceSizeLimitAsync_UnderTrackedLimit_DoesNotRescanDirectory()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot { Settings = ImageCacheSettings.Default with { MaxSizeBytes = 100 } };
        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        await service.EnforceSizeLimitAsync(0); // first call scans the (empty) directory for a baseline

        // A file over the limit that was never reported: a rescan would evict it, so it surviving
        // the next conversions shows they didn't enumerate the directory.
        var unreportedPath = cache.GetStoragePath(Guid.NewGuid());
        await File.WriteAllTextAsync(unreportedPath, new string('x', 200));
        for (var i = 0; i < 9; i++) await service.EnforceSizeLimitAsync(10);
        Assert.True(File.Exists(unreportedPath));

        // Tracked total 90 + 20 passes the limit, so this one rescans and evicts.
        await service.EnforceSizeLimitAsync(20);
        Assert.False(File.Exists(unreportedPath));
    }

    [Fact]
    public async Task GetStatsAsync_RebasesTrackedSizeFromItsScan()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot { Settings = ImageCacheSettings.Default with { MaxSizeBytes = 100 } };
        var service = new ImageCacheMaintenanceService(cache, factory, new ImageCacheSizeTracker());
        await service.EnforceSizeLimitAsync(0);

        var unreportedPath = cache.GetStoragePath(Guid.NewGuid());
        await File.WriteAllTextAsync(unreportedPath, new string('x', 150));
        await service.GetStatsAsync();

        // Nothing reported, but the stats walk counted the unreported 150 bytes, so the tracked
        // total is over the limit and this rescans and evicts.
        await service.EnforceSizeLimitAsync(0);
        Assert.False(File.Exists(unreportedPath));
    }
}
