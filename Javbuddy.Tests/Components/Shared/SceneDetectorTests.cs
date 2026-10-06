using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.VideoRepair;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class SceneDetectorTests : BunitContext
{
    private readonly ISceneDetectionService detection = Substitute.For<ISceneDetectionService>();
    private readonly List<IReadOnlyList<SceneSuggestionItem>> reported = [];
    private readonly List<double> seeks = [];
    private int scenesAdded;

    private static readonly IReadOnlyList<SceneSuggestionItem> Two =
    [
        new(1, 601, SceneSuggestionSource.Black, 1),
        new(2, 1800, SceneSuggestionSource.Cut, 60),
    ];

    public SceneDetectorTests()
    {
        Services.AddSingleton(detection);
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Array.Empty<SceneSuggestionItem>());
    }

    private IRenderedComponent<SceneDetector> RenderDetector(double? firstSceneStart = null) =>
        Render<SceneDetector>(p => p
            .Add(x => x.MovieId, 7)
            .Add(x => x.FirstSceneStart, firstSceneStart)
            .Add(x => x.OnSuggestionsChanged, (IReadOnlyList<SceneSuggestionItem> s) => reported.Add(s))
            .Add(x => x.OnSeek, (double s) => seeks.Add(s))
            .Add(x => x.OnScenesAdded, () => scenesAdded++));

    [Fact]
    public void Scan_ShowsProgress_ThenTheSuggestions()
    {
        var done = new TaskCompletionSource<VideoRepairResult>();
        detection.StartAsync(7, Arg.Any<CancellationToken>()).Returns(new VideoRepairJob
        {
            Kind = VideoFileJobKind.Detection,
            MovieId = 7,
            FileName = "ABC-123.mp4",
            Task = done.Task,
            LatestProgress = new FfmpegProgress(37, TimeSpan.Zero),
        });
        // Configured before rendering, not once the scan runs: its poll calls the substitute from
        // another thread, and NSubstitute's .Returns binds to whichever call came last.
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(_ => done.Task.IsCompleted ? Two : Array.Empty<SceneSuggestionItem>());
        var cut = RenderDetector();

        Assert.False(cut.Find(".scene-detector").ClassList.Contains("scene-detector-expanded"));
        cut.Find(".scene-detector-start-btn").Click();
        Assert.Contains("37%", cut.Find(".scene-detector-status").TextContent);
        // Progress and suggestions take a full-width line in the editor's tools row.
        Assert.True(cut.Find(".scene-detector").ClassList.Contains("scene-detector-expanded"));

        done.SetResult(VideoRepairResult.Ok("x"));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".scene-detector-item").Count), TimeSpan.FromSeconds(5));
        Assert.Contains("fade to black", cut.FindAll(".scene-detector-item")[0].TextContent);
        Assert.Contains("hard cut", cut.FindAll(".scene-detector-item")[1].TextContent);
        Assert.Equal(2, reported[^1].Count);
    }

    [Fact]
    public void NothingFound_SaysSo()
    {
        detection.StartAsync(7, Arg.Any<CancellationToken>()).Returns(new VideoRepairJob
        {
            Kind = VideoFileJobKind.Detection,
            MovieId = 7,
            FileName = "ABC-123.mp4",
            Task = Task.FromResult(VideoRepairResult.Ok("x")),
        });
        var cut = RenderDetector();

        cut.Find(".scene-detector-start-btn").Click();

        cut.WaitForAssertion(() => Assert.Equal("No new scene boundaries found.", cut.Find(".scene-detector-note").TextContent), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AcceptDismissAndSeek_CallTheService()
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        detection.AcceptAsync(1, false, Arg.Any<CancellationToken>()).Returns(SceneOperationResult.Ok(9));
        var cut = RenderDetector();

        cut.FindAll(".scene-detector-time")[1].Click();
        Assert.Equal([1800d], seeks);

        cut.FindAll(".scene-detector-dismiss-btn")[1].Click();
        detection.Received(1).DismissAsync(2, Arg.Any<CancellationToken>());

        // A scene already starts before the fade to black, so accepting it doesn't ask.
        cut = RenderDetector(firstSceneStart: 0);
        cut.FindAll(".scene-detector-accept-btn")[0].Click();
        Assert.Empty(cut.FindAll(".scene-detector-opening-prompt"));
        detection.Received(1).AcceptAsync(1, false, Arg.Any<CancellationToken>());
        Assert.Equal(1, scenesAdded);
    }

    [Fact]
    public void AcceptingAHardCut_DoesNotAskAboutAnOpeningScene()
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        detection.AcceptAsync(2, false, Arg.Any<CancellationToken>()).Returns(SceneOperationResult.Ok(9));
        var cut = RenderDetector();

        cut.FindAll(".scene-detector-accept-btn")[1].Click();

        Assert.Empty(cut.FindAll(".scene-detector-opening-prompt"));
        detection.Received(1).AcceptAsync(2, false, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(".scene-detector-opening-insert-btn", true)]
    [InlineData(".scene-detector-opening-skip-btn", false)]
    public void AcceptingTheFirstFadeToBlack_AsksAboutAnOpeningScene(string button, bool insert)
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        detection.AcceptAsync(1, insert, Arg.Any<CancellationToken>()).Returns(SceneOperationResult.Ok(9));
        var cut = RenderDetector(firstSceneStart: 1800);

        cut.FindAll(".scene-detector-accept-btn")[0].Click();

        Assert.Contains("0:00 to 10:01", cut.Find(".scene-detector-opening-prompt").TextContent);
        detection.DidNotReceiveWithAnyArgs().AcceptAsync(default, default, default);

        cut.Find(button).Click();

        detection.Received(1).AcceptAsync(1, insert, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".scene-detector-opening-prompt"));
        Assert.Equal(1, scenesAdded);
    }

    [Fact]
    public void OpeningScenePrompt_CancelAcceptsNothing()
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        var cut = RenderDetector();

        cut.Find(".scene-detector-accept-all-btn").Click();
        cut.Find(".scene-detector-opening-cancel-btn").Click();

        Assert.Empty(cut.FindAll(".scene-detector-opening-prompt"));
        detection.DidNotReceiveWithAnyArgs().AcceptAllAsync(default, default, default);
        Assert.Equal(0, scenesAdded);
    }

    [Fact]
    public void AcceptAll_AsksAboutAnOpeningScene_WhenTheEarliestIsAFadeToBlack()
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        detection.AcceptAllAsync(7, true, Arg.Any<CancellationToken>()).Returns(new SceneAcceptResult(2, []));
        var cut = RenderDetector();

        cut.Find(".scene-detector-accept-all-btn").Click();
        cut.Find(".scene-detector-opening-insert-btn").Click();

        detection.Received(1).AcceptAllAsync(7, true, Arg.Any<CancellationToken>());
        Assert.Equal(1, scenesAdded);
    }

    [Fact]
    public void AcceptAll_ReportsSkippedOnes()
    {
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(Two);
        detection.AcceptAllAsync(7, false, Arg.Any<CancellationToken>()).Returns(new SceneAcceptResult(1, ["10:01: Overlaps \"Long take\"."]));
        var cut = RenderDetector(firstSceneStart: 0);

        cut.Find(".scene-detector-accept-all-btn").Click();

        Assert.Equal("Added 1; skipped 10:01: Overlaps \"Long take\".", cut.Find(".scene-editor-error").TextContent);
        Assert.Equal(1, scenesAdded);
    }

    [Fact]
    public void ARunningScan_IsPickedUpOnRender()
    {
        detection.GetRunningJob(7).Returns(new VideoRepairJob
        {
            Kind = VideoFileJobKind.Detection,
            MovieId = 7,
            FileName = "ABC-123.mp4",
            Task = new TaskCompletionSource<VideoRepairResult>().Task,
        });

        Assert.Contains("Scanning ABC-123.mp4", RenderDetector().Find(".scene-detector-status").TextContent);
    }

    [Fact]
    public async Task ARunningScan_ListsWhatItFoundSoFar_AndAcceptsAllOfThose()
    {
        var done = new TaskCompletionSource<VideoRepairResult>();
        detection.GetRunningJob(7).Returns(new VideoRepairJob
        {
            Kind = VideoFileJobKind.Detection,
            MovieId = 7,
            FileName = "ABC-123.mp4",
            Task = done.Task,
            LatestProgress = new FfmpegProgress(40, TimeSpan.Zero),
        });
        // Everything is configured before rendering and then driven through `stored`: the scan's poll
        // calls the substitute from another thread, and configuring (or Received-checking) it while
        // that runs can bind to the poll's call instead.
        IReadOnlyList<SceneSuggestionItem> stored = [];
        var acceptAllCalls = 0;
        detection.GetSuggestionsAsync(7, Arg.Any<CancellationToken>()).Returns(_ => Volatile.Read(ref stored));
        detection.AcceptAllAsync(7, false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref acceptAllCalls);
            Volatile.Write(ref stored, []);
            return new SceneAcceptResult(2, []);
        });
        var cut = RenderDetector(firstSceneStart: 0);
        Assert.Empty(cut.FindAll(".scene-detector-item"));

        // The scan stores suggestions as it goes; the list and the timeline follow.
        Volatile.Write(ref stored, Two);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".scene-detector-item").Count), TimeSpan.FromSeconds(5));
        Assert.Contains("40%", cut.Find(".scene-detector-status").TextContent);
        Assert.Equal("2 suggested boundaries so far", cut.Find(".scene-detector-heading").TextContent);
        Assert.Equal(2, reported[^1].Count);
        Assert.Empty(cut.FindAll(".scene-detector-start-btn"));

        var acceptAll = cut.Find(".scene-detector-accept-all-btn");
        Assert.Equal("Accept all so far", acceptAll.TextContent);
        // Awaited: a plain Click() only queues the event when a poll tick holds the renderer's
        // dispatcher, so the asserts below could run before the handler.
        await acceptAll.ClickAsync(new());
        Assert.Equal(1, Volatile.Read(ref acceptAllCalls));
        Assert.Empty(cut.FindAll(".scene-detector-item"));
        Assert.Contains("Scanning", cut.Find(".scene-detector-status").TextContent);

        // It found something, so finishing with an empty list doesn't claim nothing was found.
        done.SetResult(VideoRepairResult.Ok("x"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".scene-detector-status")), TimeSpan.FromSeconds(5));
        Assert.Empty(cut.FindAll(".scene-detector-note"));
        Assert.NotNull(cut.Find(".scene-detector-start-btn"));
    }
}
