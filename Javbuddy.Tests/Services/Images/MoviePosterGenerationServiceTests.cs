using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public sealed class MoviePosterGenerationServiceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "javbuddy-poster-" + Guid.NewGuid().ToString("N"));
    private readonly TestDbContextFactory db = new();
    private readonly ILocalLibraryClient library = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();

    public MoviePosterGenerationServiceTests()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "abc-001.mp4"), [0]);
        library.ResolveMovieFolderPathAsync("abc-001", Arg.Any<CancellationToken>()).Returns(folder);
        ffmpeg.ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                WriteFrame(call.ArgAt<string>(3));
                return FfmpegRunResult.Ok();
            });
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private static void WriteFrame(string path)
    {
        using var bitmap = new SKBitmap(640, 360);
        bitmap.Erase(new SKColor(120, 120, 120));
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
        File.WriteAllBytes(path, data.ToArray());
    }

    private async Task<int> AddMovieAsync(double? duration = 600)
    {
        await using var context = await db.CreateDbContextAsync();
        var movie = new Movie { Code = "abc-001", MediaDurationSeconds = duration };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie.Id;
    }

    private MoviePosterGenerationService CreateService() =>
        new(db, library, ffmpeg, NullLogger<MoviePosterGenerationService>.Instance);

    [Fact]
    public async Task Writes_a_portrait_poster_and_a_full_frame_fanart_and_syncs_the_image_signature()
    {
        var id = await AddMovieAsync();

        var result = await CreateService().GenerateAsync(id);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(new[] { "poster.jpg", "fanart.jpg" }, name => Assert.Equal([0xFF, 0xD8], File.ReadAllBytes(Path.Combine(folder, name))[..2]));
        using var poster = SKBitmap.Decode(Path.Combine(folder, "poster.jpg"));
        using var fanart = SKBitmap.Decode(Path.Combine(folder, "fanart.jpg"));
        Assert.Equal(360, poster.Height);
        Assert.InRange((double)poster.Width / poster.Height, 0.65, 0.68);
        Assert.Equal(640, fanart.Width);
        await library.Received(1).SyncImagesSignatureAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Never_overwrites_an_existing_image()
    {
        var id = await AddMovieAsync();
        await File.WriteAllTextAsync(Path.Combine(folder, "folder.jpg"), "keep");

        var result = await CreateService().GenerateAsync(id);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(folder, "folder.jpg")));
        Assert.False(File.Exists(Path.Combine(folder, "poster.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "fanart.jpg")));
    }

    [Fact]
    public async Task Fails_without_touching_ffmpeg_when_both_images_exist()
    {
        var id = await AddMovieAsync();
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "fanart.jpg"), "x");

        var result = await CreateService().GenerateAsync(id);

        Assert.False(result.Success);
        await ffmpeg.DidNotReceiveWithAnyArgs().ExtractFrameWebpAsync(default!, default, default, default!, default);
    }

    [Fact]
    public async Task Fails_when_the_video_has_not_been_scanned()
    {
        var id = await AddMovieAsync(duration: null);

        Assert.False((await CreateService().GenerateAsync(id)).Success);
    }

    [Fact]
    public async Task A_frame_too_small_to_crop_fails_without_a_poster_or_stray_temp_files()
    {
        var id = await AddMovieAsync();
        ffmpeg.ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var bitmap = new SKBitmap(640, 1);
                bitmap.Erase(new SKColor(120, 120, 120));
                using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
                File.WriteAllBytes(call.ArgAt<string>(3), data.ToArray());
                return FfmpegRunResult.Ok();
            });

        var result = await CreateService().GenerateAsync(id);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(folder, "poster.jpg")));
        Assert.False(File.Exists(Path.Combine(folder, "fanart.jpg")), "a failed poster must not leave a lone fanart that blocks generating it later");
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        await library.DidNotReceiveWithAnyArgs().SyncImagesSignatureAsync(default, default);
    }

    [Fact]
    public async Task Fails_and_leaves_no_images_when_no_frame_can_be_extracted()
    {
        var id = await AddMovieAsync();
        ffmpeg.ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FfmpegRunResult.Failed("boom"));

        var result = await CreateService().GenerateAsync(id);

        Assert.False(result.Success);
        Assert.Contains("boom", result.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(folder, "poster.jpg")));
    }
}
