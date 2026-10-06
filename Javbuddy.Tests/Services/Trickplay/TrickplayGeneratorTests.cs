using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayGeneratorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("javbuddy-trickplay-gen-test-").FullName;
    private readonly TestDbContextFactory dbFactory = new();
    private readonly IMovieStreamService streams = Substitute.For<IMovieStreamService>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly TrickplayStore store;
    private FfmpegTrickplayRequest? lastRequest;
    private string? lastOutputDirectory;

    public TrickplayGeneratorTests()
    {
        store = new TrickplayStore(dbFactory, new ObjectStoreProvider(new FileSystemObjectStore(root), root));
        // Writes as many sheets as a real run would (100 thumbnails each).
        ffmpeg.GenerateTrickplayAsync(Arg.Any<string>(), Arg.Any<FfmpegTrickplayRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lastRequest = call.ArgAt<FfmpegTrickplayRequest>(1);
                lastOutputDirectory = call.ArgAt<string>(2);
                var thumbnails = (int)Math.Ceiling(call.ArgAt<TimeSpan>(3).TotalSeconds / 10);
                for (var i = 0; i < (thumbnails + 99) / 100; i++)
                {
                    File.WriteAllText(Path.Combine(call.ArgAt<string>(2), $"{i}.webp"), "tile");
                }
                return FfmpegRunResult.Ok();
            });
    }

    public void Dispose()
    {
        dbFactory.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private IConfiguration Config(Dictionary<string, string?>? extra = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(extra ?? [])
        .Build();

    private readonly TrickplayGenerationTracker tracker = new();

    private TrickplayGenerator Generator(IConfiguration? configuration = null) =>
        new(dbFactory, streams, ffmpeg, store, new TrickplaySettingsService(dbFactory, configuration ?? Config()), tracker, NullLogger<TrickplayGenerator>.Instance);

    private async Task<int> AddMovieAsync(string fileName, double? duration, int? width, int? height, string code = "ABC-123")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = code, Status = MovieStatus.Got, LocalFileSizeBytes = 1 };
        movie.MovieFiles.Add(new MovieFile { FileName = fileName, IsPrimary = true, DurationSeconds = duration, Width = width, Height = height });
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        streams.GetMainFilePathAsync(movie.Id, Arg.Any<CancellationToken>()).Returns($"/library/{code}/{fileName}");
        return movie.Id;
    }

    [Fact]
    public async Task Generates_AFullDecodeSet_DescribedByItsRow()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 7127.8, 1920, 1080);

        Assert.Equal(TrickplayGenerateResult.Generated, await Generator().GenerateAsync(movieId));

        var identity = TrickplayIdentity.For("ABC-123.mp4", 7127.8, 1920, 1080)!;
        var set = await store.GetSetAsync("ABC-123", identity);
        Assert.NotNull(set);
        Assert.Equal((320, 180, 10, 10, 713, 10000, false, false, "ABC-123.mp4"),
            (set.Width, set.Height, set.TileWidth, set.TileHeight, set.ThumbnailCount, set.IntervalMs, set.LeftEyeOnly, set.KeyframeOnly, set.FileName));
        Assert.Equal(
            Enumerable.Range(0, 8).Select(i => $"{i}.webp").Order(),
            Directory.EnumerateFiles(Path.Combine(root, "trickplay", "ABC-123", identity)).Select(Path.GetFileName).Order());
        Assert.Equal(new FfmpegTrickplayRequest(10, 320, 180, 10, 10, false, false), lastRequest);

        // ffmpeg wrote into a local temp folder, not the store, and it's gone.
        Assert.False(lastOutputDirectory!.StartsWith(root, StringComparison.Ordinal));
        Assert.False(Directory.Exists(lastOutputDirectory));
    }

    [Fact]
    public async Task SideBySideVr_UsesTheLeftEye_AndTheKeyframeSettingIsPassedOn()
    {
        var movieId = await AddMovieAsync("VR-001.mp4", 3600, 7680, 3840);
        await new TrickplaySettingsService(dbFactory, Config()).SaveAsync(new TrickplaySettings { KeyframeOnly = true });

        await Generator().GenerateAsync(movieId);

        Assert.Equal(new FfmpegTrickplayRequest(10, 320, 320, 10, 10, LeftEyeOnly: true, KeyframeOnly: true), lastRequest);
    }

    [Fact]
    public async Task AnExistingSet_IsNotGeneratedAgain()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 7127.8, 1920, 1080);
        await Generator().GenerateAsync(movieId);
        ffmpeg.ClearReceivedCalls();

        Assert.Equal(TrickplayGenerateResult.AlreadyCurrent, await Generator().GenerateAsync(movieId));
        await ffmpeg.DidNotReceiveWithAnyArgs().GenerateTrickplayAsync(default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task AnUnprobedFile_OrAFileThatIsntTheRecordedOne_IsSkipped()
    {
        var unprobed = await AddMovieAsync("ABC-123.mp4", null, null, null);
        Assert.Equal(TrickplayGenerateResult.NotProbed, await Generator().GenerateAsync(unprobed));

        var elsewhere = await AddMovieAsync("ABC-124.mp4", 60, 1920, 1080, code: "ABC-124");
        streams.GetMainFilePathAsync(elsewhere, Arg.Any<CancellationToken>()).Returns("/library/ABC-124/other.mkv");
        Assert.Equal(TrickplayGenerateResult.NoFile, await Generator().GenerateAsync(elsewhere));
    }

    [Fact]
    public async Task FfmpegProgress_IsPassedOnToTheCaller()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 60, 1920, 1080);
        ffmpeg.GenerateTrickplayAsync(default!, default!, default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            call.ArgAt<IProgress<FfmpegProgress>?>(4)!.Report(new FfmpegProgress(50, TimeSpan.FromSeconds(30)));
            File.WriteAllText(Path.Combine(call.ArgAt<string>(2), "0.webp"), "tile");
            return FfmpegRunResult.Ok();
        });
        var reports = new List<FfmpegProgress>();

        Assert.Equal(TrickplayGenerateResult.Generated, await Generator().GenerateAsync(movieId, new ImmediateProgress<FfmpegProgress>(reports.Add)));

        Assert.Equal([new FfmpegProgress(50, TimeSpan.FromSeconds(30))], reports);
    }

    [Fact]
    public async Task TheRun_IsTrackedWithItsProgress_UntilTheSetIsSaved()
    {
        // What the players' scrub-bar preview shows while it generates.
        var movieId = await AddMovieAsync("ABC-123.mp4", 60, 1920, 1080);
        var target = new TrickplayTarget(movieId);
        var seen = new List<TrickplayGenerating?>();
        tracker.Changed += changed =>
        {
            if (changed == target) seen.Add(tracker.Get(target));
        };
        ffmpeg.GenerateTrickplayAsync(default!, default!, default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            call.ArgAt<IProgress<FfmpegProgress>?>(4)!.Report(new FfmpegProgress(42.7, TimeSpan.FromSeconds(25)));
            File.WriteAllText(Path.Combine(call.ArgAt<string>(2), "0.webp"), "tile");
            return FfmpegRunResult.Ok();
        });
        tracker.Changed += changed =>
        {
            // Saved by the time the run ends.
            if (changed == target && tracker.Get(target) is null) Assert.Single(store.ListSetsAsync().Result);
        };

        Assert.Equal(TrickplayGenerateResult.Generated, await Generator().GenerateAsync(movieId));

        Assert.Equal([new TrickplayGenerating(null), new TrickplayGenerating(42), null], seen);
    }

    [Fact]
    public async Task AClipsRun_IsTrackedForTheHighlightsRange_AndAFailedOneEndsToo()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 7127.8, 1920, 1080);
        var clip = TrickplayTarget.ForHighlight(movieId, 1000, 1030.5);
        var targets = new List<TrickplayTarget>();
        tracker.Changed += targets.Add;
        ffmpeg.GenerateTrickplayAsync(default!, default!, default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            Assert.NotNull(tracker.Get(clip));
            Assert.Null(tracker.Get(new TrickplayTarget(movieId)));
            return FfmpegRunResult.Failed("boom");
        });

        Assert.Equal(TrickplayGenerateResult.Failed, await Generator().GenerateClipAsync(movieId, 1000, 1030.5));

        Assert.Equal([clip, clip], targets);
        Assert.Null(tracker.Get(clip));
    }

    [Fact]
    public async Task AFailedRun_LeavesNoSetAndNoTempFolder()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 60, 1920, 1080);
        ffmpeg.GenerateTrickplayAsync(default!, default!, default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            lastOutputDirectory = call.ArgAt<string>(2);
            File.WriteAllText(Path.Combine(lastOutputDirectory, "0.webp"), "partial");
            return FfmpegRunResult.Failed("boom");
        });

        Assert.Equal(TrickplayGenerateResult.Failed, await Generator().GenerateAsync(movieId));
        Assert.Empty(await store.ListSetsAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "trickplay", "ABC-123")));
        Assert.NotNull(lastOutputDirectory);
        Assert.False(Directory.Exists(lastOutputDirectory));
    }

    [Fact]
    public async Task Clip_GeneratesADenserSetForJustTheHighlight_WithAFullDecode()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 7127.8, 1920, 1080);
        await new TrickplaySettingsService(dbFactory, Config()).SaveAsync(new TrickplaySettings { KeyframeOnly = true });

        Assert.Equal(TrickplayGenerateResult.Generated, await Generator().GenerateClipAsync(movieId, 1000, 1030.5));

        var fileIdentity = TrickplayIdentity.For("ABC-123.mp4", 7127.8, 1920, 1080)!;
        var identity = HighlightTrickplay.Identity(fileIdentity, 1000, 1030.5)!;
        var set = await store.GetSetAsync("ABC-123", identity);
        Assert.NotNull(set);
        Assert.Equal((31, 1000, 30.5, 1000d, fileIdentity, false),
            (set.ThumbnailCount, set.IntervalMs, set.DurationSeconds, set.StartSeconds, set.SourceIdentity, set.KeyframeOnly));
        Assert.Equal(new FfmpegTrickplayRequest(1, 320, 180, 10, 10, false, false, 1000, 30.5), lastRequest);
        Assert.Equal(["0.webp"], Directory.EnumerateFiles(Path.Combine(root, "trickplay", "ABC-123", identity)).Select(Path.GetFileName));
        // The movie's own set is a separate one, still to be generated.
        Assert.Null(await store.GetSetAsync("ABC-123", fileIdentity));

        Assert.Equal(TrickplayGenerateResult.AlreadyCurrent, await Generator().GenerateClipAsync(movieId, 1000, 1030.5));
    }

    [Fact]
    public async Task Clip_RunningPastTheEndOfTheFile_CoversWhatsThere()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 100, 1920, 1080);

        Assert.Equal(TrickplayGenerateResult.Generated, await Generator().GenerateClipAsync(movieId, 80, 110));

        Assert.Equal(new FfmpegTrickplayRequest(1, 320, 180, 10, 10, false, false, 80, 20), lastRequest);
    }

    [Fact]
    public async Task Clip_LongEnoughForTheMoviesSet_GetsNoneOfItsOwn()
    {
        var movieId = await AddMovieAsync("ABC-123.mp4", 7127.8, 1920, 1080);

        Assert.Equal(TrickplayGenerateResult.NotNeeded, await Generator().GenerateClipAsync(movieId, 0, 1200));
        await ffmpeg.DidNotReceiveWithAnyArgs().GenerateTrickplayAsync(default!, default!, default!, default, default, default);
    }

    [Theory]
    [InlineData(1920, 1080, false, 180)]
    [InlineData(3840, 2160, false, 180)]
    [InlineData(7680, 3840, true, 320)]
    [InlineData(720, 480, false, 214)]
    public void ThumbnailHeight_KeepsTheAspect_Even(int width, int height, bool leftEye, int expected) =>
        Assert.Equal(expected, TrickplayGenerator.ThumbnailHeight(width, height, leftEye));
}
