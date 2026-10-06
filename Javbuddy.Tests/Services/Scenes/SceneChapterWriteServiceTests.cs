using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.VideoRepair;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>Writing scenes into the file as chapters, end to end through the real
/// job tracker and file replacement, with ffmpeg faked.</summary>
public sealed class SceneChapterWriteServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly IMediaServerClient mediaServer = Substitute.For<IMediaServerClient>();
    private readonly VideoRepairJobTracker tracker = new();
    private readonly string videoPath;
    private string? writtenMetadata;
    private int stagingChapterCount = -1;

    public SceneChapterWriteServiceTests()
    {
        Directory.CreateDirectory(folder);
        videoPath = Path.Combine(folder, "ABC-123.mp4");
        File.WriteAllText(videoPath, "original");
        localLibrary.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        localLibrary.RefreshMediaInfoOnlyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new LocalRefreshResult(true, null));
        mediaServer.RefreshItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new MediaServerActionResult(true, null));

        // The original has the VR merge's part chapters; the staging copy reports whatever the
        // remux "wrote" (by default: exactly the chapters in the metadata).
        ffmpeg.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var path = call.ArgAt<string>(0);
            return path.Contains(MovieVersionParser.RepairStagingMarker)
                ? new FfprobeMediaInfo { DurationSeconds = 100, ChapterCount = stagingChapterCount >= 0 ? stagingChapterCount : CountChapters(writtenMetadata) }
                : new FfprobeMediaInfo
                {
                    DurationSeconds = 100,
                    ChapterCount = 2,
                    Chapters = [new FfprobeChapterInfo(0, 50, "ABC-123-A"), new FfprobeChapterInfo(50, 100, "ABC-123-B")],
                };
        });
        ffmpeg.RemuxWithChaptersAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writtenMetadata = call.ArgAt<string>(1);
                File.WriteAllText(call.ArgAt<string>(2), "rewritten");
                return FfmpegRunResult.Ok();
            });
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

    private static int CountChapters(string? metadata) =>
        metadata is null ? 0 : metadata.Split("[CHAPTER]").Length - 1;

    private SceneChapterWriteService CreateService() => new(factory, localLibrary, ffmpeg, tracker, mediaServer);

    private Task<int> SeedAsync(params (double Start, double? End, string? Title)[] scenes) => SeedAsync("ABC-123", scenes);

    private async Task<int> SeedAsync(string code, params (double Start, double? End, string? Title)[] scenes)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, MediaDurationSeconds = 100, JellyfinItemId = "jf-1" };
        foreach (var (start, end, title) in scenes)
        {
            movie.Scenes.Add(new Scene { StartSeconds = start, EndSeconds = end, Title = title });
        }
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task Plan_ListsChaptersAndGaps_AndFlagsTheVrPartChaptersItReplaces()
    {
        var movieId = await SeedAsync((10, 60, "Interview"), (60, null, null));

        var plan = (await CreateService().GetPlanAsync(movieId)).Plan!;

        Assert.Equal("ABC-123.mp4", plan.FileName);
        Assert.Equal([new PlannedChapter(0, 10, null), new PlannedChapter(10, 60, "Interview"), new PlannedChapter(60, 100, "Scene 2")], plan.Chapters);
        Assert.Equal(1, plan.GapCount);
        Assert.True(plan.ReplacesDifferentChapters);
    }

    [Fact]
    public async Task Plan_MatchingChapters_AreNotFlaggedAsReplaced()
    {
        var movieId = await SeedAsync((0, null, "ABC-123-A"), (50, null, "ABC-123-B"));

        Assert.False((await CreateService().GetPlanAsync(movieId)).Plan!.ReplacesDifferentChapters);
    }

    [Fact]
    public async Task Plan_NoScenesOrNoFile_ExplainsWhy()
    {
        var noScenes = await SeedAsync();
        Assert.Equal("This movie has no scenes to write.", (await CreateService().GetPlanAsync(noScenes)).ErrorMessage);

        var withScenes = await SeedAsync("XYZ-999", (0, null, null)); // no local folder for this code
        Assert.Equal("The movie's video file couldn't be found locally.", (await CreateService().GetPlanAsync(withScenes)).ErrorMessage);
    }

    [Fact]
    public async Task Write_ReplacesTheFile_ThenRefreshesMediaInfoAndJellyfin()
    {
        var movieId = await SeedAsync((10, 60, "Interview"), (60, null, "Bath"));

        var job = await CreateService().StartAsync(movieId);
        var result = await job.Task!;

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(VideoFileJobKind.Chapters, job.Kind);
        Assert.Equal("rewritten", await File.ReadAllTextAsync(videoPath));
        Assert.Equal(["ABC-123.mp4"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Contains("title= \n", writtenMetadata); // MP4: the gap gets a blank title
        Assert.Contains("title=Interview\n", writtenMetadata);
        await localLibrary.Received(1).RefreshMediaInfoOnlyAsync(movieId, Arg.Any<CancellationToken>());
        await mediaServer.Received(1).RefreshItemAsync("jf-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Write_WrongChapterCountInTheNewFile_KeepsTheOriginal()
    {
        var movieId = await SeedAsync((0, null, "A"));
        stagingChapterCount = 0;

        var result = await (await CreateService().StartAsync(movieId)).Task!;

        Assert.False(result.Success);
        Assert.Contains("the original was kept", result.ErrorMessage);
        Assert.Equal("original", await File.ReadAllTextAsync(videoPath));
        Assert.Equal(["ABC-123.mp4"], Directory.GetFiles(folder).Select(Path.GetFileName));
        await mediaServer.DidNotReceive().RefreshItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_WhileARepairRuns_IsRefused()
    {
        var movieId = await SeedAsync((0, null, "A"));
        var gate = new TaskCompletionSource<VideoRepairResult>();
        tracker.GetOrStart(movieId, null, "ABC-123", "ABC-123.mp4", (_, _) => gate.Task);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().StartAsync(movieId));

        Assert.Contains("video repair", error.Message);
        gate.SetResult(VideoRepairResult.Ok(videoPath));
    }

    [Fact]
    public async Task RunningJob_IsReportedOnlyForChapterJobs()
    {
        var movieId = await SeedAsync((0, null, "A"));
        var gate = new TaskCompletionSource<VideoRepairResult>();
        tracker.GetOrStart(movieId, null, "ABC-123", "ABC-123.mp4", (_, _) => gate.Task);

        Assert.Null(CreateService().GetRunningJob(movieId));
        gate.SetResult(VideoRepairResult.Ok(videoPath));
    }
}
