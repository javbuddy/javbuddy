using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public sealed class ApexMediaServiceTests : IDisposable
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
    private readonly List<(string Kind, double At, double Duration)> calls = [];

    public ApexMediaServiceTests()
    {
        var folder = Path.Combine(root, "library", "ABC-123");
        Directory.CreateDirectory(folder);
        videoPath = Path.Combine(folder, "ABC-123.mp4");
        File.WriteAllText(videoPath, "");
        imageCache = new FakeImageCache(Path.Combine(root, "cache"));
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        ffmpeg.ProbeAsync(videoPath, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 3600, Width = 1920, Height = 1080 });

        // A fake ffmpeg that "encodes" by writing a few bytes to the requested output.
        ffmpeg.ExtractStillWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("still", call.ArgAt<double>(1), 0));
                File.WriteAllText(call.ArgAt<string>(3), "still");
                return FfmpegRunResult.Ok();
            });
        ffmpeg.ExtractPreviewVideoAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(("preview", call.ArgAt<double>(1), call.ArgAt<double>(2)));
                File.WriteAllText(call.ArgAt<string>(4), "preview!");
                return FfmpegRunResult.Ok();
            });
    }

    public void Dispose()
    {
        queue.Dispose();
        factory.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ApexMediaService CreateService(SceneMediaQueue? withQueue = null) =>
        new(factory, localLibrary, ffmpeg, imageCache, maintenance, withQueue ?? queue);

    private async Task<(int MovieId, int ApexId)> SeedAsync(double seconds = 900)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 3600 };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var apex = new MovieApex { MovieId = movie.Id, Seconds = seconds };
        db.MovieApexes.Add(apex);
        await db.SaveChangesAsync();
        return (movie.Id, apex.Id);
    }

    private async Task MoveApexAsync(double seconds)
    {
        await using var db = await factory.CreateDbContextAsync();
        var apex = await db.MovieApexes.SingleAsync();
        apex.Seconds = seconds;
        await db.SaveChangesAsync();
    }

    private async Task<List<CachedImage>> RowsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CachedImages.OrderBy(c => c.Variant).ToListAsync();
    }

    [Fact]
    public async Task Generate_WritesAStillAndAPreview_CentredOnTheApex()
    {
        var (movieId, apexId) = await SeedAsync(900);

        Assert.Equal(movieId, await CreateService().GenerateAsync(apexId));

        var rows = await RowsAsync();
        Assert.Equal(
            [("apex", apexId, "preview", 900_000L, (long?)900_000L), ("apex", apexId, "thumb", 900_000L, (long?)900_000L)],
            rows.Select(r => (r.Role, r.Index, r.Variant, r.SceneStartMs, r.SceneEndMs)));
        // The screenshot is the frame the preview starts on.
        Assert.Equal([("still", 898d, 0d), ("preview", 898d, 4d)], calls);
        Assert.NotNull(await CreateService().GetFilePathAsync(apexId, "preview"));
        Assert.NotNull(await CreateService().GetFilePathAsync(apexId, "thumb"));
    }

    [Theory]
    [InlineData(1, 0, 3)]
    [InlineData(3599, 3597, 3)]
    public async Task Generate_KeepsTheClipInsideTheMovie(double seconds, double at, double duration)
    {
        var (_, apexId) = await SeedAsync(seconds);

        await CreateService().GenerateAsync(apexId);

        Assert.Equal([("still", at, 0d), ("preview", at, duration)], calls);
    }

    [Fact]
    public async Task MovingTheApex_MakesItsMediaStale()
    {
        var (_, apexId) = await SeedAsync(900);
        await CreateService().GenerateAsync(apexId);

        await MoveApexAsync(950);

        Assert.Null(await CreateService().GetFilePathAsync(apexId, "preview"));
        Assert.Null(await CreateService().GetFilePathAsync(apexId, "thumb"));
        calls.Clear();
        await CreateService().GenerateAsync(apexId);
        Assert.Equal([("still", 948d, 0d), ("preview", 948d, 4d)], calls);
    }

    [Fact]
    public async Task UnknownVariant_IsNeverServed()
    {
        var (_, apexId) = await SeedAsync();
        await CreateService().GenerateAsync(apexId);

        Assert.Null(await CreateService().GetFilePathAsync(apexId, "full"));
        Assert.False(CreateService().ServesVariant("full"));
    }

    [Theory]
    [InlineData(ImageCacheMode.Thumbnails, false, 1)]
    [InlineData(ImageCacheMode.LocalMovies, true, 2)]
    public async Task CacheMode_DecidesWhichMediaIsMade(ImageCacheMode mode, bool preview, int rows)
    {
        imageCache.Settings = new ImageCacheSettings(mode, null, null);
        var (_, apexId) = await SeedAsync();

        await CreateService().GenerateAsync(apexId);

        Assert.Equal(preview, CreateService().ServesPreview);
        Assert.True(CreateService().ServesVariant("thumb"));
        Assert.Equal(rows, (await RowsAsync()).Count);
    }

    /// <summary>A service whose queue runs for real, so EnsureQueued's work is generated in the background.</summary>
    private async Task<(IApexMediaService Service, SceneMediaQueue Queue, TaskCompletionSource<int> Generated)> StartBackgroundAsync(int movieId)
    {
        SceneMediaQueue backgroundQueue = null!;
        var services = new ServiceCollection()
            .AddSingleton<IApexMediaService>(_ => CreateService(backgroundQueue))
            .BuildServiceProvider();
        backgroundQueue = new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        var generated = new TaskCompletionSource<int>();
        backgroundQueue.Generated += id => generated.TrySetResult(id);
        // Apexes are single moments: a scene edit's settle delay doesn't hold them back.
        backgroundQueue.NoteScenesChanged(movieId);
        await backgroundQueue.StartAsync(CancellationToken.None);
        return (CreateService(backgroundQueue), backgroundQueue, generated);
    }

    [Fact]
    public async Task EnsureQueued_GeneratesMissingMedia_InTheBackground()
    {
        var (movieId, apexId) = await SeedAsync();
        var (service, backgroundQueue, generated) = await StartBackgroundAsync(movieId);
        using var disposeQueue = backgroundQueue;

        await service.EnsureQueuedAsync(movieId);

        Assert.Equal(movieId, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(await CreateService().GetFilePathAsync(apexId, "preview"));
        Assert.NotNull(await CreateService().GetFilePathAsync(apexId, "thumb"));
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EnsureQueued_AnApexWithAPreviewButNoStill_GetsOnlyTheStill()
    {
        // What an apex made before screenshots existed looks like: a current preview and nothing else.
        var (movieId, apexId) = await SeedAsync(900);
        await CreateService().GenerateAsync(apexId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var thumb = await db.CachedImages.SingleAsync(c => c.Variant == "thumb");
            File.Delete(((ILocalImageCache)imageCache).GetStoragePath(thumb));
            db.CachedImages.Remove(thumb);
            await db.SaveChangesAsync();
        }
        calls.Clear();
        var (service, backgroundQueue, generated) = await StartBackgroundAsync(movieId);
        using var disposeQueue = backgroundQueue;

        await service.EnsureQueuedAsync(movieId);

        Assert.Equal(movieId, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal([("still", 898d, 0d)], calls);
        Assert.NotNull(await CreateService().GetFilePathAsync(apexId, "thumb"));
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EnsureQueued_WithCurrentMedia_QueuesNothing()
    {
        var (movieId, apexId) = await SeedAsync();
        await CreateService().GenerateAsync(apexId);
        calls.Clear();
        var (service, backgroundQueue, _) = await StartBackgroundAsync(movieId);
        using var disposeQueue = backgroundQueue;

        await service.EnsureQueuedAsync(movieId);
        await Task.Delay(300);

        Assert.Empty(calls);
        await backgroundQueue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Delete_RemovesRowsAndFiles()
    {
        var (_, apexId) = await SeedAsync();
        await CreateService().GenerateAsync(apexId);
        var rows = await RowsAsync();
        Assert.Equal(2, rows.Count);
        var paths = rows.Select(r => ((ILocalImageCache)imageCache).GetStoragePath(r)).ToList();
        Assert.All(paths, path => Assert.True(File.Exists(path)));

        await CreateService().DeleteForApexAsync(apexId);

        Assert.Empty(await RowsAsync());
        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }
}
