using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public sealed class HighlightMediaServiceTests : IDisposable
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

    private readonly TestDbContextFactory factory = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeImageCache imageCache;
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly IImageCacheMaintenanceService maintenance = Substitute.For<IImageCacheMaintenanceService>();
    private readonly SceneMediaQueue queue = new(Substitute.For<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
    private readonly string videoPath;
    private readonly List<(string Kind, double At, double Duration, bool LeftEye)> calls = [];

    public HighlightMediaServiceTests()
    {
        var folder = Path.Combine(root, "library", "ABC-123");
        Directory.CreateDirectory(folder);
        videoPath = Path.Combine(folder, "ABC-123.mp4");
        File.WriteAllText(videoPath, "");
        imageCache = new FakeImageCache(Path.Combine(root, "cache"));
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        ProbeReturns(1920, 1080);

        // A fake ffmpeg that "encodes" by writing a few bytes to the requested output.
        ffmpeg.ExtractStillWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("still", call.ArgAt<double>(1), 0, call.ArgAt<bool>(2)));
                File.WriteAllText(call.ArgAt<string>(3), "still");
                return FfmpegRunResult.Ok();
            });
        ffmpeg.ExtractPreviewVideoAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("preview", call.ArgAt<double>(1), call.ArgAt<double>(2), call.ArgAt<bool>(3)));
                File.WriteAllText(call.ArgAt<string>(4), "preview!");
                return FfmpegRunResult.Ok();
            });
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void ProbeReturns(int width, int height) =>
        ffmpeg.ProbeAsync(videoPath, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 3600, Width = width, Height = height });

    private HighlightMediaService CreateService() => new(factory, localLibrary, ffmpeg, imageCache, maintenance, queue);

    private async Task<(int MovieId, int HighlightId)> SeedAsync(double start = 600, double end = 640)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 3600 };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = start, EndSeconds = end };
        db.MovieHighlights.Add(highlight);
        await db.SaveChangesAsync();
        return (movie.Id, highlight.Id);
    }

    private async Task<List<CachedImage>> RowsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CachedImages.OrderBy(c => c.Variant).ToListAsync();
    }

    [Fact]
    public async Task Generate_SamplesTheHighlightsOwnRange_AsHighlightRows()
    {
        var (movieId, highlightId) = await SeedAsync(600, 640);

        Assert.Equal(movieId, await CreateService().GenerateAsync(highlightId));

        var rows = await RowsAsync();
        Assert.Equal(["preview", "thumb"], rows.Select(r => r.Variant));
        Assert.All(rows, r => Assert.Equal(("highlight", highlightId, 600_000L, (long?)640_000L), (r.Role, r.Index, r.SceneStartMs, r.SceneEndMs)));
        Assert.Contains(("still", 618d, 0d, false), calls);
        Assert.Contains(("preview", 618d, 4d, false), calls);
        Assert.NotNull(await CreateService().GetFilePathAsync(highlightId, "thumb"));
    }

    [Fact]
    public async Task Media_IsStaleOnlyWhenTheHighlightRangeChanges()
    {
        var (movieId, highlightId) = await SeedAsync(600, 640);
        await CreateService().GenerateAsync(highlightId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            // A scene around it doesn't matter.
            db.Scenes.Add(new Scene { MovieId = movieId, StartSeconds = 500, EndSeconds = 700 });
            await db.SaveChangesAsync();
        }
        Assert.NotNull(await CreateService().GetFilePathAsync(highlightId, "thumb"));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var highlight = await db.MovieHighlights.SingleAsync();
            highlight.EndSeconds = 650;
            await db.SaveChangesAsync();
        }
        Assert.Null(await CreateService().GetFilePathAsync(highlightId, "thumb"));
    }

    [Fact]
    public async Task HighlightList_ReportsMediaVersions_OnlyForTheCurrentRange()
    {
        // Movie Detail's highlight cards.
        var (movieId, highlightId) = await SeedAsync(600, 640);
        var highlights = new MovieHighlightService(factory, CreateService());
        Assert.Null((await highlights.GetHighlightsAsync(movieId))[0].ThumbVersion);

        await CreateService().GenerateAsync(highlightId);
        var item = (await highlights.GetHighlightsAsync(movieId))[0];
        Assert.NotNull(item.ThumbVersion);
        Assert.NotNull(item.PreviewVersion);

        await highlights.UpdateHighlightAsync(highlightId, 600, 650, null);
        item = (await highlights.GetHighlightsAsync(movieId))[0];
        Assert.Null(item.ThumbVersion);
        Assert.Null(item.PreviewVersion);
    }

    [Fact]
    public async Task HighlightList_DoesNotAdvertiseMediaTheEndpointWouldRefuse()
    {
        // A missing file or an expired row would 404 at /highlight-image.
        var (movieId, highlightId) = await SeedAsync();
        await CreateService().GenerateAsync(highlightId);
        var highlights = new MovieHighlightService(factory, CreateService());

        var thumb = (await RowsAsync()).Single(r => r.Variant == "thumb");
        File.Delete(imageCache.GetStoragePath(thumb.StorageId));
        var item = (await highlights.GetHighlightsAsync(movieId))[0];
        Assert.Null(item.ThumbVersion);
        Assert.NotNull(item.PreviewVersion);
        var card = Assert.Single((await new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory), CreateService()))
            .GetHighlightPageAsync(new SceneWallFilter(), SceneWallSort.DateAdded, 1, 0, 10)).Cards);
        Assert.Null(card.ThumbVersion);
        Assert.NotNull(card.PreviewVersion);

        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow.AddDays(-10)));
        }
        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.LocalMovies, TimeSpan.FromDays(1), null);
        Assert.Null((await highlights.GetHighlightsAsync(movieId))[0].PreviewVersion);
    }

    [Fact]
    public async Task HighlightList_AdvertisesOnlyVariantsTheCurrentModeServes()
    {
        var (movieId, highlightId) = await SeedAsync();
        await CreateService().GenerateAsync(highlightId);

        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.Thumbnails, null, null);
        var item = (await new MovieHighlightService(factory, CreateService()).GetHighlightsAsync(movieId))[0];

        Assert.NotNull(item.ThumbVersion);
        Assert.Null(item.PreviewVersion);
    }

    [Theory]
    [InlineData(ImageCacheMode.Disabled, false, false)]
    [InlineData(ImageCacheMode.Thumbnails, true, false)]
    [InlineData(ImageCacheMode.LocalMovies, true, true)]
    public async Task CacheMode_LimitsTheVariants(ImageCacheMode mode, bool thumb, bool preview)
    {
        imageCache.Settings = new ImageCacheSettings(mode, null, null);
        var (_, highlightId) = await SeedAsync();

        await CreateService().GenerateAsync(highlightId);

        var variants = (await RowsAsync()).Select(r => r.Variant).ToList();
        Assert.Equal(thumb, variants.Contains("thumb"));
        Assert.Equal(preview, variants.Contains("preview"));
        Assert.Equal(thumb, CreateService().ServesVariant("thumb"));
        Assert.Equal(preview, CreateService().ServesVariant("preview"));
    }

    [Fact]
    public async Task UnreachableFile_WritesNothing()
    {
        var (_, highlightId) = await SeedAsync();
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Null(await CreateService().GenerateAsync(highlightId));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task AFailedPreviewBesideAWrittenStill_IsRememberedAsFailed()
    {
        var (movieId, highlightId) = await SeedAsync();
        ffmpeg.ExtractPreviewVideoAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("preview", call.ArgAt<double>(1), call.ArgAt<double>(2), call.ArgAt<bool>(3)));
                return FfmpegRunResult.Failed("boom");
            });
        SceneMediaQueue backgroundQueue = null!;
        var services = new ServiceCollection()
            .AddSingleton<IHighlightMediaService>(_ => new HighlightMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, backgroundQueue))
            .BuildServiceProvider();
        backgroundQueue = new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        using var disposeQueue = backgroundQueue;
        await backgroundQueue.StartAsync(CancellationToken.None);
        var service = new HighlightMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, backgroundQueue);

        // The still was written, so the run still reports the movie.
        Assert.Equal(movieId, await service.GenerateAsync(highlightId));
        await service.EnsureQueuedAsync(movieId);
        await Task.Delay(300);

        Assert.Single(calls, c => c.Kind == "preview");
        Assert.Equal(["thumb"], (await RowsAsync()).Select(r => r.Variant));
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(ImageCacheMode.LocalMovies)]
    [InlineData(ImageCacheMode.Disabled)]
    public async Task EnsureQueued_AlsoQueuesTheHighlightsTrickplay_WhateverTheCacheMode(ImageCacheMode mode)
    {
        // Queued with the media, so playing the highlight doesn't have to.
        imageCache.Settings = new ImageCacheSettings(mode, null, null);
        var trickplay = Substitute.For<IHighlightTrickplayService>();
        var (movieId, _) = await SeedAsync();

        await new HighlightMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, queue, trickplay).EnsureQueuedAsync(movieId);

        await trickplay.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestingMissingMedia_AlsoQueuesTheHighlightsTrickplay_ButCurrentMediaDoesnt()
    {
        var trickplay = Substitute.For<IHighlightTrickplayService>();
        var service = new HighlightMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, queue, trickplay);
        var (movieId, highlightId) = await SeedAsync();

        Assert.Null(await service.GetFilePathAsync(highlightId, "thumb"));
        await trickplay.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());

        trickplay.ClearReceivedCalls();
        await service.GenerateAsync(highlightId);
        Assert.NotNull(await service.GetFilePathAsync(highlightId, "thumb"));
        await trickplay.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task Delete_RemovesRowsAndFiles()
    {
        var (_, highlightId) = await SeedAsync();
        await CreateService().GenerateAsync(highlightId);
        var paths = (await RowsAsync()).Select(r => ((ILocalImageCache)imageCache).GetStoragePath(r)).ToList();
        Assert.All(paths, p => Assert.True(File.Exists(p)));

        await CreateService().DeleteForHighlightAsync(highlightId);

        Assert.Empty(await RowsAsync());
        Assert.All(paths, p => Assert.False(File.Exists(p)));
    }
}
