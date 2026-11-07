using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Movies;

public sealed class MovieExtraFanartStillsServiceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "javbuddy-stills-" + Guid.NewGuid().ToString("N"));
    private readonly TestDbContextFactory db = new();
    private readonly ILocalLibraryClient library = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly IMovieExtraFanartService extraFanart = Substitute.For<IMovieExtraFanartService>();
    private readonly List<double> requestedSeconds = [];

    public MovieExtraFanartStillsServiceTests()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "abc-001.mp4"), [0]);
        library.ResolveMovieFolderPathAsync("abc-001", Arg.Any<CancellationToken>()).Returns(folder);
        library.ListExtraFanartFileNamesAsync("abc-001", Arg.Any<CancellationToken>()).Returns(new List<string>());
        ffmpeg.ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedSeconds.Add(call.ArgAt<double>(1));
                using var bitmap = new SKBitmap(1280, 720);
                bitmap.Erase(new SKColor(120, 120, 120));
                using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
                File.WriteAllBytes(call.ArgAt<string>(3), data.ToArray());
                return FfmpegRunResult.Ok();
            });
        extraFanart.AddAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<ExtraFanartUploadItem>>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(call => ExtraFanartResult.Ok(call.ArgAt<IReadOnlyList<ExtraFanartUploadItem>>(1).Count));
        extraFanart.DeleteAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ExtraFanartResult.Ok());
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private async Task<int> AddMovieAsync()
    {
        await using var context = await db.CreateDbContextAsync();
        var movie = new Movie { Code = "abc-001", MediaDurationSeconds = 1000 };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie.Id;
    }

    private MovieExtraFanartStillsService CreateService() =>
        new(db, library, ffmpeg, extraFanart, NullLogger<MovieExtraFanartStillsService>.Instance);

    [Fact]
    public void SlotCandidateTimes_give_each_slot_two_times_inside_it_and_skip_the_first_and_last_tenth()
    {
        var slots = MovieExtraFanartStillsService.SlotCandidateTimes(1000, 4);

        Assert.Equal(4, slots.Count);
        Assert.All(slots, pair => Assert.Equal(2, pair.Length));
        Assert.All(slots.SelectMany(t => t), t => Assert.InRange(t, 100, 900));
        Assert.Equal(slots.Select(s => s[0]).OrderBy(t => t), slots.Select(s => s[0]));
        Assert.True(slots[0][1] < slots[1][0]);
    }

    [Fact]
    public async Task Preview_reports_each_still_as_its_slot_finishes()
    {
        var id = await AddMovieAsync();
        var reported = new List<int>();

        var result = await CreateService().PreviewAsync(id, 3, ExtraFanartStillsMode.Append, false, still => reported.Add(still.Id));

        Assert.Equal([1, 2, 3], reported);
        result.Preview!.Dispose();
    }

    [Fact]
    public async Task Preview_returns_the_requested_count_in_video_order_with_scratch_cleanup()
    {
        var id = await AddMovieAsync();

        var result = await CreateService().PreviewAsync(id, 3, ExtraFanartStillsMode.Append);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(6, requestedSeconds.Count); // twice the count are sampled, the best kept
        Assert.Equal(3, result.Preview!.Stills.Count);
        Assert.Equal(result.Preview.Stills.OrderBy(s => s.Seconds).Select(s => s.Id), result.Preview.Stills.Select(s => s.Id));
        result.Preview.Dispose();
    }

    [Fact]
    public async Task Preview_passes_the_left_eye_flag_to_ffmpeg_and_cleans_up_its_scratch_folder()
    {
        var id = await AddMovieAsync();
        var scratchFolders = () => Directory.GetDirectories(Path.GetTempPath(), "javbuddy-stills-*").ToHashSet();
        var before = scratchFolders();

        var result = await CreateService().PreviewAsync(id, 2, ExtraFanartStillsMode.Append, leftEyeOnly: true);

        await ffmpeg.Received().ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), true, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Single(scratchFolders().Except(before));
        result.Preview!.Dispose();
        Assert.Empty(scratchFolders().Except(before));
    }

    [Fact]
    public async Task Preview_keeps_the_sharper_of_the_two_frames_of_a_slot()
    {
        var id = await AddMovieAsync();
        var calls = 0;
        ffmpeg.ExtractFrameWebpAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Odd calls are a slot's first frame (flat grey), even calls its second (a checkerboard).
                using var bitmap = new SKBitmap(640, 360);
                bitmap.Erase(new SKColor(120, 120, 120));
                if (++calls % 2 == 0)
                {
                    for (var x = 0; x < 640; x++) for (var y = 0; y < 360; y++) if ((x / 4 + y / 4) % 2 == 0) bitmap.SetPixel(x, y, new SKColor(200, 200, 200));
                }
                using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 90);
                File.WriteAllBytes(call.ArgAt<string>(3), data.ToArray());
                return FfmpegRunResult.Ok();
            });
        var slots = MovieExtraFanartStillsService.SlotCandidateTimes(1000, 2);

        var result = await CreateService().PreviewAsync(id, 2, ExtraFanartStillsMode.Append);

        Assert.Equal(slots.Select(s => s[1]), result.Preview!.Stills.Select(s => s.Seconds));
        result.Preview.Dispose();
    }

    [Fact]
    public async Task Preview_in_only_if_none_mode_fails_when_extrafanart_exists()
    {
        var id = await AddMovieAsync();
        library.ListExtraFanartFileNamesAsync("abc-001", Arg.Any<CancellationToken>()).Returns(new List<string> { "fanart1.jpg" });

        var result = await CreateService().PreviewAsync(id, 3, ExtraFanartStillsMode.OnlyIfNone);

        Assert.False(result.Success);
        await ffmpeg.DidNotReceiveWithAnyArgs().ExtractFrameWebpAsync(default!, default, default, default!, default);
    }

    [Fact]
    public async Task Save_adds_only_the_selected_stills_as_webp()
    {
        var id = await AddMovieAsync();
        var service = CreateService();
        using var preview = (await service.PreviewAsync(id, 3, ExtraFanartStillsMode.Append)).Preview!;
        IReadOnlyList<ExtraFanartUploadItem>? added = null;
        await extraFanart.AddAsync(id, Arg.Do<IReadOnlyList<ExtraFanartUploadItem>>(i => added = i), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>());

        var result = await service.SaveAsync(id, preview, [preview.Stills[0].Id, preview.Stills[2].Id], 80, ExtraFanartStillsMode.Append);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, added!.Count);
        using var stream = added[0].OpenStream();
        using var codec = SKCodec.Create(stream);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        await extraFanart.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
    }

    [Fact]
    public async Task Save_in_replace_mode_deletes_the_existing_images_first()
    {
        var id = await AddMovieAsync();
        library.ListExtraFanartFileNamesAsync("abc-001", Arg.Any<CancellationToken>()).Returns(new List<string> { "fanart1.jpg", "fanart2.jpg" });
        var service = CreateService();
        using var preview = (await service.PreviewAsync(id, 2, ExtraFanartStillsMode.ReplaceAll)).Preview!;

        var result = await service.SaveAsync(id, preview, [preview.Stills[0].Id], 85, ExtraFanartStillsMode.ReplaceAll);

        Assert.True(result.Success, result.ErrorMessage);
        await extraFanart.Received(1).DeleteAsync(id, "fanart1.jpg", Arg.Any<CancellationToken>());
        await extraFanart.Received(1).DeleteAsync(id, "fanart2.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_with_nothing_selected_fails()
    {
        var id = await AddMovieAsync();
        var service = CreateService();
        using var preview = (await service.PreviewAsync(id, 2, ExtraFanartStillsMode.Append)).Preview!;

        var result = await service.SaveAsync(id, preview, [], 85, ExtraFanartStillsMode.Append);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Save_in_replace_mode_deletes_nothing_when_a_still_exceeds_the_size_limit()
    {
        var id = await AddMovieAsync();
        library.ListExtraFanartFileNamesAsync("abc-001", Arg.Any<CancellationToken>()).Returns(new List<string> { "fanart1.jpg" });
        var service = new MovieExtraFanartStillsService(db, library, ffmpeg, extraFanart, NullLogger<MovieExtraFanartStillsService>.Instance,
            new Javbuddy.Services.Images.ImageUploadSettings(MaxSizeMb: 1, MaxBatchFiles: 500) with { });
        using var preview = (await service.PreviewAsync(id, 1, ExtraFanartStillsMode.ReplaceAll)).Preview!;
        // Replace the frame with noise so it can't compress under 1 MB.
        var noise = new byte[4000 * 3000 * 4];
        new Random(1).NextBytes(noise);
        using (var big = new SKBitmap(4000, 3000))
        {
            System.Runtime.InteropServices.Marshal.Copy(noise, 0, big.GetPixels(), noise.Length);
            using var data = big.Encode(SKEncodedImageFormat.Webp, 100);
            File.WriteAllBytes(preview.FramePath(preview.Stills[0].Id), data.ToArray());
        }

        var result = await service.SaveAsync(id, preview, [preview.Stills[0].Id], 100, ExtraFanartStillsMode.ReplaceAll);

        Assert.False(result.Success);
        await extraFanart.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
    }
}
