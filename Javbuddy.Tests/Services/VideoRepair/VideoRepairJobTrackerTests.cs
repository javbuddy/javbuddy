using Javbuddy.Services.Monitoring;
using Javbuddy.Services.VideoRepair;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.VideoRepair;

public class VideoRepairJobTrackerTests
{
    [Fact]
    public async Task GetOrStart_WhileRunning_ReturnsSameJobInstance()
    {
        var tracker = new VideoRepairJobTracker();
        var gate = new TaskCompletionSource();

        var job1 = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, ct) =>
        {
            await gate.Task;
            return VideoRepairResult.Ok("/path/TEST-101.mp4");
        });

        var job2 = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/other.mp4")));

        Assert.Same(job1, job2);
        Assert.True(tracker.IsRunning(1));

        gate.SetResult();
        await job1.Task!;

        Assert.False(tracker.IsRunning(1));
    }

    [Fact]
    public async Task GetOrStart_DifferentMovies_RunOneRepairAtATime()
    {
        var tracker = new VideoRepairJobTracker();
        var firstGate = new TaskCompletionSource();
        var secondStarted = false;

        var first = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await firstGate.Task;
            return VideoRepairResult.Ok("/a.mp4");
        });
        var second = tracker.GetOrStart(2, null, "TEST-102", "TEST-102.mp4", (_, _) =>
        {
            secondStarted = true;
            return Task.FromResult(VideoRepairResult.Ok("/b.mp4"));
        });

        await Task.Delay(50);
        Assert.False(secondStarted);

        firstGate.SetResult();
        await first.Task!;
        await second.Task!;
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task Cancel_WhileQueuedBehindAnotherRepair_NeverRunsIt()
    {
        var tracker = new VideoRepairJobTracker();
        var firstGate = new TaskCompletionSource();
        var secondRan = false;

        var first = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await firstGate.Task;
            return VideoRepairResult.Ok("/a.mp4");
        });
        var second = tracker.GetOrStart(2, null, "TEST-102", "TEST-102.mp4", (_, _) =>
        {
            secondRan = true;
            return Task.FromResult(VideoRepairResult.Ok("/b.mp4"));
        });

        tracker.Cancel(2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Task!);
        firstGate.SetResult();
        await first.Task!;

        Assert.False(secondRan);
    }

    [Fact]
    public async Task GetOrStart_RepairSucceeds_UpdatesActivityAndReturnsOk()
    {
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var tracker = new VideoRepairJobTracker(activities);

        var job = tracker.GetOrStart(1, null, "HMN-168", "HMN-168.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/path/HMN-168.mp4")));

        var result = await job.Task!;

        Assert.True(result.Success);
        Assert.Equal(new TaskActivity("Repairing · HMN-168", "Repaired HMN-168.mp4 (HMN-168).", false), activities.Current);
    }

    [Fact]
    public async Task GetOrStart_RepairFails_MarksActivityFailed()
    {
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var tracker = new VideoRepairJobTracker(activities);

        var job = tracker.GetOrStart(1, null, "HMN-168", "HMN-168.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Failed("FFmpeg crashed")));

        var result = await job.Task!;

        Assert.False(result.Success);
        Assert.Equal(new TaskActivity("Repairing · HMN-168", "FFmpeg crashed", false, true), activities.Current);
    }

    [Fact]
    public async Task Cancel_CancelsRunningJob()
    {
        var tracker = new VideoRepairJobTracker();
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();

        var job = tracker.GetOrStart(1, null, "HMN-168", "HMN-168.mp4", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
            return VideoRepairResult.Ok("/done.mp4");
        });

        await started.Task;
        tracker.Cancel(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Task!);
        await cancelled.Task;
    }

    [Fact]
    public async Task GetActiveJobs_IncludesQueuedJobsUntilTheyFinish()
    {
        var tracker = new VideoRepairJobTracker();
        var gate = new TaskCompletionSource();

        var runningJob = tracker.GetOrStart(1, null, "RUN-01", "RUN-01.mp4", async (_, _) =>
        {
            await gate.Task;
            return VideoRepairResult.Ok("/run.mp4");
        });
        var queuedJob = tracker.GetOrStart(2, null, "QUEUE-02", "QUEUE-02.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/queued.mp4")));

        Assert.Equal([1, 2], tracker.GetActiveJobs().Select(j => j.MovieId).Order());

        gate.SetResult();
        await runningJob.Task!;
        await queuedJob.Task!;
        Assert.Empty(tracker.GetActiveJobs());
    }

    [Fact]
    public async Task GetOrStart_MultipleRepairsQueued_ActivityTracksCurrentlyRunningMovie()
    {
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var tracker = new VideoRepairJobTracker(activities);
        var firstGate = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        var secondGate = new TaskCompletionSource();

        var first = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await firstGate.Task;
            return VideoRepairResult.Ok("/path/TEST-101.mp4");
        });

        var second = tracker.GetOrStart(2, null, "TEST-102", "TEST-102.mp4", async (_, _) =>
        {
            secondStarted.SetResult();
            await secondGate.Task;
            return VideoRepairResult.Ok("/path/TEST-102.mp4");
        });

        // First job is running; second job is queued behind it.
        // Activity tracker must show the currently running movie, not the queued one.
        Assert.Equal("Repairing · TEST-101", activities.Current?.Name);
        Assert.True(activities.Current?.IsRunning);

        // Complete the first job; second job unblocks and begins running.
        firstGate.SetResult();
        await first.Task!;

        // Wait until second job starts running.
        await secondStarted.Task;

        // Activity tracker must now update to the second movie which is currently running.
        Assert.Equal("Repairing · TEST-102", activities.Current?.Name);
        Assert.True(activities.Current?.IsRunning);

        // Complete the second job.
        secondGate.SetResult();
        await second.Task!;

        Assert.Equal("Repairing · TEST-102", activities.Current?.Name);
        Assert.False(activities.Current?.IsRunning);
    }

    [Fact]
    public async Task Cancel_WhileQueuedBehindAnotherRepair_DoesNotPolluteActivityTracker()
    {
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var tracker = new VideoRepairJobTracker(activities);
        var firstGate = new TaskCompletionSource();

        var first = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await firstGate.Task;
            return VideoRepairResult.Ok("/path/TEST-101.mp4");
        });

        var second = tracker.GetOrStart(2, null, "TEST-102", "TEST-102.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/path/TEST-102.mp4")));

        // Cancel the queued second job before it starts.
        tracker.Cancel(2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Task!);

        // The first job is still running and remains the current activity.
        Assert.Equal("Repairing · TEST-101", activities.Current?.Name);
        Assert.True(activities.Current?.IsRunning);

        firstGate.SetResult();
        await first.Task!;

        Assert.Equal("Repairing · TEST-101", activities.Current?.Name);
        Assert.False(activities.Current?.IsRunning);
    }
    [Fact]
    public async Task FinishedJobs_AreEvictedAndReleaseTheirCancellationSources()
    {
        var tracker = new VideoRepairJobTracker();
        var jobs = new List<VideoRepairJob>();

        for (var id = 1; id <= 50; id++)
        {
            var code = $"TEST-{id:000}";
            jobs.Add(tracker.GetOrStart(id, null, code, $"{code}.mp4", (_, _) =>
                Task.FromResult(VideoRepairResult.Ok($"/{code}.mp4"))));
        }

        foreach (var job in jobs)
        {
            await job.Task!;
            await WaitForRetirementAsync(tracker, job);
        }

        Assert.All(jobs, j => Assert.Null(tracker.Get(j.MovieId)));
        Assert.Empty(tracker.GetActiveJobs());
    }

    [Fact]
    public async Task FailedCancelledAndFaultedJobs_AreAllEvicted()
    {
        var tracker = new VideoRepairJobTracker();
        var gate = new TaskCompletionSource();

        var failed = tracker.GetOrStart(1, null, "FAIL-001", "FAIL-001.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Failed("FFmpeg crashed")));
        var faulted = tracker.GetOrStart(2, null, "BOOM-002", "BOOM-002.mp4", (_, _) =>
            Task.FromException<VideoRepairResult>(new IOException("disk gone")));
        var cancelled = tracker.GetOrStart(3, null, "STOP-003", "STOP-003.mp4", async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return VideoRepairResult.Ok("/never.mp4");
        });

        Assert.False((await failed.Task!).Success);
        await Assert.ThrowsAsync<IOException>(() => faulted.Task!);
        tracker.Cancel(3);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Task!);

        foreach (var job in new[] { failed, faulted, cancelled })
        {
            await WaitForRetirementAsync(tracker, job);
        }
    }

    [Fact]
    public async Task RunningJob_StaysReattachable_AndHolderReadsResultAfterEviction()
    {
        var tracker = new VideoRepairJobTracker();
        var gate = new TaskCompletionSource();

        var started = tracker.GetOrStart(1, 11, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await gate.Task;
            return VideoRepairResult.Ok("/path/TEST-101.mp4");
        });

        // A modal opened later (another tab, or after navigation) finds the same running job.
        var reattached = tracker.Get(1);
        Assert.Same(started, reattached);
        Assert.True(tracker.IsRunning(1));

        gate.SetResult();
        await WaitForRetirementAsync(tracker, started);

        var result = await reattached!.Task!;
        Assert.True(result.Success);
        Assert.Equal("/path/TEST-101.mp4", result.OutputPath);
    }

    [Fact]
    public async Task RestartAfterCompletion_RetiringTheOldJobKeepsTheNewOne()
    {
        var tracker = new VideoRepairJobTracker();
        var secondGate = new TaskCompletionSource();

        var first = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/first.mp4")));
        await first.Task!;

        // May run before or after the first job's retirement continuation; either way the newer
        // entry must survive it.
        var second = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", async (_, _) =>
        {
            await secondGate.Task;
            return VideoRepairResult.Ok("/second.mp4");
        });
        Assert.NotSame(first, second);

        await WaitForCtsDisposedAsync(first);
        Assert.Same(second, tracker.Get(1));
        Assert.True(tracker.IsRunning(1));
        _ = second.Cts.Token;

        secondGate.SetResult();
        await second.Task!;
        await WaitForRetirementAsync(tracker, second);
    }

    [Fact]
    public async Task Cancel_AfterJobRetired_IsANoOp()
    {
        var tracker = new VideoRepairJobTracker();

        var job = tracker.GetOrStart(1, null, "TEST-101", "TEST-101.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/done.mp4")));
        await job.Task!;
        await WaitForRetirementAsync(tracker, job);

        tracker.Cancel(1);
    }

    [Theory]
    [InlineData(VideoFileJobKind.Repair, "video repair")]
    [InlineData(VideoFileJobKind.Chapters, "chapter writing")]
    [InlineData(VideoFileJobKind.Detection, "scene detection")]
    public async Task GetOrStart_LogsTheJobsOwnKind(VideoFileJobKind kind, string jobName)
    {
        var logger = Substitute.For<ILogger<VideoRepairJobTracker>>();
        var tracker = new VideoRepairJobTracker(logger: logger);

        var job = tracker.GetOrStart(1, null, "FGAN-188", "FGAN-188.mp4", (_, _) =>
            Task.FromResult(VideoRepairResult.Ok("/path/FGAN-188.mp4")), kind);
        await job.Task!;

        AssertLogged(logger, $"Starting {jobName} for movie FGAN-188 (File: FGAN-188.mp4)");
        AssertLogged(logger, $"Completed {jobName} for movie FGAN-188");
    }

    private static void AssertLogged(ILogger logger, string message) =>
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString() == message),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

    private static async Task WaitForRetirementAsync(VideoRepairJobTracker tracker, VideoRepairJob job)
    {
        await WaitForCtsDisposedAsync(job);
        Assert.NotSame(job, tracker.Get(job.MovieId));
    }

    // Retirement runs as a continuation after the job's Task completes, so it can trail the await.
    private static async Task WaitForCtsDisposedAsync(VideoRepairJob job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                _ = job.Cts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Job for movie #{job.MovieId} was never retired.");
            await Task.Delay(10);
        }
    }
}
