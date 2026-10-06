using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public sealed class SceneMediaServiceTests : IDisposable
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

    public SceneMediaServiceTests()
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

    private SceneMediaService CreateService() => new(factory, localLibrary, ffmpeg, imageCache, maintenance, queue);

    private async Task<(int MovieId, int SceneId)> SeedAsync(double start = 600, double? end = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 3600 };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var scene = new Scene { MovieId = movie.Id, StartSeconds = start, EndSeconds = end };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return (movie.Id, scene.Id);
    }

    private async Task<List<CachedImage>> RowsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CachedImages.OrderBy(c => c.Variant).ToListAsync();
    }

    [Theory]
    [InlineData(600, null, 603, 4)]          // no known end: no middle, so 3 s in
    [InlineData(600, 3600.0, 2098, 4)]      // preview centered on the middle
    [InlineData(600, 606.0, 601, 4)]
    [InlineData(600, 601.5, 600, 1.5)]      // shorter than a preview: all of it
    [InlineData(0, 720.0, 358, 4)]
    public void SampleWindow_StillIsThePreviewsFirstFrame(double start, double? end, double expectedPreviewAt, double expectedPreview)
    {
        var (stillAt, previewAt, preview) = SceneMediaService.SampleWindow(start, end);
        Assert.Equal(previewAt, stillAt);
        Assert.Equal(expectedPreviewAt, previewAt, precision: 3);
        Assert.Equal(expectedPreview, preview, precision: 3);
        if (end is { } e)
        {
            Assert.InRange(previewAt, start, e - preview);
        }
    }

    [Fact]
    public async Task Generate_WritesStillAndPreview_AsSceneCacheRows()
    {
        var (movieId, sceneId) = await SeedAsync();

        var result = await CreateService().GenerateAsync(sceneId);

        Assert.Equal(movieId, result);
        var rows = await RowsAsync();
        Assert.Equal(["preview", "thumb"], rows.Select(r => r.Variant));
        Assert.All(rows, r =>
        {
            Assert.Equal("scene", r.Role);
            Assert.Equal(sceneId, r.Index);
            Assert.Equal("ABC-123", r.Code);
            Assert.Equal(600_000, r.SceneStartMs);
            Assert.Equal(3_600_000, r.SceneEndMs);
            Assert.True(File.Exists(((ILocalImageCache)imageCache).GetStoragePath(r)));
        });
        // The still is a WebP, the preview a WebM video.
        Assert.Equal([".webm", ".webp"], rows.Select(r => Path.GetExtension(((ILocalImageCache)imageCache).GetStoragePath(r))));
        // The scene runs 600–3600 s (the movie's end): preview 2098–2102 around the middle, still at its first frame.
        Assert.Equal([("still", 2098d, 0d, false), ("preview", 2098d, 4d, false)], calls);
        await maintenance.Received(1).EnforceSizeLimitAsync(13, Arg.Any<CancellationToken>());
        Assert.Empty(Directory.GetFiles(imageCache.RootPath, "*.tmp.*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Generate_SideBySideVideo_CropsToOneEye()
    {
        var (_, sceneId) = await SeedAsync();
        ProbeReturns(3840, 1920);

        await CreateService().GenerateAsync(sceneId);

        Assert.All(calls, c => Assert.True(c.LeftEye));
    }

    [Fact]
    public async Task Generate_CurrentMediaIsNotRegenerated_UntilTheStartMoves()
    {
        var (movieId, sceneId) = await SeedAsync();
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        var storageIds = (await RowsAsync()).Select(r => r.StorageId).ToList();
        calls.Clear();

        Assert.Null(await service.GenerateAsync(sceneId));
        Assert.Empty(calls);

        await new MovieSceneService(factory).UpdateSceneAsync(sceneId, 900, null, null);
        Assert.Equal(movieId, await service.GenerateAsync(sceneId));

        var rows = await RowsAsync();
        Assert.All(rows, r => Assert.Equal(900_000, r.SceneStartMs));
        Assert.Equal(storageIds, rows.Select(r => r.StorageId)); // files replaced in place
    }

    [Fact]
    public async Task Generate_FollowsTheCacheMode()
    {
        var (_, sceneId) = await SeedAsync();
        var service = CreateService();

        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.Disabled, null, null);
        Assert.Null(await service.GenerateAsync(sceneId));
        Assert.Empty(await RowsAsync());

        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.Thumbnails, null, null);
        await service.GenerateAsync(sceneId);
        Assert.Equal(["thumb"], (await RowsAsync()).Select(r => r.Variant));
    }

    [Fact]
    public async Task Generate_ExpiredMediaIsRegenerated()
    {
        var (_, sceneId) = await SeedAsync();
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow.AddDays(-10)));
        }
        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.LocalMovies, TimeSpan.FromDays(1), null);
        calls.Clear();

        await service.GenerateAsync(sceneId);

        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Generate_NoLocalFile_WritesNothing()
    {
        var (_, sceneId) = await SeedAsync();
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Null(await CreateService().GenerateAsync(sceneId));

        Assert.Empty(await RowsAsync());
        Assert.Empty(calls);
    }

    [Fact]
    public async Task GetFilePath_ReturnsCurrentFileOnly()
    {
        var (_, sceneId) = await SeedAsync();
        var service = CreateService();

        Assert.Null(await service.GetFilePathAsync(sceneId, "thumb"));
        await service.GenerateAsync(sceneId);

        var path = await service.GetFilePathAsync(sceneId, "thumb");
        Assert.Equal("still", await File.ReadAllTextAsync(path!));
        Assert.Null(await service.GetFilePathAsync(sceneId, "bogus"));
    }

    [Fact]
    public async Task DeletingTheScene_RemovesItsMediaRowsAndFiles()
    {
        var (_, sceneId) = await SeedAsync();
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        var files = (await RowsAsync()).Select(r => ((ILocalImageCache)imageCache).GetStoragePath(r)).ToList();

        await new MovieSceneService(factory, service).DeleteSceneAsync(sceneId);

        Assert.Empty(await RowsAsync());
        Assert.All(files, f => Assert.False(File.Exists(f)));
    }

    [Fact]
    public async Task Generate_ChangingOnlyTheEnd_RegeneratesTheMedia()
    {
        var (movieId, sceneId) = await SeedAsync(start: 600, end: 3600);
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        calls.Clear();

        await new MovieSceneService(factory).UpdateSceneAsync(sceneId, 600, 601, null);

        Assert.Null(await service.GetFilePathAsync(sceneId, SceneMediaService.VariantThumb));
        Assert.Equal(movieId, await service.GenerateAsync(sceneId));
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.InRange(c.At, 600, 601));
        Assert.All(await RowsAsync(), r => Assert.Equal(601_000, r.SceneEndMs));
    }

    [Fact]
    public async Task Generate_AFollowingSceneMovingTheImplicitEnd_RegeneratesTheMedia()
    {
        var (movieId, sceneId) = await SeedAsync(start: 0);
        var service = CreateService();
        var scenes = new MovieSceneService(factory);
        await service.GenerateAsync(sceneId);
        Assert.NotNull((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);

        // Scene 1 had no end, so it ran to the end of the movie; adding scene 2 ends it at 12:00.
        var nextId = (await scenes.AddSceneAsync(movieId, 720, null, null)).SceneId!.Value;
        Assert.Null((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);
        Assert.Null(await service.GetFilePathAsync(sceneId, SceneMediaService.VariantPreview));

        calls.Clear();
        Assert.Equal(movieId, await service.GenerateAsync(sceneId));
        Assert.All(calls, c => Assert.InRange(c.At, 0, 720));
        Assert.NotNull((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);

        // Deleting scene 2 lets scene 1 run to the end of the movie again.
        await scenes.DeleteSceneAsync(nextId);
        Assert.Null((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);
    }

    [Fact]
    public async Task SceneList_ReportsMediaVersions_OnlyForTheCurrentStart()
    {
        var (movieId, sceneId) = await SeedAsync();
        var scenes = new MovieSceneService(factory);
        Assert.Null((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);

        await CreateService().GenerateAsync(sceneId);
        var item = (await scenes.GetScenesAsync(movieId))[0];
        Assert.NotNull(item.ThumbVersion);
        Assert.NotNull(item.PreviewVersion);

        await scenes.UpdateSceneAsync(sceneId, 700, null, null);
        Assert.Null((await scenes.GetScenesAsync(movieId))[0].ThumbVersion);
    }

    [Theory]
    [InlineData(ImageCacheMode.LocalMovies, true, true)]
    [InlineData(ImageCacheMode.Thumbnails, true, false)]
    [InlineData(ImageCacheMode.Disabled, false, false)]
    public async Task SceneListAndWall_AdvertiseOnlyVariantsTheCurrentModeServes(ImageCacheMode mode, bool thumb, bool preview)
    {
        // Media generated under a mode that caches both, then the mode is narrowed
        // without clearing the cache. Rows remain, but /scene-image would 404 for a disabled variant.
        var (movieId, sceneId) = await SeedAsync();
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        Assert.Equal(["preview", "thumb"], (await RowsAsync()).Select(r => r.Variant));

        imageCache.Settings = new ImageCacheSettings(mode, null, null);
        var scenes = new MovieSceneService(factory, service);

        var item = (await scenes.GetScenesAsync(movieId))[0];
        Assert.Equal(thumb, item.ThumbVersion is not null);
        Assert.Equal(preview, item.PreviewVersion is not null);
        Assert.Equal(thumb, await service.GetFilePathAsync(sceneId, SceneMediaService.VariantThumb) is not null);
        Assert.Equal(preview, await service.GetFilePathAsync(sceneId, SceneMediaService.VariantPreview) is not null);

        var card = Assert.Single((await new RefreshingSceneWall(factory, new SceneWallQueryService(factory, scenes))
            .GetPageAsync(new SceneWallFilter(), SceneWallSort.DateAdded, 1, 0, 10)).Cards);
        Assert.Equal(thumb, card.ThumbVersion is not null);
        Assert.Equal(preview, card.PreviewVersion is not null);
    }

    [Fact]
    public async Task SceneListAndWall_DoNotAdvertiseMediaTheEndpointWouldRefuse()
    {
        // A row whose file is gone, or which expired, made the wall request an image
        // /scene-image answers with 404 — a broken image instead of the placeholder.
        var (movieId, sceneId) = await SeedAsync();
        var service = CreateService();
        await service.GenerateAsync(sceneId);
        var scenes = new MovieSceneService(factory, service);
        var wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, scenes));

        var thumb = (await RowsAsync()).Single(r => r.Variant == "thumb");
        File.Delete(imageCache.GetStoragePath(thumb.StorageId));
        var item = (await scenes.GetScenesAsync(movieId))[0];
        Assert.Null(item.ThumbVersion);
        Assert.NotNull(item.PreviewVersion);
        Assert.Null(Assert.Single((await wall.GetPageAsync(new SceneWallFilter(), SceneWallSort.DateAdded, 1, 0, 10)).Cards).ThumbVersion);

        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow.AddDays(-10)));
        }
        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.LocalMovies, TimeSpan.FromDays(1), null);
        Assert.Null((await scenes.GetScenesAsync(movieId))[0].PreviewVersion);
    }

    [Fact]
    public async Task Queue_RunsGenerationInTheBackground_AndReportsTheMovie()
    {
        var (movieId, sceneId) = await SeedAsync();
        var media = Substitute.For<ISceneMediaService>();
        media.GenerateAsync(sceneId, Arg.Any<CancellationToken>()).Returns(movieId);
        var services = new ServiceCollection().AddSingleton(media).BuildServiceProvider();
        using var backgroundQueue = new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        var generated = new TaskCompletionSource<int>();
        backgroundQueue.Generated += id => generated.TrySetResult(id);

        await backgroundQueue.StartAsync(CancellationToken.None);
        backgroundQueue.Enqueue(sceneId, movieId, new SceneMediaWindow(600_000, 3_600_000));

        Assert.Equal(movieId, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Queue_WaitsForAMovieWhoseScenesJustChanged_ThenGenerates()
    {
        var (movieId, sceneId) = await SeedAsync();
        var media = Substitute.For<ISceneMediaService>();
        media.GenerateAsync(sceneId, Arg.Any<CancellationToken>()).Returns(movieId);
        var services = new ServiceCollection().AddSingleton(media).BuildServiceProvider();
        using var backgroundQueue = new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance)
        {
            SettleDelay = TimeSpan.FromMilliseconds(600)
        };
        var generated = new TaskCompletionSource<int>();
        backgroundQueue.Generated += id => generated.TrySetResult(id);
        await backgroundQueue.StartAsync(CancellationToken.None);
        var window = new SceneMediaWindow(600_000, 3_600_000);

        backgroundQueue.NoteScenesChanged(movieId);
        var started = DateTime.UtcNow;
        backgroundQueue.Enqueue(sceneId, movieId, window);
        await Task.Delay(200);
        // Another edit restarts the wait; a repeat request while deferred doesn't queue it twice.
        backgroundQueue.NoteScenesChanged(movieId);
        backgroundQueue.Enqueue(sceneId, movieId, window);

        Assert.Equal(movieId, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(750), "generation ran before the scenes settled");
        await Task.Delay(300);
        await media.Received(1).GenerateAsync(sceneId, Arg.Any<CancellationToken>());
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Queue_AFailedPreviewBesideAWrittenStill_IsRememberedAsFailed()
    {
        var (movieId, _) = await SeedAsync();
        ffmpeg.ExtractPreviewVideoAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("preview", call.ArgAt<double>(1), call.ArgAt<double>(2), call.ArgAt<bool>(3)));
                return FfmpegRunResult.Failed("boom");
            });
        SceneMediaQueue backgroundQueue = null!;
        var services = new ServiceCollection()
            .AddSingleton<ISceneMediaService>(_ => new SceneMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, backgroundQueue))
            .BuildServiceProvider();
        backgroundQueue = new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        using var disposeQueue = backgroundQueue;
        var generated = new TaskCompletionSource<int>();
        backgroundQueue.Generated += id => generated.TrySetResult(id);
        await backgroundQueue.StartAsync(CancellationToken.None);
        var service = new SceneMediaService(factory, localLibrary, ffmpeg, imageCache, maintenance, backgroundQueue);

        await service.EnsureQueuedAsync(movieId);
        // The still was written, so the job still reports the movie.
        Assert.Equal(movieId, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await service.EnsureQueuedAsync(movieId);
        await Task.Delay(300);

        Assert.Single(calls, c => c.Kind == "preview");
        Assert.Equal(["thumb"], (await RowsAsync()).Select(r => r.Variant));
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SceneRangeEdits_AreReported_TitleOnlyEditsAreNot()
    {
        var (movieId, sceneId) = await SeedAsync();
        var media = Substitute.For<ISceneMediaService>();
        var scenes = new MovieSceneService(factory, media);

        var addedId = (await scenes.AddSceneAsync(movieId, 1200, null, null)).SceneId!.Value;
        await scenes.UpdateSceneAsync(sceneId, 600, null, "Just a title");
        media.Received(1).NoteScenesChanged(movieId);

        await scenes.UpdateSceneAsync(sceneId, 650, null, "Just a title");
        await scenes.DeleteSceneAsync(addedId);
        media.Received(3).NoteScenesChanged(movieId);
    }
}
