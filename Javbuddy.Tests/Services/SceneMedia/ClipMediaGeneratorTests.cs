using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public sealed class ClipMediaGeneratorTests : IDisposable
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

    private static readonly SceneMediaWindow Window = new(10_000, 20_000);

    private readonly TestDbContextFactory factory = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "javbuddy-clipgen-" + Guid.NewGuid().ToString("N"));
    private readonly FakeImageCache imageCache;
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly IImageCacheMaintenanceService maintenance = Substitute.For<IImageCacheMaintenanceService>();
    private readonly string videoPath;
    private readonly List<(string Kind, double At, double Duration, bool LeftEye)> calls = [];
    private int movieId;

    public ClipMediaGeneratorTests()
    {
        var folder = Path.Combine(root, "library", "ABC-123");
        Directory.CreateDirectory(folder);
        videoPath = Path.Combine(folder, "ABC-123.mp4");
        File.WriteAllText(videoPath, "");
        imageCache = new FakeImageCache(Path.Combine(root, "cache"));
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        ffmpeg.ProbeAsync(videoPath, Arg.Any<CancellationToken>()).Returns(new FfprobeMediaInfo { DurationSeconds = 3600, Width = 1920, Height = 1080 });
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
                File.WriteAllText(call.ArgAt<string>(4), "preview");
                return FfmpegRunResult.Ok();
            });
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private ClipMediaGenerator Create() => new(factory, localLibrary, ffmpeg, imageCache, maintenance);

    private async Task SeedMovieAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123" };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        movieId = movie.Id;
    }

    private async Task<List<CachedImage>> RowsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CachedImages.OrderBy(c => c.Variant).ToListAsync();
    }

    [Fact]
    public void EnabledVariants_FollowTheCacheMode()
    {
        var generator = Create();
        Assert.Equal([SceneMediaService.VariantThumb, SceneMediaService.VariantPreview], generator.EnabledVariants);

        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.Thumbnails, null, null);
        Assert.Equal([SceneMediaService.VariantThumb], generator.EnabledVariants);

        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.Disabled, null, null);
        Assert.Empty(generator.EnabledVariants);
    }

    [Fact]
    public async Task GenerateAsync_WritesBothVariantsStampedWithTheWindow()
    {
        await SeedMovieAsync();

        var outcome = await Create().GenerateAsync("scene", 5, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(new ClipGenerateOutcome(true, false), outcome);
        var rows = await RowsAsync();
        Assert.Equal(["preview", "thumb"], rows.Select(r => r.Variant));
        Assert.All(rows, r =>
        {
            Assert.Equal("scene", r.Role);
            Assert.Equal(5, r.Index);
            Assert.Equal("ABC-123", r.Code);
            Assert.Equal(10_000, r.SceneStartMs);
            Assert.Equal(20_000, r.SceneEndMs);
            Assert.True(File.Exists(((ILocalImageCache)imageCache).GetStoragePath(r)));
        });
        await maintenance.Received(1).EnforceSizeLimitAsync(Arg.Is<long>(n => n > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_UnknownEnd_SamplesWithTheProbedDuration()
    {
        await SeedMovieAsync();

        await Create().GenerateAsync("highlight", 1, movieId, 100, null, new SceneMediaWindow(100_000, null), [], CancellationToken.None);

        var expected = SceneMediaService.SampleWindow(100, 3600);
        Assert.Contains(calls, c => c.Kind == "preview" && c.At == expected.PreviewAt && c.Duration == expected.PreviewSeconds);
    }

    [Fact]
    public async Task GenerateAsync_SideBySideVideo_UsesLeftEyeOnly()
    {
        await SeedMovieAsync();
        ffmpeg.ProbeAsync(videoPath, Arg.Any<CancellationToken>()).Returns(new FfprobeMediaInfo { DurationSeconds = 3600, Width = 3840, Height = 1080 });

        await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.True(c.LeftEye));
    }

    [Fact]
    public async Task GenerateAsync_RegularVideo_UsesTheWholeFrame()
    {
        await SeedMovieAsync();

        await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.False(c.LeftEye));
    }

    [Fact]
    public async Task GenerateAsync_CurrentRows_DoNothing()
    {
        await SeedMovieAsync();
        var generator = Create();
        await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);
        var rows = await RowsAsync();
        calls.Clear();

        var outcome = await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, rows, CancellationToken.None);

        Assert.Equal(ClipGenerateOutcome.NothingToDo, outcome);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task GenerateAsync_WindowChanged_RegeneratesOverTheSameStorageId()
    {
        await SeedMovieAsync();
        var generator = Create();
        await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);
        var before = await RowsAsync();

        var outcome = await generator.GenerateAsync("scene", 1, movieId, 30, 40, new SceneMediaWindow(30_000, 40_000), before, CancellationToken.None);

        Assert.True(outcome.WroteAny);
        var after = await RowsAsync();
        Assert.Equal(before.Select(r => r.StorageId).Order(), after.Select(r => r.StorageId).Order());
        Assert.All(after, r => Assert.Equal(30_000, r.SceneStartMs));
    }

    [Fact]
    public async Task GenerateAsync_VariantsFilter_OnlyMakesTheRequestedOnes()
    {
        await SeedMovieAsync();

        await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None, [SceneMediaService.VariantThumb]);

        Assert.Equal(["thumb"], (await RowsAsync()).Select(r => r.Variant));
        Assert.DoesNotContain(calls, c => c.Kind == "preview");
    }

    [Fact]
    public async Task GenerateAsync_ExpiredRow_IsRegenerated()
    {
        await SeedMovieAsync();
        var generator = Create();
        await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);
        var rows = await RowsAsync();
        foreach (var row in rows) row.UpdatedAt = DateTime.UtcNow.AddHours(-5);
        imageCache.Settings = new ImageCacheSettings(ImageCacheMode.LocalMovies, TimeSpan.FromHours(1), null);
        calls.Clear();

        var outcome = await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, rows, CancellationToken.None);

        Assert.True(outcome.WroteAny);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task GenerateAsync_NoVideoFile_Fails()
    {
        await SeedMovieAsync();
        File.Delete(videoPath);

        var outcome = await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(ClipGenerateOutcome.Failed, outcome);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task GenerateAsync_ProbeFails_Fails()
    {
        await SeedMovieAsync();
        ffmpeg.ProbeAsync(videoPath, Arg.Any<CancellationToken>()).Returns((FfprobeMediaInfo?)null);

        var outcome = await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(ClipGenerateOutcome.Failed, outcome);
    }

    [Fact]
    public async Task GenerateAsync_OneVariantFails_StillWritesTheOtherAndReportsFailure()
    {
        await SeedMovieAsync();
        ffmpeg.ExtractPreviewVideoAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FfmpegRunResult.Failed("boom"));

        var outcome = await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);

        Assert.Equal(new ClipGenerateOutcome(true, true), outcome);
        Assert.Equal(["thumb"], (await RowsAsync()).Select(r => r.Variant));
    }

    [Fact]
    public async Task GenerateAsync_EmptyOutputFile_CountsAsFailedAndLeavesNoTempFile()
    {
        await SeedMovieAsync();
        string? tempPath = null;
        ffmpeg.ExtractStillWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                tempPath = call.ArgAt<string>(3);
                File.WriteAllText(tempPath, "");
                return FfmpegRunResult.Ok();
            });

        var outcome = await Create().GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None, [SceneMediaService.VariantThumb]);

        Assert.Equal(ClipGenerateOutcome.Failed, outcome);
        Assert.NotNull(tempPath); // ffmpeg was asked to write somewhere
        Assert.False(File.Exists(tempPath));
        await maintenance.DidNotReceive().EnforceSizeLimitAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RemovesRowsAndFilesOfThatClipOnly()
    {
        await SeedMovieAsync();
        var generator = Create();
        await generator.GenerateAsync("scene", 1, movieId, 10, 20, Window, [], CancellationToken.None);
        await generator.GenerateAsync("scene", 2, movieId, 10, 20, Window, [], CancellationToken.None);
        var doomed = (await RowsAsync()).Where(r => r.Index == 1).ToList();

        await generator.DeleteAsync("scene", 1, CancellationToken.None);

        var remaining = await RowsAsync();
        Assert.All(remaining, r => Assert.Equal(2, r.Index));
        Assert.Equal(2, remaining.Count);
        Assert.All(doomed, r => Assert.False(File.Exists(((ILocalImageCache)imageCache).GetStoragePath(r))));
    }
}
