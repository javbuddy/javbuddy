using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public class LocalImageCacheServiceTests : IDisposable
{
    private sealed class TempCache : ILocalImageCache, IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "javbuddy-localcache-tests-" + Guid.NewGuid().ToString("N"));
        public int QualityFull => 90;
        public int QualityThumb => 75;
        public ImageCacheSettings Settings { get; set; } = ImageCacheSettings.Default;

        public TempCache() => Directory.CreateDirectory(RootPath);

        public string GetStoragePath(Guid storageId) => Path.Combine(RootPath, storageId.ToString("N") + ".webp");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private readonly TempCache cache = new();

    public void Dispose()
    {
        cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LocalImageCacheService CreateService(
        TestDbContextFactory factory,
        ILocalImageCache cache,
        ILocalLibraryClient? libraryClient = null,
        JavbuddyMetrics? metrics = null,
        InFlightImageConversions? inFlightConversions = null)
    {
        libraryClient ??= Substitute.For<ILocalLibraryClient>();
        metrics ??= new JavbuddyMetrics();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();
        return new LocalImageCacheService(libraryClient, cache, factory, metrics, maintenance, inFlightConversions ?? new InFlightImageConversions());
    }

    [Fact]
    public async Task GetOrCreateAsync_SourceOffline_ReturnsCachedFile_WhenUnexpiredCacheExists()
    {
        using var factory = new TestDbContextFactory();
        var storageId = Guid.NewGuid();
        var cachedFilePath = cache.GetStoragePath(storageId);
        await File.WriteAllTextAsync(cachedFilePath, "fake webp bytes");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Add(new CachedImage
            {
                Code = "ABC-123",
                Role = LocalImageCacheService.RolePoster,
                Index = 0,
                Variant = LocalImageCacheService.VariantThumb,
                StorageId = storageId,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var libraryClient = Substitute.For<ILocalLibraryClient>();
        // Source file is offline/unreachable on disk
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateService(factory, cache, libraryClient);

        var result = await service.GetOrCreateAsync("ABC-123", LocalImageCacheService.RolePoster, 0, LocalImageCacheService.VariantThumb);

        Assert.NotNull(result);
        Assert.Equal(cachedFilePath, result);
    }

    [Fact]
    public async Task GetOrCreateAsync_SourceOffline_ReturnsNull_WhenNoCacheExists()
    {
        using var factory = new TestDbContextFactory();
        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateService(factory, cache, libraryClient);

        var result = await service.GetOrCreateAsync("ABC-123", LocalImageCacheService.RolePoster, 0, LocalImageCacheService.VariantThumb);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_SourceOffline_ReturnsCachedPaths_WhenUnexpiredCacheExists()
    {
        using var factory = new TestDbContextFactory();
        var thumbId = Guid.NewGuid();
        var fullId = Guid.NewGuid();
        var thumbPath = cache.GetStoragePath(thumbId);
        var fullPath = cache.GetStoragePath(fullId);
        await File.WriteAllTextAsync(thumbPath, "thumb bytes");
        await File.WriteAllTextAsync(fullPath, "full bytes");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.AddRange(
                new CachedImage
                {
                    Code = "ABC-123",
                    Role = LocalImageCacheService.RolePoster,
                    Index = 0,
                    Variant = LocalImageCacheService.VariantThumb,
                    StorageId = thumbId,
                    UpdatedAt = DateTime.UtcNow,
                },
                new CachedImage
                {
                    Code = "ABC-123",
                    Role = LocalImageCacheService.RolePoster,
                    Index = 0,
                    Variant = LocalImageCacheService.VariantFull,
                    StorageId = fullId,
                    UpdatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateService(factory, cache, libraryClient);

        var (resolvedThumb, resolvedFull) = await service.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0);

        Assert.Equal(thumbPath, resolvedThumb);
        Assert.Equal(fullPath, resolvedFull);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_SourceOffline_ReturnsNulls_WhenNoCacheExists()
    {
        using var factory = new TestDbContextFactory();
        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateService(factory, cache, libraryClient);

        var (resolvedThumb, resolvedFull) = await service.GetOrCreateBothAsync("ABC-123", LocalImageCacheService.RolePoster, 0);

        Assert.Null(resolvedThumb);
        Assert.Null(resolvedFull);
    }

    [Fact]
    public async Task GetOrCreateAsync_OriginalVariant_ReturnsExtraFanartSourceFile_WithoutConverting()
    {
        using var factory = new TestDbContextFactory();
        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ListExtraFanartFileNamesAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "01.jpg" });
        libraryClient.ResolveLiveExtraFanartFilePathAsync("ABC-123", "01.jpg", Arg.Any<CancellationToken>())
            .Returns("/library/ABC-123/extrafanart/01.jpg");

        var service = CreateService(factory, cache, libraryClient);

        var result = await service.GetOrCreateAsync("ABC-123", LocalImageCacheService.RoleExtraFanart, 0, LocalImageCacheService.VariantOriginal);

        Assert.Equal("/library/ABC-123/extrafanart/01.jpg", result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CachedImages.AnyAsync(c => c.Code == "ABC-123"));
    }

    [Fact]
    public async Task GetOrCreateAsync_OriginalVariant_ReturnsNull_ForNonExtraFanartRoles()
    {
        using var factory = new TestDbContextFactory();
        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns("/library/ABC-123/poster.jpg");

        var service = CreateService(factory, cache, libraryClient);

        var result = await service.GetOrCreateAsync("ABC-123", LocalImageCacheService.RolePoster, 0, LocalImageCacheService.VariantOriginal);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentMissesForOneImage_ConvertOnce()
    {
        using var factory = TestDbContextFactory.WithConnectionPerContext();
        var sourceDir = Path.Combine(cache.RootPath, "library");
        Directory.CreateDirectory(sourceDir);
        var posterPath = Path.Combine(sourceDir, "poster.jpg");
        using (var bitmap = new SKBitmap(1600, 1076))
        {
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SteelBlue);
            using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
            await File.WriteAllBytesAsync(posterPath, data.ToArray());
        }

        // Hold every request at source resolution, then release them all at once, so they all
        // reach the cache-miss path together — without de-duplication each would convert.
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var libraryClient = Substitute.For<ILocalLibraryClient>();
        libraryClient.ResolveFirstExistingLocalFilePathAsync("ABC-123", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => release.Task);

        var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        var inFlight = new InFlightImageConversions();

        var requests = Enumerable.Range(0, 8).Select(i =>
        {
            var service = CreateService(factory, cache, libraryClient, metrics, inFlight);
            var variant = i % 2 == 0 ? LocalImageCacheService.VariantThumb : LocalImageCacheService.VariantFull;
            return service.GetOrCreateAsync("ABC-123", LocalImageCacheService.RolePoster, 0, variant);
        }).ToList();
        release.SetResult(posterPath);
        var results = await Task.WhenAll(requests);

        Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_image_cache_conversions_total");
        Assert.All(results, path => Assert.True(File.Exists(path)));
        Assert.Equal(2, results.Distinct().Count());
    }
}
