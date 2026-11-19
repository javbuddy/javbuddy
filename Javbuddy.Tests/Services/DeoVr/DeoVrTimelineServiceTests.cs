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

    /// <summary>A movie with a main file and an alternate version, and (optionally) a trickplay set for
    /// the main file's identity, which is returned.</summary>
    private async Task<string> SeedWithFilesAsync(bool withSet = true)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123-alt.mp4", DurationSeconds = 40, Width = 1280, Height = 720 });
            movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123.mp4", IsPrimary = true, DurationSeconds = 40, Width = 1920, Height = 1080 });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var identity = TrickplayIdentity.For("ABC-123.mp4", 40, 1920, 1080)!;
        if (withSet)
        {
            await Store().SaveAsync("ABC-123", NewSet(identity), [() => new MemoryStream(Sheet())]);
        }
        return identity;
    }

    [Fact]
    public async Task PregenerateAsync_CachesTheMainFilesMosaic_ThenLeavesAnUpToDateOneAlone()
    {
        var identity = await SeedWithFilesAsync();

        Assert.True(await Service().PregenerateAsync("ABC-123"));
        var row = await SingleRowAsync();
        Assert.Equal(identity, row.SourceIdentity);
        var path = ((ILocalImageCache)imageCache).GetStoragePath(row);
        var written = File.GetLastWriteTimeUtc(path);

        Assert.True(await Service().PregenerateAsync("ABC-123"));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        await maintenance.Received(1).EnforceSizeLimitAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());

        // A request afterwards is served from what was pregenerated.
        int movieId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            movieId = await db.Movies.Select(m => m.Id).SingleAsync();
        }
        await using var served = await Service().OpenAsync(movieId, identity);
        Assert.Equal(path, served!.Key);
    }

    [Fact]
    public async Task PregenerateAsync_RebuildsInPlace_AMosaicOfAnotherSetOrAnExpiredOne()
    {
        var identity = await SeedWithFilesAsync();
        Assert.True(await Service().PregenerateAsync("ABC-123"));
        var first = await SingleRowAsync();

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.SourceIdentity, OtherIdentity));
        }
        Assert.True(await Service().PregenerateAsync("ABC-123"));
        var second = await SingleRowAsync();
        Assert.Equal((first.StorageId, identity), (second.StorageId, second.SourceIdentity));

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow.AddHours(-2)));
        }
        imageCache.Settings = ImageCacheSettings.Default with { Ttl = TimeSpan.FromHours(1) };
        Assert.True(await Service().PregenerateAsync("ABC-123"));
        Assert.True((await SingleRowAsync()).UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Single(Directory.EnumerateFiles(imageCache.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PregenerateAsync_RebuildsAMosaicWhoseFileIsGone()
    {
        await SeedWithFilesAsync();
        Assert.True(await Service().PregenerateAsync("ABC-123"));
        var path = ((ILocalImageCache)imageCache).GetStoragePath(await SingleRowAsync());
        File.Delete(path);

        Assert.True(await Service().PregenerateAsync("ABC-123"));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task PregenerateAsync_IsNullWithoutAFile()
    {
        await SeedAsync(withSet: true);
        Assert.Null(await Service().PregenerateAsync("ABC-123")); // a set, but no file to point DeoVR at
        Assert.Null(await Service().PregenerateAsync("NOPE-001"));
    }

    [Fact]
    public async Task PregenerateAsync_IsNullWithoutASetForTheMainFile()
    {
        await SeedWithFilesAsync(withSet: false);
        // Only the alternate version has a set; DeoVR shows the main file's.
        await Store().SaveAsync("ABC-123", NewSet(TrickplayIdentity.For("ABC-123-alt.mp4", 40, 1280, 720)!), [() => new MemoryStream(Sheet())]);

        Assert.Null(await Service().PregenerateAsync("ABC-123"));

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.CachedImages);
    }

    [Fact]
    public async Task PregenerateAsync_IsNullWithTheCacheDisabled()
    {
        await SeedWithFilesAsync();
        imageCache.Settings = ImageCacheSettings.Default with { Mode = ImageCacheMode.Disabled };

        Assert.Null(await Service().PregenerateAsync("ABC-123"));

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.CachedImages);
    }

    [Fact]
    public async Task PregenerateAsync_IsNullWhenTheMainFileHasNoDurationOrResolution()
    {
        await SeedWithFilesAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.MovieFiles.Where(f => f.IsPrimary).ExecuteUpdateAsync(s => s.SetProperty(f => f.DurationSeconds, (double?)null));
        }

        Assert.Null(await Service().PregenerateAsync("ABC-123"));
    }

    [Fact]
    public async Task PregenerateAsync_IsFalseWhenTheMosaicCannotBeWrittenToTheCache()
    {
        await SeedWithFilesAsync();
        // A file where the cache directory should be, so creating its shard directory fails.
        await File.WriteAllTextAsync(imageCache.RootPath, "not a directory");
        try
        {
            Assert.False(await Service().PregenerateAsync("ABC-123"));

            await using var db = await dbFactory.CreateDbContextAsync();
            Assert.Empty(db.CachedImages);
        }
        finally
        {
            File.Delete(imageCache.RootPath);
        }
    }

    [Fact]
    public async Task PregenerateAsync_IsFalseWhenTheSetHasNoSheets()
    {
        var identity = await SeedWithFilesAsync();
        await stores.Get(ObjectStoreArea.Trickplay).DeleteAsync($"ABC-123/{identity}/0.webp");

        Assert.False(await Service().PregenerateAsync("ABC-123"));
    }

    private static byte[] Sheet()
    {
        using var bitmap = new SKBitmap(64, 36);
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        return data.ToArray();
    }
}
