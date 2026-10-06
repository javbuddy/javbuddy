using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.VrMerge;

public class VrMergeJobTrackerTests
{
    [Fact]
    public async Task GetOrStart_WhileRunning_ReportsOneActiveJob_ThenZeroOnceDone()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        var tracker = new VrMergeJobTracker(metrics);
        var gate = new TaskCompletionSource();

        var job = tracker.GetOrStart(1, async (_, ct) =>
        {
            await gate.Task;
            return VrMergeResult.Ok("/merged.mp4");
        });

        recorder.SampleGauges();
        var active = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_vr_merge_active_jobs");
        Assert.Equal(1, active.Value);

        gate.SetResult();
        await job.Task!;

        recorder.SampleGauges();
        var finalActive = recorder.Measurements.Last(m => m.InstrumentName == "javbuddy_vr_merge_active_jobs");
        Assert.Equal(0, finalActive.Value);
    }

    [Fact]
    public async Task GetOrStart_MergeSucceeds_RecordsSuccessOutcome()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        var tracker = new VrMergeJobTracker(metrics);

        var job = tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/merged.mp4")));
        await job.Task!;

        var recorded = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_vr_merge_jobs_total");
        Assert.Equal("success", recorded.Tags["outcome"]);
    }

    [Fact]
    public async Task GetOrStart_MergeSucceeds_TracksAndCompletesActivity()
    {
        using var metrics = new JavbuddyMetrics();
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var tracker = new VrMergeJobTracker(metrics, activities);

        var job = tracker.GetOrStart(1, "DEVR-041", (_, _) => Task.FromResult(VrMergeResult.Ok("/merged.mp4")));
        await job.Task!;

        Assert.Equal(new TaskActivity("Sorting · DEVR-041", "VR video parts merged.", false), activities.Current);
    }

    [Fact]
    public async Task GetOrStart_MergeThrows_RecordsErrorOutcomeAndRethrows()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        var tracker = new VrMergeJobTracker(metrics);

        var job = tracker.GetOrStart(1, (IProgress<FfmpegProgress> _, CancellationToken _) => Task.FromException<VrMergeResult>(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.Task!);

        var recorded = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_vr_merge_jobs_total");
        Assert.Equal("error", recorded.Tags["outcome"]);
    }

    private static VrMergeJobTracker CreateTracker() => new(new JavbuddyMetrics());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task FinishedJob_IsRetiredFromTheTracker_AndItsCancellationSourceDisposed()
    {
        var tracker = CreateTracker();
        var job = tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/merged.mp4")));
        await job.Task!;

        await WaitUntilAsync(() => tracker.Get(1) is null);

        Assert.Throws<ObjectDisposedException>(() => job.Cts.Cancel());
    }

    [Fact]
    public async Task ManyFinishedJobs_LeaveNothingRetained()
    {
        var tracker = CreateTracker();
        for (var id = 1; id <= 50; id++)
        {
            await tracker.GetOrStart(id, (_, _) => Task.FromResult(VrMergeResult.Ok("/m.mp4"))).Task!;
        }

        await WaitUntilAsync(() => Enumerable.Range(1, 50).All(id => tracker.Get(id) is null));
    }

    [Fact]
    public async Task RunningJob_IsReattachedUntilItFinishes()
    {
        var tracker = CreateTracker();
        var gate = new TaskCompletionSource();
        var job = tracker.GetOrStart(1, async (_, _) =>
        {
            await gate.Task;
            return VrMergeResult.Ok("/merged.mp4");
        });

        Assert.Same(job, tracker.Get(1));
        Assert.Same(job, tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/other.mp4"))));

        gate.SetResult();
        await job.Task!;
        await WaitUntilAsync(() => tracker.Get(1) is null);
    }

    [Fact]
    public async Task CancelAfterTheJobFinished_DoesNothingAndDoesNotThrow()
    {
        var tracker = CreateTracker();
        var job = tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/merged.mp4")));
        await job.Task!;
        await WaitUntilAsync(() => tracker.Get(1) is null);

        tracker.Cancel(1);
    }

    [Fact]
    public async Task RestartAfterFinish_StartsAFreshJob()
    {
        var tracker = CreateTracker();
        var first = tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/a.mp4")));
        await first.Task!;
        await WaitUntilAsync(() => tracker.Get(1) is null);

        var second = tracker.GetOrStart(1, (_, _) => Task.FromResult(VrMergeResult.Ok("/b.mp4")));

        Assert.NotSame(first, second);
        Assert.Equal("/b.mp4", (await second.Task!).MergedFilePath);
    }
}
