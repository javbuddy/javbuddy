using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.VideoRepair;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Javbuddy.Tests.Components.Shared;

public class SceneChapterWriterTests : BunitContext
{
    private readonly ISceneChapterWriteService writer = Substitute.For<ISceneChapterWriteService>();

    private static readonly ChapterWritePlan Plan = new(
        "ABC-123.mp4",
        [new PlannedChapter(0, 10, null), new PlannedChapter(10, 60, "Interview"), new PlannedChapter(60, 100, "Bath")],
        [new FfprobeChapterInfo(0, 50, "ABC-123-A"), new FfprobeChapterInfo(50, 100, "ABC-123-B")]);

    public SceneChapterWriterTests() => Services.AddSingleton(writer);

    private IRenderedComponent<SceneChapterWriter> RenderWriter(int sceneCount = 2) =>
        Render<SceneChapterWriter>(p => p.Add(x => x.MovieId, 7).Add(x => x.SceneCount, sceneCount));

    [Fact]
    public void NoScenes_DisablesTheAction()
    {
        Assert.True(RenderWriter(sceneCount: 0).Find(".scene-chapter-writer-open-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void Confirmation_SummarisesChapters_AndWarnsAboutReplacedChapters()
    {
        writer.GetPlanAsync(7, Arg.Any<CancellationToken>()).Returns(new ChapterWritePlanResult(Plan));
        var cut = RenderWriter();

        Assert.False(cut.Find(".scene-chapter-writer").ClassList.Contains("scene-chapter-writer-expanded"));
        cut.Find(".scene-chapter-writer-open-btn").Click();

        // The confirmation takes a full-width line in the editor's tools row.
        Assert.True(cut.Find(".scene-chapter-writer").ClassList.Contains("scene-chapter-writer-expanded"));
        var summary = cut.Find(".scene-chapter-writer-summary").TextContent;
        Assert.Contains("3 chapters", summary);
        Assert.Contains("2 from scenes, 1 untitled for the gaps", summary);
        Assert.Contains("ABC-123.mp4", summary);
        Assert.Contains("ABC-123-A, ABC-123-B", cut.Find(".scene-chapter-writer-warning").TextContent);

        cut.Find(".scene-chapter-writer-cancel-btn").Click();
        Assert.NotNull(cut.Find(".scene-chapter-writer-open-btn"));
        Assert.False(cut.Find(".scene-chapter-writer").ClassList.Contains("scene-chapter-writer-expanded"));
    }

    [Fact]
    public void PlanError_IsShown()
    {
        writer.GetPlanAsync(7, Arg.Any<CancellationToken>()).Returns(new ChapterWritePlanResult(null, "The movie's video file couldn't be found locally."));
        var cut = RenderWriter();

        cut.Find(".scene-chapter-writer-open-btn").Click();

        Assert.Equal("The movie's video file couldn't be found locally.", cut.Find(".scene-editor-error").TextContent);
    }

    [Fact]
    public void Writing_ShowsProgress_ThenTheOutcome()
    {
        writer.GetPlanAsync(7, Arg.Any<CancellationToken>()).Returns(new ChapterWritePlanResult(Plan));
        var done = new TaskCompletionSource<VideoRepairResult>();
        var job = new VideoRepairJob { Kind = VideoFileJobKind.Chapters, MovieId = 7, FileName = "ABC-123.mp4", Task = done.Task, LatestProgress = new FfmpegProgress(42, TimeSpan.Zero) };
        writer.StartAsync(7, Arg.Any<CancellationToken>()).Returns(job);
        var cut = RenderWriter();

        cut.Find(".scene-chapter-writer-open-btn").Click();
        cut.Find(".scene-chapter-writer-confirm-btn").Click();

        Assert.Contains("42%", cut.Find(".scene-chapter-writer-status").TextContent);
        done.SetResult(VideoRepairResult.Ok("/x/ABC-123.mp4"));
        cut.WaitForAssertion(() => Assert.Equal("Chapters written into ABC-123.mp4.", cut.Find(".scene-chapter-writer-done").TextContent), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void StartRefused_ShowsWhy()
    {
        writer.GetPlanAsync(7, Arg.Any<CancellationToken>()).Returns(new ChapterWritePlanResult(Plan));
        writer.StartAsync(7, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A video repair of ABC-123.mp4 is running for ABC-123. Wait for it to finish first."));
        var cut = RenderWriter();

        cut.Find(".scene-chapter-writer-open-btn").Click();
        cut.Find(".scene-chapter-writer-confirm-btn").Click();

        Assert.Contains("video repair", cut.Find(".scene-editor-error").TextContent);
    }

    [Fact]
    public void ARunningWrite_IsPickedUpOnRender()
    {
        var job = new VideoRepairJob { Kind = VideoFileJobKind.Chapters, MovieId = 7, FileName = "ABC-123.mp4", Task = new TaskCompletionSource<VideoRepairResult>().Task };
        writer.GetRunningJob(7).Returns(job);

        Assert.Contains("Writing chapters into ABC-123.mp4", RenderWriter().Find(".scene-chapter-writer-status").TextContent);
    }
}
