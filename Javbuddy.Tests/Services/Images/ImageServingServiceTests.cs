using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Images;

public class ImageServingServiceTests
{
    private static ImageServingService CreateService(
        TestDbContextFactory factory,
        ILocalImageCacheService? imageCacheService = null,
        ILocalImageCache? imageCache = null,
        IRemotePosterCropService? remotePosterCropService = null,
        IR18DevDumpStore? r18DevDumpStore = null)
    {
        if (imageCacheService is null)
        {
            imageCacheService = Substitute.For<ILocalImageCacheService>();
            imageCacheService.GetOrCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((string?)null);
        }

        if (imageCache is null)
        {
            imageCache = Substitute.For<ILocalImageCache>();
            imageCache.Settings.Returns(ImageCacheSettings.Default with { Mode = ImageCacheMode.AllMovies });
        }

        if (remotePosterCropService is null)
        {
            remotePosterCropService = Substitute.For<IRemotePosterCropService>();
            remotePosterCropService.GetOrCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((string?)null);
        }

        r18DevDumpStore ??= Substitute.For<IR18DevDumpStore>();

        return new ImageServingService(imageCacheService, imageCache, remotePosterCropService, r18DevDumpStore, factory);
    }

    [Theory]
    [InlineData("backdrop")]
    [InlineData("")]
    public async Task GetPosterOrFanartAsync_UnknownRole_ReturnsNotFound(string role)
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.GetPosterOrFanartAsync("ABC-123", role, "thumb");

        Assert.Equal(ImageServingResultKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_LocalCacheHit_ReturnsLocalFile()
    {
        using var factory = new TestDbContextFactory();
        var imageCacheService = Substitute.For<ILocalImageCacheService>();
        imageCacheService.GetOrCreateAsync("ABC-123", LocalImageCacheService.RolePoster, 0, "thumb", Arg.Any<CancellationToken>())
            .Returns("/cache/ABC-123-poster-thumb.webp");
        var service = CreateService(factory, imageCacheService: imageCacheService);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.LocalFile, result.Kind);
        Assert.Equal("/cache/ABC-123-poster-thumb.webp", result.FilePath);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_MissingMovieNoLocalFile_CropsRemoteCover()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing, MetaCoverUrl = "https://example.com/cover.jpg" });
            await db.SaveChangesAsync();
        }

        var remotePosterCropService = Substitute.For<IRemotePosterCropService>();
        remotePosterCropService.GetOrCreateAsync("ABC-123", "https://example.com/cover.jpg", "thumb", Arg.Any<CancellationToken>())
            .Returns("/cache/ABC-123-cropped-thumb.webp");
        var service = CreateService(factory, remotePosterCropService: remotePosterCropService);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.LocalFile, result.Kind);
        Assert.Equal("/cache/ABC-123-cropped-thumb.webp", result.FilePath);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_LocalMoviesMode_DoesNotCacheRemoteCover()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing, MetaCoverUrl = "https://example.com/cover.jpg" });
            await db.SaveChangesAsync();
        }

        var imageCache = Substitute.For<ILocalImageCache>();
        imageCache.Settings.Returns(ImageCacheSettings.Default);
        var remotePosterCropService = Substitute.For<IRemotePosterCropService>();
        var service = CreateService(factory, imageCache: imageCache, remotePosterCropService: remotePosterCropService);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.Redirect, result.Kind);
        Assert.Equal("https://example.com/cover.jpg", result.RedirectUrl);
        await remotePosterCropService.DidNotReceive().GetOrCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_GotMovieNoLocalFile_RedirectsToRemoteCover()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaCoverUrl = "https://example.com/cover.jpg" });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.Redirect, result.Kind);
        Assert.Equal("https://example.com/cover.jpg", result.RedirectUrl);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_FanartRole_RedirectsToBackdropNotCover()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaCoverUrl = "https://example.com/cover.jpg",
                MetaBackdropUrl = "https://example.com/backdrop.jpg",
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RoleFanart, "full");

        Assert.Equal(ImageServingResultKind.Redirect, result.Kind);
        Assert.Equal("https://example.com/backdrop.jpg", result.RedirectUrl);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_UntrackedCodeWithR18DevPreview_CropsPreviewCover()
    {
        using var factory = new TestDbContextFactory();
        var r18DevDumpStore = Substitute.For<IR18DevDumpStore>();
        r18DevDumpStore.GetMovieByCodeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new R18DevMovieDetail("ABC-123", null, null, null, null, null, null,
                "https://r18.dev/preview.jpg", null, null, null, null, [], [], null, []));
        var remotePosterCropService = Substitute.For<IRemotePosterCropService>();
        remotePosterCropService.GetOrCreateAsync("ABC-123", "https://r18.dev/preview.jpg", "thumb", Arg.Any<CancellationToken>())
            .Returns("/cache/ABC-123-preview-thumb.webp");
        var service = CreateService(factory, remotePosterCropService: remotePosterCropService, r18DevDumpStore: r18DevDumpStore);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.LocalFile, result.Kind);
        Assert.Equal("/cache/ABC-123-preview-thumb.webp", result.FilePath);
    }

    [Fact]
    public async Task GetPosterOrFanartAsync_UntrackedCodeWithNoPreview_ReturnsNotFound()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory);

        var result = await service.GetPosterOrFanartAsync("ABC-123", LocalImageCacheService.RolePoster, "thumb");

        Assert.Equal(ImageServingResultKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task GetPosterVersionAsync_StaysTheSameUntilThePosterChanges()
    {
        using var factory = new TestDbContextFactory();
        var dir = Directory.CreateTempSubdirectory("javbuddy-poster-version-");
        try
        {
            var posterPath = Path.Combine(dir.FullName, "poster.jpg");
            await File.WriteAllBytesAsync(posterPath, [1, 2, 3]);
            File.SetLastWriteTimeUtc(posterPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaCoverUrl = "https://example.com/a.jpg" });
                db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = LocalImageCacheService.RolePoster, Variant = "full", UpdatedAt = new DateTime(2026, 1, 1) });
                await db.SaveChangesAsync();
            }

            var imageCacheService = Substitute.For<ILocalImageCacheService>();
            imageCacheService.ResolveSourcePathAsync("ABC-123", LocalImageCacheService.RolePoster, 0, Arg.Any<CancellationToken>())
                .Returns(posterPath);
            var service = CreateService(factory, imageCacheService: imageCacheService);

            var original = await service.GetPosterVersionAsync("ABC-123");
            Assert.Equal(original, await service.GetPosterVersionAsync("ABC-123"));

            // The poster file in the library folder is replaced (a crop, or an outside edit).
            File.SetLastWriteTimeUtc(posterPath, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            var afterFileChange = await service.GetPosterVersionAsync("ABC-123");
            Assert.NotEqual(original, afterFileChange);

            // The cached copy is rewritten (a crop of a movie with no local folder).
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, new DateTime(2026, 3, 1)));
            }
            var afterCacheChange = await service.GetPosterVersionAsync("ABC-123");
            Assert.NotEqual(afterFileChange, afterCacheChange);

            // A metadata refresh brings in a new remote cover.
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Movies.ExecuteUpdateAsync(s => s.SetProperty(m => m.MetaCoverUrl, "https://example.com/b.jpg"));
            }
            Assert.NotEqual(afterCacheChange, await service.GetPosterVersionAsync("ABC-123"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
