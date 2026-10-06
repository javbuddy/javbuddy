using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.DeoVr;

public sealed class DeoVrTimelineServiceTests : IDisposable
{
    private const string Identity = "0123456789abcdef";
    private const string OtherIdentity = "fedcba9876543210";

    private sealed class FakeImageCache(string root) : ILocalImageCache
    {
        public string RootPath { get; } = root;
        public int QualityFull => 90;
        public int QualityThumb => 80;
        public ImageCacheSettings Settings { get; set; } = ImageCacheSettings.Default;

        public string GetStoragePath(Guid storageId)
        {
            var name = storageId.ToString("N");
            return Path.Combine(RootPath, name[..2], name + ".webp");
        }
    }

    private readonly TestDbContextFactory dbFactory = new();
    private readonly InMemoryObjectStoreProvider stores = new();
    private readonly FakeImageCache imageCache = new(Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N")));
    private readonly IImageCacheMaintenanceService maintenance = Substitute.For<IImageCacheMaintenanceService>();

    public void Dispose()
    {
        dbFactory.Dispose();
        if (Directory.Exists(imageCache.RootPath)) Directory.Delete(imageCache.RootPath, recursive: true);
    }

    private TrickplayStore Store() => new(dbFactory, stores);

    private DeoVrTimelineService Service() =>
        new(dbFactory, Store(), imageCache, maintenance, new InFlightImageConversions(), NullLogger<DeoVrTimelineService>.Instance);

    private static TrickplaySet NewSet(string identity = Identity) => new()
    {
        Identity = identity,
        Width = 32,
        Height = 18,
        TileWidth = 2,
        TileHeight = 2,
        ThumbnailCount = 4,
        IntervalMs = 10000,
        DurationSeconds = 40,
        FileName = "ABC-123.mp4",
    };

    private async Task<CachedImage> SingleRowAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CachedImages.AsNoTracking().SingleAsync();
    }

    private static async Task AssertIsMosaicAsync(StoredObject? mosaic)
    {
        Assert.NotNull(mosaic);
        await using (mosaic)
        {
            using var bitmap = SKBitmap.Decode(mosaic.Content);
            Assert.Equal((4096, 4096), (bitmap.Width, bitmap.Height));
        }
    }

    private async Task<int> SeedAsync(bool withSet = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123" };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        if (withSet)
        {
            await Store().SaveAsync("ABC-123", NewSet(), [() => new MemoryStream(Sheet())]);
        }
        return movie.Id;
    }

    [Fact]
    public async Task OpenAsync_BuildsTheMosaicOnce_AndKeepsItInTheImageCache()
    {
        var movieId = await SeedAsync();

        await AssertIsMosaicAsync(await Service().OpenAsync(movieId, Identity));

        var row = await SingleRowAsync();
        Assert.Equal(("ABC-123", DeoVrTimeline.CacheRole, DeoVrTimeline.CacheVariant, Identity), (row.Code, row.Role, row.Variant, row.SourceIdentity));
        var path = ((ILocalImageCache)imageCache).GetStoragePath(row);
        Assert.Equal(".jpg", Path.GetExtension(path));
        Assert.True(File.Exists(path));
        Assert.Equal([path], Directory.EnumerateFiles(imageCache.RootPath, "*", SearchOption.AllDirectories));
        Assert.DoesNotContain(stores.Get(ObjectStoreArea.Trickplay).Keys, key => !key.EndsWith(".webp", StringComparison.Ordinal));
        await maintenance.Received(1).EnforceSizeLimitAsync(new FileInfo(path).Length, Arg.Any<CancellationToken>());

        // Served from the cache from then on, even if the sheets were gone.
        await stores.Get(ObjectStoreArea.Trickplay).DeleteAsync($"ABC-123/{Identity}/0.webp");
        await using var again = await Service().OpenAsync(movieId, Identity);
        Assert.Equal(path, again!.Key);
    }

    [Fact]
    public async Task OpenAsync_RebuildsInPlace_ForAnotherSet()
    {
        var movieId = await SeedAsync();
        await Store().SaveAsync("ABC-123", NewSet(OtherIdentity), [() => new MemoryStream(Sheet())]);
        await (await Service().OpenAsync(movieId, Identity))!.DisposeAsync();
        var first = await SingleRowAsync();

        await AssertIsMosaicAsync(await Service().OpenAsync(movieId, OtherIdentity));

        var second = await SingleRowAsync();
        Assert.Equal((first.StorageId, OtherIdentity), (second.StorageId, second.SourceIdentity));
        Assert.Single(Directory.EnumerateFiles(imageCache.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenAsync_RebuildsAnExpiredMosaic()
    {
        var movieId = await SeedAsync();
        await (await Service().OpenAsync(movieId, Identity))!.DisposeAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow.AddHours(-2)));
        }
        imageCache.Settings = ImageCacheSettings.Default with { Ttl = TimeSpan.FromHours(1) };

        await AssertIsMosaicAsync(await Service().OpenAsync(movieId, Identity));

        Assert.True((await SingleRowAsync()).UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task OpenAsync_WithTheCacheDisabled_BuildsTheMosaicWithoutKeepingIt()
    {
        var movieId = await SeedAsync();
        imageCache.Settings = ImageCacheSettings.Default with { Mode = ImageCacheMode.Disabled };

        await AssertIsMosaicAsync(await Service().OpenAsync(movieId, Identity));

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.CachedImages);
        Assert.False(Directory.Exists(imageCache.RootPath));
    }

    [Fact]
    public async Task OpenAsync_InThumbnailsMode_StillCachesTheMosaic()
    {
        var movieId = await SeedAsync();
        imageCache.Settings = ImageCacheSettings.Default with { Mode = ImageCacheMode.Thumbnails };

        await AssertIsMosaicAsync(await Service().OpenAsync(movieId, Identity));

        Assert.Equal(Identity, (await SingleRowAsync()).SourceIdentity);
    }

    [Fact]
    public async Task OpenAsync_IsNullWithoutTheMovieOrSet()
    {
        var movieId = await SeedAsync(withSet: false);

        Assert.Null(await Service().OpenAsync(movieId, Identity));
        Assert.Null(await Service().OpenAsync(9999, Identity));
        Assert.Null(await Service().OpenAsync(movieId, "../etc"));
    }

    [Fact]
    public async Task OpenAsync_IsNullWhenTheSetHasNoSheets()
    {
        var movieId = await SeedAsync();
        await stores.Get(ObjectStoreArea.Trickplay).DeleteAsync($"ABC-123/{Identity}/0.webp");

        Assert.Null(await Service().OpenAsync(movieId, Identity));
    }

    private static byte[] Sheet()
    {
        using var bitmap = new SKBitmap(64, 36);
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        return data.ToArray();
    }
}
