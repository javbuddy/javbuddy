using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Movies;

public class MovieCoverCropServiceTests
{
    private static byte[] CreateSampleImageBytes(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task GetCropSourcesAsync_WithLocalAndRemoteSources_ReturnsExpectedList()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Code = "IPX-535",
                Title = "Test Movie",
                MetaBackdropUrl = "https://example.com/backdrop.jpg",
                MetaCoverUrl = "https://example.com/cover.jpg"
            });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<ILocalLibraryClient>();
        client.TryGetMetadataAsync("IPX-535", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(false, null, null, null));
        client.ListExtraFanartFileNamesAsync("IPX-535", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "a.jpg", "b.jpg" });

        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();
        var httpFactory = Substitute.For<IHttpClientFactory>();

        var service = new MovieCoverCropService(factory, client, cache, maintenance, httpFactory);
        var result = await service.GetCropSourcesAsync("IPX-535");

        Assert.Equal("IPX-535", result.Code);
        Assert.Equal("backdrop", result.DefaultSourceKey);
        Assert.False(result.HasLocalFolder);
        Assert.Contains(result.Sources, s => s.Key == "backdrop");
        Assert.Contains(result.Sources, s => s.Key == "poster");

        // Every source gets a small variant for the source strip alongside its full image.
        Assert.Equal(4, result.Sources.Count);
        Assert.All(result.Sources, s => Assert.Equal(s.Url.Replace("/full", "/thumb"), s.ThumbnailUrl));
        Assert.Equal("/image-cache/IPX-535/extrafanart/1/thumb", result.Sources.Single(s => s.Key == "extrafanart-1").ThumbnailUrl);
    }

    [Fact]
    public async Task RotateImageAsync_ReturnsRotatedPng_AndNullForUnusableInput()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieCoverCropService(
            factory,
            Substitute.For<ILocalLibraryClient>(),
            Substitute.For<ILocalImageCache>(),
            Substitute.For<IImageCacheMaintenanceService>(),
            Substitute.For<IHttpClientFactory>());

        using var source = new SKBitmap(6, 3);
        source.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(source);
        var sourceBytes = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        var rotated = await service.RotateImageAsync(sourceBytes, clockwise: true);

        Assert.NotNull(rotated);
        using var bitmap = SKBitmap.Decode(rotated);
        Assert.Equal((3, 6), (bitmap.Width, bitmap.Height));
        Assert.Null(await service.RotateImageAsync([], clockwise: true));
        Assert.Null(await service.RotateImageAsync([1, 2, 3], clockwise: true));
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_LocalMovie_WritesPosterJpgAndUpdatesCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "javbuddy_test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var fanartFile = Path.Combine(tempDir, "fanart.jpg");
            var originalFanartBytes = CreateSampleImageBytes(1200, 800, SKColors.DarkBlue);
            await File.WriteAllBytesAsync(fanartFile, originalFanartBytes);

            using var factory = new TestDbContextFactory();
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Movies.Add(new Movie
                {
                    Code = "IPX-535",
                    Title = "Test Movie",
                    Status = MovieStatus.Got
                });
                await db.SaveChangesAsync();
            }

            var client = Substitute.For<ILocalLibraryClient>();
            client.TryGetMetadataAsync("IPX-535", Arg.Any<CancellationToken>(), false)
                .Returns(new LocalLookupResult(true, tempDir, null, null));

            var cacheStorageDir = Path.Combine(Path.GetTempPath(), "javbuddy_cache_" + Guid.NewGuid());
            Directory.CreateDirectory(cacheStorageDir);
            try
            {
                var cache = Substitute.For<ILocalImageCache>();
                cache.QualityFull.Returns(82);
                cache.QualityThumb.Returns(85);
                cache.GetStoragePath(Arg.Any<Guid>()).Returns(call => Path.Combine(cacheStorageDir, call.Arg<Guid>().ToString() + ".webp"));

                var maintenance = Substitute.For<IImageCacheMaintenanceService>();
                var httpFactory = Substitute.For<IHttpClientFactory>();

                var service = new MovieCoverCropService(factory, client, cache, maintenance, httpFactory);

                // Crop right half (e.g. 0.5 to 1.0)
                var sourceBytes = CreateSampleImageBytes(1000, 700, SKColors.Purple);
                var cropRect = new NormalizedCropRect(0.5, 0.0, 0.5, 1.0);

                var opResult = await service.SaveCroppedCoverAsync("IPX-535", sourceBytes, cropRect);

                Assert.True(opResult.Success);

                // Verify poster.jpg was written to tempDir
                var posterFile = Path.Combine(tempDir, "poster.jpg");
                Assert.True(File.Exists(posterFile));
                var posterBytes = await File.ReadAllBytesAsync(posterFile);
                Assert.NotEmpty(posterBytes);

                // Verify fanart.jpg was NOT modified or overwritten!
                Assert.True(File.Exists(fanartFile));
                var currentFanartBytes = await File.ReadAllBytesAsync(fanartFile);
                Assert.Equal(originalFanartBytes, currentFanartBytes);

                // Verify CachedImages entry exists
                await using (var db = await factory.CreateDbContextAsync())
                {
                    var cached = await db.CachedImages.Where(c => c.Code == "IPX-535" && c.Role == "poster").ToListAsync();
                    Assert.Equal(2, cached.Count); // thumb and full
                }

                await client.Received(1).SyncImagesSignatureAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
                await maintenance.Received(1).EnforceSizeLimitAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
            }
            finally
            {
                if (Directory.Exists(cacheStorageDir)) Directory.Delete(cacheStorageDir, true);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private sealed class LocalSaveFixture : IDisposable
    {
        public string MovieDir { get; } = Path.Combine(Path.GetTempPath(), "javbuddy_test_" + Guid.NewGuid());
        public string CacheDir { get; } = Path.Combine(Path.GetTempPath(), "javbuddy_cache_" + Guid.NewGuid());
        public TestDbContextFactory Factory { get; } = new();
        public ILocalLibraryClient Client { get; } = Substitute.For<ILocalLibraryClient>();
        public MovieCoverCropService Service { get; }

        public LocalSaveFixture()
        {
            Directory.CreateDirectory(MovieDir);
            Directory.CreateDirectory(CacheDir);
            using (var db = Factory.CreateDbContext())
            {
                db.Movies.Add(new Movie { Code = "IPX-535", Title = "Test Movie", Status = MovieStatus.Got });
                db.SaveChanges();
            }

            Client.TryGetMetadataAsync("IPX-535", Arg.Any<CancellationToken>(), false)
                .Returns(new LocalLookupResult(true, MovieDir, null, null));
            var cache = Substitute.For<ILocalImageCache>();
            cache.QualityFull.Returns(82);
            cache.QualityThumb.Returns(85);
            cache.GetStoragePath(Arg.Any<Guid>()).Returns(call => Path.Combine(CacheDir, call.Arg<Guid>() + ".webp"));
            Service = new MovieCoverCropService(Factory, Client, cache, Substitute.For<IImageCacheMaintenanceService>(), Substitute.For<IHttpClientFactory>());
        }

        public Task<MovieCoverCropSaveResult> SaveAsync() =>
            Service.SaveCroppedCoverAsync("IPX-535", CreateSampleImageBytes(1000, 700, SKColors.Purple), new NormalizedCropRect(0.5, 0.0, 0.5, 1.0));

        public void Dispose()
        {
            Factory.Dispose();
            if (Directory.Exists(MovieDir)) Directory.Delete(MovieDir, true);
            if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true);
        }
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_LocalMovieWithOtherPosterFiles_StandardizesOnPosterJpg()
    {
        using var fx = new LocalSaveFixture();
        var fanart = CreateSampleImageBytes(1200, 800, SKColors.DarkBlue);
        await File.WriteAllBytesAsync(Path.Combine(fx.MovieDir, "fanart.jpg"), fanart);
        await File.WriteAllBytesAsync(Path.Combine(fx.MovieDir, "folder.jpg"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(fx.MovieDir, "poster.png"), [4, 5, 6]);

        var result = await fx.SaveAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.IsWarning);
        Assert.True(File.Exists(Path.Combine(fx.MovieDir, "poster.jpg")));
        Assert.False(File.Exists(Path.Combine(fx.MovieDir, "folder.jpg")));
        Assert.False(File.Exists(Path.Combine(fx.MovieDir, "poster.png")));
        Assert.Equal(fanart, await File.ReadAllBytesAsync(Path.Combine(fx.MovieDir, "fanart.jpg")));
        Assert.Equal("Saved the cropped cover to poster.jpg in the movie folder and removed poster.png, folder.jpg.", result.Message);
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_WithStraightenDegrees_CropsTheRotatedFrame()
    {
        using var fx = new LocalSaveFixture();

        // A 90° rotation turns a 1000 x 700 source into a 700 x 1000 frame; the full-frame crop is that size.
        var result = await fx.Service.SaveCroppedCoverAsync(
            "IPX-535",
            CreateSampleImageBytes(1000, 700, SKColors.Purple),
            new NormalizedCropRect(0, 0, 1, 1),
            straightenDegrees: 90);

        Assert.True(result.Success, result.ErrorMessage);
        using var poster = SKBitmap.Decode(Path.Combine(fx.MovieDir, "poster.jpg"));
        Assert.Equal((700, 1000), (poster.Width, poster.Height));
    }

    [Theory]
    [InlineData(12.0)]
    [InlineData(-12.0)]
    public async Task SaveCroppedCoverAsync_WithAFineAngle_AndACropInsideTheRotatedImage_SavesNoEmptyCorner(double degrees)
    {
        using var fx = new LocalSaveFixture();

        // 400 x 300 at 12° has a ~454 x 377 frame; the central 40% box (≈181 x 151) fits well inside the image.
        var result = await fx.Service.SaveCroppedCoverAsync(
            "IPX-535",
            CreateSampleImageBytes(400, 300, SKColors.Yellow),
            new NormalizedCropRect(0.3, 0.3, 0.4, 0.4),
            degrees);

        Assert.True(result.Success, result.ErrorMessage);
        using var poster = SKBitmap.Decode(Path.Combine(fx.MovieDir, "poster.jpg"));
        Assert.InRange(poster.Width, 175, 190);
        for (var x = 0; x < poster.Width; x++)
        {
            for (var y = 0; y < poster.Height; y++)
            {
                var pixel = poster.GetPixel(x, y);
                Assert.True(pixel.Red > 200 && pixel.Green > 200, $"dark pixel {pixel} at ({x},{y})");
            }
        }
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_WithoutStraightenDegrees_LeavesTheSourceOrientation()
    {
        using var fx = new LocalSaveFixture();

        var result = await fx.Service.SaveCroppedCoverAsync(
            "IPX-535",
            CreateSampleImageBytes(1000, 700, SKColors.Purple),
            new NormalizedCropRect(0, 0, 1, 1));

        Assert.True(result.Success, result.ErrorMessage);
        using var poster = SKBitmap.Decode(Path.Combine(fx.MovieDir, "poster.jpg"));
        Assert.Equal((1000, 700), (poster.Width, poster.Height));
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_LocalMovieWithoutOtherPosterFiles_ReportsDiskSave()
    {
        using var fx = new LocalSaveFixture();

        var result = await fx.SaveAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Saved the cropped cover to poster.jpg in the movie folder.", result.Message);
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_DiskWriteFails_ReturnsErrorAndLeavesCacheAndOtherPostersAlone()
    {
        using var fx = new LocalSaveFixture();
        // A directory where poster.jpg should go makes the final move fail on every platform.
        Directory.CreateDirectory(Path.Combine(fx.MovieDir, "poster.jpg"));
        await File.WriteAllBytesAsync(Path.Combine(fx.MovieDir, "folder.jpg"), [1, 2, 3]);

        var result = await fx.SaveAsync();

        Assert.False(result.Success);
        Assert.StartsWith("Could not write poster.jpg to the movie folder:", result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(fx.MovieDir, "folder.jpg")));
        Assert.False(File.Exists(Path.Combine(fx.MovieDir, "poster.jpg.tmp")));
        Assert.Empty(Directory.GetFiles(fx.CacheDir));
        await using var db = await fx.Factory.CreateDbContextAsync();
        Assert.Empty(await db.CachedImages.ToListAsync());
        await fx.Client.DidNotReceive().SyncImagesSignatureAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCropSourcesAsync_ReportsWhetherMovieHasLocalFolder()
    {
        using var fx = new LocalSaveFixture();
        fx.Client.ListExtraFanartFileNamesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(new List<string>());

        var result = await fx.Service.GetCropSourcesAsync("IPX-535");

        Assert.True(result.HasLocalFolder);
    }

    [Fact]
    public async Task SaveCroppedCoverAsync_MissingMovie_SavesToCacheWithCustomSourceUrl()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Code = "IPX-999",
                Title = "Missing Movie",
                Status = MovieStatus.Missing
            });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<ILocalLibraryClient>();
        client.TryGetMetadataAsync("IPX-999", Arg.Any<CancellationToken>(), false)
            .Returns(new LocalLookupResult(false, null, null, null));

        var cacheStorageDir = Path.Combine(Path.GetTempPath(), "javbuddy_cache_" + Guid.NewGuid());
        Directory.CreateDirectory(cacheStorageDir);
        try
        {
            var cache = Substitute.For<ILocalImageCache>();
            cache.QualityFull.Returns(82);
            cache.QualityThumb.Returns(85);
            cache.GetStoragePath(Arg.Any<Guid>()).Returns(call => Path.Combine(cacheStorageDir, call.Arg<Guid>().ToString() + ".webp"));

            var maintenance = Substitute.For<IImageCacheMaintenanceService>();
            var httpFactory = Substitute.For<IHttpClientFactory>();

            var service = new MovieCoverCropService(factory, client, cache, maintenance, httpFactory);

            var sourceBytes = CreateSampleImageBytes(1200, 800, SKColors.Crimson);
            var cropRect = new NormalizedCropRect(0.52, 0.0, 0.48, 1.0);

            var opResult = await service.SaveCroppedCoverAsync("IPX-999", sourceBytes, cropRect);

            Assert.True(opResult.Success);
            Assert.Equal("Saved the cropped cover to Javbuddy's image cache only — this movie has no local folder, so nothing was written to disk.", opResult.Message);

            // Verify CachedImages entry has SourceUrl = "custom"
            await using (var db = await factory.CreateDbContextAsync())
            {
                var cached = await db.CachedImages.Where(c => c.Code == "IPX-999" && c.Role == "poster").ToListAsync();
                Assert.Equal(2, cached.Count);
                Assert.All(cached, c => Assert.Equal("custom", c.SourceUrl));
            }
        }
        finally
        {
            if (Directory.Exists(cacheStorageDir)) Directory.Delete(cacheStorageDir, true);
        }
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_DefunctUrl_ReturnsError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();
        var httpFactory = Substitute.For<IHttpClientFactory>();

        var service = new MovieCoverCropService(factory, client, cache, maintenance, httpFactory);

        var result = await service.DownloadImageFromUrlAsync("https://pics.r18.com/digital/amateur/test.jpg");

        Assert.False(result.Success);
        Assert.Contains("defunct", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static MovieCoverCropService DownloadService(HttpMessageHandler handler, ImageUploadSettings? uploadSettings = null) =>
        new(new TestDbContextFactory(), Substitute.For<ILocalLibraryClient>(), Substitute.For<ILocalImageCache>(),
            Substitute.For<IImageCacheMaintenanceService>(), new FakeHttpClientFactory(handler), uploadSettings);

    private static FakeHttpMessageHandler Responding(Func<HttpContent> content) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content() }));

    [Fact]
    public async Task DownloadImageFromUrlAsync_Success_SendsJavbuddyHeadersAndReturnsMediaType()
    {
        var handler = Responding(() =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new("image/webp");
            return content;
        });
        var service = DownloadService(handler);

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.webp");

        Assert.True(result.Success);
        Assert.Equal("image/webp", result.ContentType);
        Assert.Contains("Javbuddy/1.0", handler.LastRequest!.Headers.UserAgent.ToString());
        Assert.Contains(handler.LastRequest.Headers.Accept, a => a.MediaType == "image/webp");
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_NoContentTypeOrLength_DefaultsToJpeg()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream([1, 2, 3]))));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover");

        Assert.True(result.Success);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_EmptyBody_ReturnsEmptyError()
    {
        var service = DownloadService(Responding(() => new ByteArrayContent([])));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        Assert.Equal("Downloaded image file is empty.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_DeclaredOversize_ReportsDeclaredSize()
    {
        var service = DownloadService(Responding(() => new ByteArrayContent(new byte[3 * 1024 * 1024])), new ImageUploadSettings(MaxSizeMb: 1));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        Assert.Equal($"Image size ({3L:F1} MB) exceeds maximum allowed size (1 MB).", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ChunkedOversize_ReturnsSizeError()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(new byte[1024 * 1024 + 1]))), new ImageUploadSettings(MaxSizeMb: 1));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        Assert.Equal("Image size exceeds maximum allowed size (1 MB).", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_NetworkError_ReturnsNetworkMessage()
    {
        var service = DownloadService(FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused")));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        Assert.Equal("Network error downloading image: connection refused", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_Timeout_ReturnsTimeoutMessage()
    {
        var service = DownloadService(FakeHttpMessageHandler.Throwing(new TaskCanceledException("timed out")));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        Assert.Equal("Image download timed out after 30 seconds.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_CallerCancelledMidStream_Propagates()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(new byte[10], ControlledStream.AfterData.BlockUntilCancelled))));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadImageFromUrlAsync("https://example.com/cover.jpg", cts.Token));
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_UsesTheGuardedImportClient()
    {
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(Responding(() => new ByteArrayContent([1, 2, 3]))));
        var service = new MovieCoverCropService(new TestDbContextFactory(), Substitute.For<ILocalLibraryClient>(), Substitute.For<ILocalImageCache>(),
            Substitute.For<IImageCacheMaintenanceService>(), http);

        await service.DownloadImageFromUrlAsync("https://example.com/cover.jpg");

        http.Received(1).CreateClient(RemoteImageDownloader.ImportClientName);
    }
}
