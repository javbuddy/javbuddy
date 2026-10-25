using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Images;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.DeoVr;

public sealed class DeoVrThumbnailServiceTests : IDisposable
{
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
    private readonly string libraryDir = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeImageCache imageCache = new(Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N")));
    private readonly ILocalImageCacheService imageCacheService = Substitute.For<ILocalImageCacheService>();
    private readonly IImageCacheMaintenanceService maintenance = Substitute.For<IImageCacheMaintenanceService>();

    public void Dispose()
    {
        dbFactory.Dispose();
        if (Directory.Exists(libraryDir)) Directory.Delete(libraryDir, recursive: true);
        if (Directory.Exists(imageCache.RootPath)) Directory.Delete(imageCache.RootPath, recursive: true);
    }

    private DeoVrThumbnailService Service() =>
        new(dbFactory, imageCacheService, imageCache, maintenance, new InFlightImageConversions(), NullLogger<DeoVrThumbnailService>.Instance);

    private async Task<int> SeedAsync(string? coverUrl = "https://img.example.com/abc.jpg")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaCoverUrl = coverUrl };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    /// <summary>Writes a WebP fanart, which DeoVR couldn't show as-is, and makes it the movie's source.</summary>
    private string WriteFanart(int width = 1600, int height = 1076, SKColor? color = null)
    {
        Directory.CreateDirectory(libraryDir);
        var path = Path.Combine(libraryDir, "fanart.webp");
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        File.WriteAllBytes(path, data.ToArray());
        imageCacheService.ResolveSourcePathAsync("ABC-123", LocalImageCacheService.RoleFanart, 0, Arg.Any<CancellationToken>()).Returns(path);
        return path;
    }

    private static async Task<SKBitmap> AssertIsJpegAsync(DeoVrThumbnail? thumbnail)
    {
        await using var image = thumbnail?.Image;
        Assert.NotNull(image);
        using var buffer = new MemoryStream();
        await image.Content.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        Assert.Equal([0xFF, 0xD8], bytes[..2]);
        return SKBitmap.Decode(bytes);
    }

    // The default interface method, which the fake's own type doesn't expose.
    private string StoragePath(CachedImage row) => ((ILocalImageCache)imageCache).GetStoragePath(row);

    private async Task<CachedImage> SingleRowAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return Assert.Single(db.CachedImages);
    }

    [Fact]
    public async Task GetAsync_ServesLocalFanartAsADownscaledJpeg_AndCachesIt()
    {
        var movieId = await SeedAsync();
        var source = new FileInfo(WriteFanart());

        using var bitmap = await AssertIsJpegAsync(await Service().GetAsync(movieId));

        Assert.Equal(DeoVrThumbnailService.MaxEdge, bitmap.Width);
        var row = await SingleRowAsync();
        Assert.Equal(DeoVrThumbnailService.CacheRole, row.Role);
        Assert.Equal(source.Length, row.SourceLength);
        Assert.True(File.Exists(StoragePath(row)));
        Assert.EndsWith(".jpg", StoragePath(row), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_ServesTheCachedCopy_UntilTheFanartChanges()
    {
        var movieId = await SeedAsync();
        WriteFanart();
        await (await Service().GetAsync(movieId))!.Image!.DisposeAsync();
        var first = await SingleRowAsync();

        await using (var cached = (await Service().GetAsync(movieId))!.Image)
        {
            Assert.Equal(StoragePath(first), cached!.Key);
        }

        var changed = WriteFanart(color: SKColors.Blue);
        File.SetLastWriteTimeUtc(changed, File.GetLastWriteTimeUtc(changed).AddMinutes(1));
        using var bitmap = await AssertIsJpegAsync(await Service().GetAsync(movieId));

        // JPEG is lossy, so the rebuilt (blue) image is only nearly pure blue.
        var pixel = bitmap.GetPixel(10, 10);
        Assert.True(pixel.Blue > 200 && pixel.Red < 50, $"Expected blue, got {pixel}");
        var rebuilt = await SingleRowAsync();
        Assert.Equal(first.StorageId, rebuilt.StorageId);
        Assert.Equal(new FileInfo(changed).LastWriteTimeUtc, rebuilt.SourceLastWriteUtc);
    }

    [Fact]
    public async Task GetAsync_WithTheCacheDisabled_BuildsTheJpegWithoutKeepingIt()
    {
        var movieId = await SeedAsync();
        WriteFanart();
        imageCache.Settings = ImageCacheSettings.Default with { Mode = ImageCacheMode.Disabled };

        (await AssertIsJpegAsync(await Service().GetAsync(movieId))).Dispose();

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.CachedImages);
        Assert.False(Directory.Exists(imageCache.RootPath));
    }

    [Fact]
    public async Task GetAsync_WithoutLocalFanart_RedirectsToTheRemoteCover()
    {
        var movieId = await SeedAsync();

        Assert.Equal(new DeoVrThumbnail(null, "https://img.example.com/abc.jpg"), await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task GetAsync_IsNullWithoutTheMovieOrAnyImage()
    {
        var movieId = await SeedAsync(coverUrl: null);

        Assert.Null(await Service().GetAsync(movieId));
        Assert.Null(await Service().GetAsync(9999));
    }

    [Fact]
    public async Task PregenerateAsync_CachesTheJpeg_ThenLeavesAnUpToDateOneAlone()
    {
        var movieId = await SeedAsync();
        WriteFanart();

        Assert.True(await Service().PregenerateAsync("ABC-123"));
        var row = await SingleRowAsync();
        var written = File.GetLastWriteTimeUtc(StoragePath(row));

        Assert.True(await Service().PregenerateAsync("ABC-123"));
        Assert.Equal(written, File.GetLastWriteTimeUtc(StoragePath(row)));

        // A request afterwards is served from what was pregenerated.
        await using var served = (await Service().GetAsync(movieId))!.Image;
        Assert.Equal(StoragePath(row), served!.Key);
    }

    [Fact]
    public async Task PregenerateAsync_IsNullWithoutLocalFanart_OrWithTheCacheDisabled()
    {
        await SeedAsync();
        Assert.Null(await Service().PregenerateAsync("ABC-123"));

        WriteFanart();
        imageCache.Settings = ImageCacheSettings.Default with { Mode = ImageCacheMode.Disabled };
        Assert.Null(await Service().PregenerateAsync("ABC-123"));

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.CachedImages);
    }

    [Fact]
    public async Task PregenerateAsync_IsFalseWhenTheFanartDoesNotDecode()
    {
        await SeedAsync();
        var path = WriteFanart();
        await File.WriteAllTextAsync(path, "not an image");

        Assert.False(await Service().PregenerateAsync("ABC-123"));
    }
}
