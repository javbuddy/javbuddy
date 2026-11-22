using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Services.VrMerge;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents.SortWizard;

public class MergeStepTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "javbuddy-mergestep-" + Guid.NewGuid().ToString("N"));
    private readonly JavbuddyMetrics metrics = new();
    private readonly FakeTimeProvider time = new();
    private readonly WizardSession session = new() { TorrentDownloadId = 7 };
    private readonly IPathMappingService pathMapping = Substitute.For<IPathMappingService>();
    private readonly IVrMergeService mergeService = Substitute.For<IVrMergeService>();
    private readonly IFfmpegBinaryResolver ffmpeg = Substitute.For<IFfmpegBinaryResolver>();
    private readonly VrMergeJobTracker tracker;

    public MergeStepTests()
    {
        Directory.CreateDirectory(dir);
        ffmpeg.IsAvailable.Returns(true);
        tracker = new VrMergeJobTracker(metrics);
    }

    public void Dispose()
    {
        metrics.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private MergeStep Create() => new(session, pathMapping, mergeService, tracker, ffmpeg, time);

    private static VrMergeCandidatePart Part(string path) =>
        new() { Path = path, Name = Path.GetFileName(path), SizeBytes = 1, DurationSeconds = 1, ChapterTitle = "" };

    private static VrMergeCandidate Candidate(string folder, params string[] partNames) =>
        new() { FolderPath = folder, CodeBase = "ABC-123", Parts = [.. partNames.Select(n => Part(Path.Combine(folder, n)))] };

    /// <summary>Completes when the step's state satisfies <paramref name="condition"/>, driven by the session's change
    /// notification rather than a delay; the timeout is only a failsafe against a hung test.</summary>
    private async Task WaitForAsync(Func<bool> condition)
    {
        var signal = new TaskCompletionSource();
        void Check()
        {
            if (condition()) signal.TrySetResult();
        }
        session.Changed += Check;
        try
        {
            Check();
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            session.Changed -= Check;
        }
    }

    [Fact]
    public async Task InitializeAsync_FfmpegMissing_StaysOnScan()
    {
        ffmpeg.IsAvailable.Returns(false);
        await using var step = Create();

        await step.InitializeAsync("/raw", dir, CancellationToken.None);

        Assert.False(step.FfmpegAvailable);
        Assert.Equal(WizardStep.Scan, session.Step);
        await mergeService.DidNotReceive().DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_TwoPartsInMappedFolder_ShowsMergeStep()
    {
        pathMapping.TranslateToAppPathAsync("/raw", Arg.Any<CancellationToken>()).Returns(dir);
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        await using var step = Create();

        await step.InitializeAsync("/raw", "", CancellationToken.None);

        Assert.Equal(WizardStep.Merge, session.Step);
        Assert.Equal(dir, step.MergeSourcePath);
        Assert.Equal("ABC-123", step.MergeCodeBase);
        Assert.Equal(2, step.MergeAllParts.Count);
    }

    [Fact]
    public async Task InitializeAsync_FolderWithoutParts_StaysOnScan()
    {
        pathMapping.TranslateToAppPathAsync("/raw", Arg.Any<CancellationToken>()).Returns(dir);
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns((VrMergeCandidate?)null);
        await using var step = Create();

        await step.InitializeAsync("/raw", "", CancellationToken.None);

        Assert.Equal(WizardStep.Scan, session.Step);
    }

    [Fact]
    public async Task InitializeAsync_MappedPathMissing_FallsBackToScanPath_AndRecordsBothAttempts()
    {
        var missing = Path.Combine(dir, "nope");
        pathMapping.TranslateToAppPathAsync("/raw", Arg.Any<CancellationToken>()).Returns(missing);
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        await using var step = Create();

        await step.InitializeAsync("/raw", dir, CancellationToken.None);

        Assert.Equal(dir, step.MergeSourcePath);
        Assert.Equal([missing, dir], step.MergeSourcePathAttempts);
        Assert.Equal(WizardStep.Merge, session.Step);
    }

    [Fact]
    public async Task InitializeAsync_NoFolderResolves_ShowsMergeStepForManualPath()
    {
        pathMapping.TranslateToAppPathAsync("/raw", Arg.Any<CancellationToken>()).Returns(Path.Combine(dir, "nope"));
        await using var step = Create();

        await step.InitializeAsync("/raw", "", CancellationToken.None);

        Assert.Equal(WizardStep.Merge, session.Step);
        Assert.Equal("", step.MergeSourcePath);
        await mergeService.DidNotReceive().DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectMergeCandidateAsync_BlankPath_SetsError()
    {
        await using var step = Create();

        await step.DetectMergeCandidateAsync();

        Assert.Equal("Enter a folder path to scan for parts.", step.MergeError);
        Assert.False(step.DetectingMergeCandidate);
    }

    [Fact]
    public async Task DetectMergeCandidateAsync_NothingDetected_SetsErrorNamingThePath()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns((VrMergeCandidate?)null);
        await using var step = Create();
        step.MergeSourcePath = dir;

        await step.DetectMergeCandidateAsync();

        Assert.Equal($"No multi-part VR release detected at \"{dir}\".", step.MergeError);
        Assert.Null(step.MergeCandidate);
        Assert.Empty(step.MergeAllParts);
    }

    [Fact]
    public async Task ToggleAndMove_AdjustTheIncludedPartsAndTheirChapterTitles()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4", "c.mp4"));
        await using var step = Create();
        step.MergeSourcePath = dir;
        await step.DetectMergeCandidateAsync();
        var a = Path.Combine(dir, "a.mp4");
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");

        step.ToggleMergePart(b);
        Assert.Equal([a, c], step.MergeIncludedPartsInOrder.Select(p => p.Path));
        Assert.Equal("ABC-123-B", step.ChapterTitleFor(c));
        Assert.Null(step.ChapterTitleFor(b));

        step.ToggleMergePart(b);
        step.MoveMergePart(0, 2);
        Assert.Equal([b, c, a], step.MergeAllParts.Select(p => p.Path));
        step.MoveMergePart(0, 9); // out of range: ignored
        Assert.Equal([b, c, a], step.MergeAllParts.Select(p => p.Path));
    }

    [Fact]
    public async Task StartMergeAsync_UnderTwoParts_SetsErrorAndDoesNotStart()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        await using var step = Create();
        step.MergeSourcePath = dir;
        await step.DetectMergeCandidateAsync();
        step.ToggleMergePart(Path.Combine(dir, "a.mp4"));

        await step.StartMergeAsync();

        Assert.Equal("At least two parts must be selected to merge.", step.MergeError);
        Assert.False(step.Merging);
        Assert.Null(tracker.Get(7));
    }

    [Fact]
    public async Task StartMergeAsync_Success_SendsOrderedTitledPartsAndMovesToScan()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        var release = new TaskCompletionSource<VrMergeResult>();
        VrMergeRequest? sent = null;
        mergeService.MergeAsync(Arg.Do<VrMergeRequest>(r => sent = r), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(release.Task);
        await using var step = Create();
        step.MergeSourcePath = dir;
        await step.DetectMergeCandidateAsync();
        session.Step = WizardStep.Merge;

        await step.StartMergeAsync();
        Assert.True(step.Merging);

        release.SetResult(VrMergeResult.Ok("/merged.mp4"));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => step.MergedFilePath is not null);

        Assert.Equal("/merged.mp4", step.MergedFilePath);
        Assert.Equal(WizardStep.Scan, session.Step);
        Assert.Null(step.MergeError);
        Assert.Equal(dir, sent!.FolderPath);
        Assert.Equal(["ABC-123-A", "ABC-123-B"], sent.OrderedParts.Select(p => p.ChapterTitle));
    }

    [Fact]
    public async Task StartMergeAsync_Failure_ShowsTheMergeError_AndStaysOnMerge()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        var release = new TaskCompletionSource<VrMergeResult>();
        mergeService.MergeAsync(Arg.Any<VrMergeRequest>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(release.Task);
        await using var step = Create();
        step.MergeSourcePath = dir;
        await step.DetectMergeCandidateAsync();
        session.Step = WizardStep.Merge;

        await step.StartMergeAsync();
        release.SetResult(VrMergeResult.Failed("disk full"));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => step.MergeError is not null);

        Assert.Equal("disk full", step.MergeError);
        Assert.Null(step.MergedFilePath);
        Assert.Equal(WizardStep.Merge, session.Step);
    }

    [Fact]
    public async Task CancelMerge_CancelsTheTrackedJob_AndReportsCancelled()
    {
        mergeService.DetectAsync(dir, Arg.Any<CancellationToken>()).Returns(Candidate(dir, "a.mp4", "b.mp4"));
        mergeService.MergeAsync(Arg.Any<VrMergeRequest>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.Delay(Timeout.Infinite, ci.ArgAt<CancellationToken>(2)).ContinueWith(_ => VrMergeResult.Ok("x"), TaskContinuationOptions.OnlyOnRanToCompletion));
        await using var step = Create();
        step.MergeSourcePath = dir;
        await step.DetectMergeCandidateAsync();

        await step.StartMergeAsync();
        step.CancelMerge();
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => step.MergeError is not null);

        Assert.Equal("Merge cancelled.", step.MergeError);
    }

    [Fact]
    public async Task InitializeAsync_MergeAlreadyRunning_ReattachesInsteadOfStartingASecond()
    {
        var release = new TaskCompletionSource<VrMergeResult>();
        var calls = 0;
        tracker.GetOrStart(7, (_, _) => { calls++; return release.Task; });
        await using var step = Create();

        await step.InitializeAsync("/raw", dir, CancellationToken.None);

        Assert.Equal(WizardStep.Merge, session.Step);
        Assert.True(step.Merging);
        Assert.Equal(1, calls);
        await mergeService.DidNotReceive().DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        release.SetResult(VrMergeResult.Ok("/merged.mp4"));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => step.MergedFilePath is not null);
        Assert.Equal("/merged.mp4", step.MergedFilePath);
    }

    [Fact]
    public async Task SkipMerge_GoesToScan()
    {
        session.Step = WizardStep.Merge;
        await using var step = Create();

        step.SkipMerge();

        Assert.Equal(WizardStep.Scan, session.Step);
    }
}
