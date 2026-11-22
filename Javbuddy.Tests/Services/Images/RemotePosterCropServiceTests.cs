using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public class RemotePosterCropServiceTests : IDisposable
{
    private const string CoverUrl = "https://example.test/cover.jpg";

    private sealed class TempCache : ILocalImageCache
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "javbuddy-posterCrop-tests-" + Guid.NewGuid().ToString("N"));
        public int QualityFull => 90;
        public int QualityThumb => 75;
        public ImageCacheSettings Settings { get; set; } = ImageCacheSettings.Default;

        public string GetStoragePath(Guid storageId) => Path.Combine(RootPath, storageId.ToString("N") + ".webp");
    }

    private readonly TempCache cache = new();
    private readonly TestDbContextFactory factory = new();
    private readonly IImageCacheMaintenanceService maintenance = Substitute.For<IImageCacheMaintenanceService>();

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(cache.RootPath)) Directory.Delete(cache.RootPath, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static byte[] LandscapeCover()
    {
        using var bitmap = new SKBitmap(800, 538);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private RemotePosterCropService Create(FakeHttpMessageHandler handler) =>
        new(new FakeHttpClientFactory(handler), cache, factory, maintenance, new InFlightImageConversions());

    private static FakeHttpMessageHandler Serving(byte[] bytes, int[]? hits = null) => new((_, _) =>
    {
        if (hits is not null) Interlocked.Increment(ref hits[0]);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/png");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    });

    [Fact]
    public async Task GetOrCreateAsync_DownloadsCropsAndCachesBothVariants()
    {
        var service = Create(Serving(LandscapeCover()));

        var thumb = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);
        var full = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantFull);

        Assert.NotNull(thumb);
        Assert.NotNull(full);
        Assert.NotEqual(thumb, full);
        Assert.True(File.Exists(thumb));
        Assert.True(File.Exists(full));
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.CachedImages.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("ABC-123", r.Code);
            Assert.Equal(LocalImageCacheService.RolePoster, r.Role);
            Assert.Equal(CoverUrl, r.SourceUrl);
        });
        await maintenance.Received().EnforceSizeLimitAsync(Arg.Is<long>(n => n > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_FreshCacheForTheSameUrl_ServesItWithoutDownloading()
    {
        var hits = new int[1];
        var service = Create(Serving(LandscapeCover(), hits));
        var first = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);

        var second = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);

        Assert.Equal(first, second);
        Assert.Equal(1, hits[0]);
    }

    [Fact]
    public async Task GetOrCreateAsync_CoverUrlChanged_DownloadsAgainAndReusesTheStorageIds()
    {
        var hits = new int[1];
        var service = Create(Serving(LandscapeCover(), hits));
        var first = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);

        var second = await service.GetOrCreateAsync("ABC-123", "https://example.test/other.jpg", LocalImageCacheService.VariantThumb);

        Assert.Equal(2, hits[0]);
        Assert.Equal(first, second);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.CachedImages.CountAsync());
        Assert.All(await db.CachedImages.ToListAsync(), r => Assert.Equal("https://example.test/other.jpg", r.SourceUrl));
    }

    [Fact]
    public async Task GetOrCreateAsync_CustomPosterRow_IsServedAsIs()
    {
        var storageId = Guid.NewGuid();
        Directory.CreateDirectory(cache.RootPath);
        await File.WriteAllTextAsync(cache.GetStoragePath(storageId), "custom");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Add(new CachedImage
            {
                Code = "ABC-123",
                Role = LocalImageCacheService.RolePoster,
                Index = 0,
                Variant = LocalImageCacheService.VariantThumb,
                StorageId = storageId,
                SourceUrl = "custom",
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var hits = new int[1];
        var service = Create(Serving(LandscapeCover(), hits));

        var path = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);

        Assert.Equal(cache.GetStoragePath(storageId), path);
        Assert.Equal(0, hits[0]);
    }

    [Fact]
    public async Task GetOrCreateAsync_DownloadFails_ReturnsNullAndCachesNothing()
    {
        var service = Create(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.NotFound));

        var path = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantThumb);

        Assert.Null(path);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.CachedImages.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateAsync_UndecodableImage_ReturnsNull()
    {
        var service = Create(Serving([1, 2, 3, 4]));

        var path = await service.GetOrCreateAsync("ABC-123", CoverUrl, LocalImageCacheService.VariantFull);

        Assert.Null(path);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.CachedImages.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateAsync_InvalidUrl_ReturnsNullWithoutARequest()
    {
        var hits = new int[1];
        var service = Create(Serving(LandscapeCover(), hits));

        var path = await service.GetOrCreateAsync("ABC-123", "not a url", LocalImageCacheService.VariantThumb);

        Assert.Null(path);
        Assert.Equal(0, hits[0]);
    }
}
