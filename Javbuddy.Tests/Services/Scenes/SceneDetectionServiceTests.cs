using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.VideoRepair;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

public sealed class SceneDetectionServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly VideoRepairJobTracker tracker = new();
    // The hard cut is ignored by the default options (camera-angle changes, see SceneDetectionOptions).
    private FfmpegSceneSignals signals = new([new FfmpegBlackStretch(600, 601), new FfmpegBlackStretch(1798, 1800)], [new FfmpegSceneCut(1200, 60)]);

    // What the mocked ffmpeg run does with the sink.
    private Func<IFfmpegSceneSignalSink, Task<FfmpegSceneSignals?>> scan;

    /// <summary>Reports every signal in file order, like the real scan, and succeeds.</summary>
    private async Task<FfmpegSceneSignals?> ReportAllAsync(IFfmpegSceneSignalSink sink)
    {
        foreach (var signal in signals.BlackStretches.Select(b => (Seconds: b.EndSeconds, Signal: (object)b))
                     .Concat(signals.Cuts.Select(c => (c.Seconds, Signal: (object)c)))
                     .OrderBy(s => s.Seconds))
        {
            await (signal.Signal is FfmpegBlackStretch black ? sink.OnBlackStretchAsync(black, default) : sink.OnCutAsync((FfmpegSceneCut)signal.Signal, default));
        }
        return signals;
    }

    public SceneDetectionServiceTests()
    {
        Directory.CreateDirectory(folder);
        var video = Path.Combine(folder, "ABC-123.mp4");
        File.WriteAllText(video, "");
        scan = ReportAllAsync;
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        ffmpeg.ProbeAsync(video, Arg.Any<CancellationToken>()).Returns(new FfprobeMediaInfo { DurationSeconds = 3600 });
        ffmpeg.DetectSceneSignalsAsync(video, Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<IFfmpegSceneSignalSink?>(), Arg.Any<CancellationToken>())
            .Returns(call => scan(call.ArgAt<IFfmpegSceneSignalSink>(3)));
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SceneDetectionService CreateService() => new(factory, localLibrary, ffmpeg, tracker, new MovieSceneService(factory));

    private async Task<int> SeedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 3600 };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private async Task<IReadOnlyList<SceneSuggestionItem>> ScanAsync(int movieId, SceneDetectionService service)
    {
        var job = await service.StartAsync(movieId);
        Assert.Equal(VideoFileJobKind.Detection, job.Kind);
        Assert.True((await job.Task!).Success);
        return await service.GetSuggestionsAsync(movieId);
    }

    [Fact]
    public async Task Scan_StoresSuggestions_WithoutCreatingScenes()
    {
        var movieId = await SeedAsync();

        var suggestions = await ScanAsync(movieId, CreateService());

        Assert.Equal([601d, 1800d], suggestions.Select(s => s.Seconds));
        Assert.All(suggestions, s => Assert.Equal(SceneSuggestionSource.Black, s.Source));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Scenes.ToListAsync());
    }

    [Fact]
    public async Task Rescan_ReplacesPendingSuggestions_ButRemembersDismissedOnes()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        var first = await ScanAsync(movieId, service);
        await service.DismissAsync(first[0].Id);

        signals = new([new FfmpegBlackStretch(600, 601.5), new FfmpegBlackStretch(2399, 2400)], []);
        var second = await ScanAsync(movieId, service);

        // 601.5 is within 5 s of the dismissed 601; the old pending 1800 is gone, 2400 is new.
        Assert.Equal([2400d], second.Select(s => s.Seconds));
    }

    [Fact]
    public async Task Scan_StoresSuggestionsWhileRunning_AndSkipsScenesAcceptedMeanwhile()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        var halfway = new TaskCompletionSource();
        var resume = new TaskCompletionSource();
        scan = async sink =>
        {
            await sink.OnBlackStretchAsync(new FfmpegBlackStretch(600, 601), default);
            await sink.OnDecodedAsync(700, default);
            halfway.SetResult();
            await resume.Task;
            await sink.OnBlackStretchAsync(new FfmpegBlackStretch(1798, 1800), default);
            await sink.OnBlackStretchAsync(new FfmpegBlackStretch(2399, 2400), default);
            return signals;
        };

        var job = await service.StartAsync(movieId);
        await halfway.Task;
        var early = Assert.Single(await service.GetSuggestionsAsync(movieId));
        Assert.Equal(601d, early.Seconds);
        Assert.False(job.Task!.IsCompleted);

        // Accepted while the scan goes on, plus a scene added by hand where a later break lands.
        Assert.True((await service.AcceptAsync(early.Id)).Success);
        await new MovieSceneService(factory).AddSceneAsync(movieId, 1801, null, null);
        resume.SetResult();
        Assert.True((await job.Task).Success);

        // 1800 is within 5 s of the hand-added scene; 2400 settled when the scan completed.
        Assert.Equal([2400d], (await service.GetSuggestionsAsync(movieId)).Select(s => s.Seconds));
    }

    [Fact]
    public async Task FailedScan_KeepsWhatItAlreadySuggested()
    {
        var movieId = await SeedAsync();
        scan = async sink =>
        {
            await sink.OnBlackStretchAsync(new FfmpegBlackStretch(600, 601), default);
            await sink.OnDecodedAsync(700, default);
            await sink.OnBlackStretchAsync(new FfmpegBlackStretch(1798, 1800), default);
            return null;
        };
        var service = CreateService();

        var job = await service.StartAsync(movieId);

        Assert.False((await job.Task!).Success);
        // 1800 was still held (a longer black could have followed within a minute), so it's lost.
        Assert.Equal([601d], (await service.GetSuggestionsAsync(movieId)).Select(s => s.Seconds));
    }

    [Fact]
    public async Task Accept_CreatesAnUntitledScene_AndRemovesTheSuggestion()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        var suggestions = await ScanAsync(movieId, service);

        var result = await service.AcceptAsync(suggestions[0].Id);

        Assert.True(result.Success);
        var scene = Assert.Single(await new MovieSceneService(factory).GetScenesAsync(movieId));
        Assert.Equal(601d, scene.StartSeconds);
        Assert.Null(scene.Title);
        Assert.Equal([1800d], (await service.GetSuggestionsAsync(movieId)).Select(s => s.Seconds));
    }

    [Fact]
    public async Task Accept_WithOpeningScene_AlsoAddsOneFromZeroUpToTheSuggestion()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        var suggestions = await ScanAsync(movieId, service);

        var result = await service.AcceptAsync(suggestions[0].Id, insertOpeningScene: true);

        Assert.True(result.Success);
        var scenes = await new MovieSceneService(factory).GetScenesAsync(movieId);
        Assert.Equal([0d, 601d], scenes.Select(s => s.StartSeconds));
        Assert.Null(scenes[0].EndSeconds);
        Assert.Equal(601d, scenes[0].EffectiveEndSeconds);
    }

    [Fact]
    public async Task Accept_InsideAnExplicitScene_FailsAndKeepsTheSuggestion()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        var suggestions = await ScanAsync(movieId, service);
        await new MovieSceneService(factory).AddSceneAsync(movieId, 500, 700, "Long take");

        var result = await service.AcceptAsync(suggestions[0].Id);

        Assert.Equal("Overlaps \"Long take\".", result.ErrorMessage);
        Assert.Equal(2, (await service.GetSuggestionsAsync(movieId)).Count);
    }

    [Fact]
    public async Task AcceptAll_AddsWhatFits_AndReportsTheRest()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        await ScanAsync(movieId, service);
        await new MovieSceneService(factory).AddSceneAsync(movieId, 500, 700, "Long take");

        var result = await service.AcceptAllAsync(movieId);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(["10:01: Overlaps \"Long take\"."], result.Errors);
    }

    [Fact]
    public async Task AcceptAll_WithOpeningScene_AlsoAddsOneFromZero()
    {
        var movieId = await SeedAsync();
        var service = CreateService();
        await ScanAsync(movieId, service);

        var result = await service.AcceptAllAsync(movieId, insertOpeningScene: true);

        Assert.Equal(2, result.Accepted);
        Assert.Empty(result.Errors);
        var scenes = await new MovieSceneService(factory).GetScenesAsync(movieId);
        Assert.Equal([0d, 601d, 1800d], scenes.Select(s => s.StartSeconds));
    }

    [Fact]
    public async Task Start_WhileAnotherJobRuns_IsRefused()
    {
        var movieId = await SeedAsync();
        var gate = new TaskCompletionSource<VideoRepairResult>();
        tracker.GetOrStart(movieId, null, "ABC-123", "ABC-123.mp4", (_, _) => gate.Task, VideoFileJobKind.Chapters);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().StartAsync(movieId));

        Assert.Contains("chapter writing", error.Message);
        gate.SetResult(VideoRepairResult.Ok(""));
    }
}
