using System.Diagnostics;
using Javbuddy.Models;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class ScheduledTaskRunnerTests
{
    private sealed class FakeTask(string name, Func<CancellationToken, IProgress<TaskProgress>, Task<string?>> run, bool recordRunHistory = true) : IScheduledTask
    {
        private readonly Func<CancellationToken, IProgress<TaskProgress>, Task<string?>> run = run;

        public string Name { get; } = name;
        public string Description => "";
        public bool RecordRunHistory { get; } = recordRunHistory;
        public TimeSpan GetInterval() => TimeSpan.FromMinutes(5);
        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress) => run(ct, progress);
    }

    private static ScheduledTaskRunner CreateRunner(TestDbContextFactory factory, JavbuddyMetrics? metrics = null, ScheduledTaskChangeNotifier? notifier = null, ScheduledTaskCancellationRegistry? registry = null, ScheduledTaskRunTracker? tracker = null) =>
        new(factory, metrics ?? new JavbuddyMetrics(), notifier ?? new ScheduledTaskChangeNotifier(), registry ?? new ScheduledTaskCancellationRegistry(), tracker ?? new ScheduledTaskRunTracker(), Substitute.For<ILogger<ScheduledTaskRunner>>());

    // A task run starts its own trace, so it's picked out by its (test-unique) name rather than by TraceCapture's parent.
    private static async Task<Activity> RunTracedAsync(ScheduledTaskRunner runner, IScheduledTask task)
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == JavbuddyTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (a.DisplayName == $"task {task.Name}") lock (stopped) stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);
        await runner.RunAsync(task, CancellationToken.None);
        lock (stopped) return Assert.Single(stopped);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_RecordsOneRootSpanPerRun_WithItsOutcome(bool recordRunHistory)
    {
        using var factory = new TestDbContextFactory();
        var ok = new FakeTask($"trace-ok-{recordRunHistory}", (_, _) => Task.FromResult<string?>(null), recordRunHistory);
        var failing = new FakeTask($"trace-fail-{recordRunHistory}", (_, _) => throw new InvalidOperationException("boom"), recordRunHistory);

        var okSpan = await RunTracedAsync(CreateRunner(factory), ok);
        var failedSpan = await RunTracedAsync(CreateRunner(factory), failing);

        Assert.Null(okSpan.ParentId);
        Assert.Equal(ok.Name, okSpan.GetTagItem("javbuddy.task.name"));
        Assert.Equal("success", okSpan.GetTagItem("javbuddy.outcome"));
        Assert.Equal("failure", failedSpan.GetTagItem("javbuddy.outcome"));
        Assert.Equal(ActivityStatusCode.Error, failedSpan.Status);
        Assert.Equal("System.InvalidOperationException", failedSpan.GetTagItem("error.type"));
        Assert.Contains(failedSpan.Events, e => e.Name == "exception");
    }

    [Fact]
    public async Task RunAsync_SuccessfulTask_RecordsSuccessAndSummary()
    {
        using var factory = new TestDbContextFactory();
        var task = new FakeTask("test-task", (_, _) => Task.FromResult<string?>("did the thing"));

        await CreateRunner(factory).RunAsync(task, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.Equal("test-task", run.TaskName);
        Assert.True(run.Success);
        Assert.Equal("did the thing", run.ResultSummary);
        Assert.Null(run.ErrorMessage);
        Assert.NotEqual(default, run.QueuedAt);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.EndedAt);
    }

    [Fact]
    public async Task RunAsync_TaskThrows_RecordsFailureAndErrorMessage()
    {
        using var factory = new TestDbContextFactory();
        var task = new FakeTask("failing-task", (_, _) => throw new InvalidOperationException("boom"));

        await CreateRunner(factory).RunAsync(task, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.False(run.Success);
        Assert.Equal("boom", run.ErrorMessage);
        Assert.Null(run.ResultSummary);
        Assert.NotNull(run.EndedAt);
    }

    [Fact]
    public async Task RunAsync_SuccessfulTask_RecordsRunCounterAndDuration()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();
        var task = new FakeTask("test-task", (_, _) => Task.FromResult<string?>("did the thing"));

        await CreateRunner(factory, metrics).RunAsync(task, CancellationToken.None);

        var run = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_scheduled_task_runs_total");
        Assert.Equal("test-task", run.Tags["task"]);
        Assert.Equal("success", run.Tags["outcome"]);
        var duration = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_scheduled_task_duration_seconds");
        Assert.Equal("test-task", duration.Tags["task"]);
    }

    [Fact]
    public async Task RunAsync_TaskThrows_RecordsFailureOutcome()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();
        var task = new FakeTask("failing-task", (_, _) => throw new InvalidOperationException("boom"));

        await CreateRunner(factory, metrics).RunAsync(task, CancellationToken.None);

        var run = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_scheduled_task_runs_total");
        Assert.Equal("failure", run.Tags["outcome"]);
    }

    [Fact]
    public async Task RunAsync_SuccessOnly_RecordsTheTaskLastSuccessTimestamp()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();
        var ok = new FakeTask("ok-task", (_, _) => Task.FromResult<string?>(null));
        var failing = new FakeTask("failing-task", (_, _) => throw new InvalidOperationException("boom"));
        var unrecorded = new FakeTask("quiet-task", (_, _) => Task.FromResult<string?>(null), recordRunHistory: false);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var runner = CreateRunner(factory, metrics);
        await runner.RunAsync(ok, CancellationToken.None);
        await runner.RunAsync(failing, CancellationToken.None);
        await runner.RunAsync(unrecorded, CancellationToken.None);
        recorder.SampleGauges();

        var stamps = recorder.Measurements
            .Where(m => m.InstrumentName == "javbuddy_scheduled_task_last_success_timestamp_seconds")
            .ToDictionary(m => (string)m.Tags["task"]!, m => (double)m.Value!);
        Assert.Equal(["ok-task", "quiet-task"], stamps.Keys.Order());
        Assert.All(stamps.Values, v => Assert.InRange(v, before, before + 60));
    }

    [Fact]
    public async Task RunAsync_ProgressReported_FlushesFinalValueToTheRow()
    {
        using var factory = new TestDbContextFactory();
        var task = new FakeTask("progress-task", async (_, progress) =>
        {
            for (var i = 1; i <= 10; i++)
            {
                progress.Report(new TaskProgress(i, 10));
            }
            await Task.CompletedTask;
            return "done";
        });

        await CreateRunner(factory).RunAsync(task, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.Equal(10, run.ProgressCurrent);
        Assert.Equal(10, run.ProgressTotal);
    }

    [Fact]
    public async Task RunAsync_CancelledTask_RecordsFailureAndEndedAt()
    {
        using var factory = new TestDbContextFactory();
        using var cts = new CancellationTokenSource();
        var task = new FakeTask("cancelled-task", (ct, _) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("done");
        });

        await CreateRunner(factory).RunAsync(task, cts.Token);

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.False(run.Success);
        Assert.Equal("Task was cancelled.", run.ErrorMessage);
        Assert.NotNull(run.EndedAt);
    }

    [Fact]
    public async Task RunAsync_TaskWithRecordRunHistoryFalse_WritesNoRowButStillRecordsMetrics()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();
        var ran = false;
        var task = new FakeTask("frequent-task", (_, _) =>
        {
            ran = true;
            return Task.FromResult<string?>("did the thing");
        }, recordRunHistory: false);

        await CreateRunner(factory, metrics).RunAsync(task, CancellationToken.None);

        Assert.True(ran);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.ScheduledTaskRuns);
        var run = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_scheduled_task_runs_total");
        Assert.Equal("success", run.Tags["outcome"]);
    }

    [Fact]
    public async Task RunAsync_TaskWithRecordRunHistoryFalseThrows_WritesNoRowButLogsFailure()
    {
        using var factory = new TestDbContextFactory();
        var logger = Substitute.For<ILogger<ScheduledTaskRunner>>();
        var task = new FakeTask("frequent-failing-task", (_, _) => throw new InvalidOperationException("boom"), recordRunHistory: false);

        await new ScheduledTaskRunner(factory, new JavbuddyMetrics(), new ScheduledTaskChangeNotifier(), new ScheduledTaskCancellationRegistry(), new ScheduledTaskRunTracker(), logger).RunAsync(task, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.ScheduledTaskRuns);
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CleanUpOrphanedRunsAsync_TerminatesUnfinishedRunsAndNotifies()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new ScheduledTaskChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;

        var now = DateTime.UtcNow;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.AddRange(
                new ScheduledTaskRun { TaskName = "Stuck Task 1", QueuedAt = now.AddHours(-2), StartedAt = now.AddHours(-2), EndedAt = null },
                new ScheduledTaskRun { TaskName = "Stuck Task 2", QueuedAt = now.AddHours(-1), StartedAt = null, EndedAt = null },
                new ScheduledTaskRun { TaskName = "Completed Task", QueuedAt = now.AddHours(-3), StartedAt = now.AddHours(-3), EndedAt = now.AddHours(-2), Success = true, ResultSummary = "ok" }
            );
            await db.SaveChangesAsync();
        }

        var cleanedCount = await CreateRunner(factory, notifier: notifier).CleanUpOrphanedRunsAsync();

        Assert.Equal(2, cleanedCount);
        Assert.Equal(1, notifications);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var runs = await db.ScheduledTaskRuns.OrderBy(r => r.Id).ToListAsync();
            Assert.False(runs[0].Success);
            Assert.NotNull(runs[0].EndedAt);
            Assert.Equal("Interrupted by application restart/shutdown", runs[0].ErrorMessage);

            Assert.False(runs[1].Success);
            Assert.NotNull(runs[1].EndedAt);
            Assert.Equal("Interrupted by application restart/shutdown", runs[1].ErrorMessage);

            Assert.True(runs[2].Success);
            Assert.Equal("ok", runs[2].ResultSummary);
        }
    }

    [Fact]
    public async Task PruneOldRunsAsync_KeepsATasksNewestSuccessfulRun_EvenWhenItHasFailedSince()
    {
        using var factory = new TestDbContextFactory();
        var now = DateTime.UtcNow;
        static ScheduledTaskRun Run(DateTime queuedAt, bool success) => new() { TaskName = "Failing", QueuedAt = queuedAt, EndedAt = queuedAt.AddMinutes(1), Success = success };
        var oldSuccess = Run(now.AddDays(-300), true);
        var olderSuccess = Run(now.AddDays(-350), true);
        var failure = Run(now.AddDays(-200), false);
        var newestFailure = Run(now.AddDays(-150), false);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.AddRange(olderSuccess, oldSuccess, failure, newestFailure);
            await db.SaveChangesAsync();
        }

        var deleted = await CreateRunner(factory).PruneOldRunsAsync(TimeSpan.FromDays(90));

        Assert.Equal(2, deleted);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(new[] { oldSuccess.Id, newestFailure.Id }.Order(), await verify.ScheduledTaskRuns.Select(r => r.Id).OrderBy(i => i).ToListAsync());
    }

    [Fact]
    public async Task PruneOldRunsAsync_DeletesOldFinishedRuns_ButKeepsEachTasksNewestAndRecentOnes()
    {
        using var factory = new TestDbContextFactory();
        var now = DateTime.UtcNow;
        var busyOld = Run("Busy", now.AddDays(-200), finished: true);
        var freshOld = Run("Fresh", now.AddDays(-120), finished: true);
        ScheduledTaskRun[] runs =
        [
            // "Busy": two old finished runs, only the older one may go.
            busyOld,
            Run("Busy", now.AddDays(-150), finished: true),
            // "Rare": a single old finished run is its "last execution", so it stays.
            Run("Rare", now.AddDays(-300), finished: true),
            // "Fresh": a recent finished run behind an old one.
            freshOld,
            Run("Fresh", now.AddDays(-1), finished: true),
            // "Stuck": an old run that never ended is not pruned.
            Run("Stuck", now.AddDays(-400), finished: false),
            Run("Stuck", now.AddDays(-399), finished: false),
            // "Running": the newest run is unfinished, so the old finished one is still the last execution.
            Run("Running", now.AddDays(-200), finished: true),
            Run("Running", now.AddDays(-100), finished: false),
            // "Late": the earlier-queued run ended last (the Tasks page's last execution), the other was queued last.
            Run("Late", now.AddDays(-200), endedAt: now.AddDays(-10)),
            Run("Late", now.AddDays(-150), finished: true),
        ];
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.AddRange(runs);
            await db.SaveChangesAsync();
        }

        var deleted = await CreateRunner(factory).PruneOldRunsAsync(TimeSpan.FromDays(90));

        Assert.Equal(2, deleted);
        await using var verify = await factory.CreateDbContextAsync();
        var remaining = await verify.ScheduledTaskRuns.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();
        Assert.Equal(runs.Where(r => r != busyOld && r != freshOld).Select(r => r.Id).Order(), remaining);

        static ScheduledTaskRun Run(string name, DateTime queuedAt, bool finished = true, DateTime? endedAt = null) => new()
        {
            TaskName = name,
            QueuedAt = queuedAt,
            EndedAt = endedAt ?? (finished ? queuedAt.AddMinutes(1) : null),
            Success = finished,
        };
    }

    [Fact]
    public async Task CleanUpOrphanedRunsAsync_NoOrphanedRuns_ReturnsZeroAndDoesNotNotify()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new ScheduledTaskChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;

        var now = DateTime.UtcNow;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun
            {
                TaskName = "Completed Task",
                QueuedAt = now.AddHours(-1),
                StartedAt = now.AddHours(-1),
                EndedAt = now,
                Success = true
            });
            await db.SaveChangesAsync();
        }

        var cleanedCount = await CreateRunner(factory, notifier: notifier).CleanUpOrphanedRunsAsync();

        Assert.Equal(0, cleanedCount);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task MarkRunAsFailedAsync_ActiveRun_TerminatesRunAndNotifies()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new ScheduledTaskChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;

        int runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = new ScheduledTaskRun { TaskName = "Stuck Task", QueuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var result = await CreateRunner(factory, notifier: notifier).MarkRunAsFailedAsync(runId, "Manually dismissed");

        Assert.True(result);
        Assert.Equal(1, notifications);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.ScheduledTaskRuns.SingleAsync(r => r.Id == runId);
            Assert.False(run.Success);
            Assert.NotNull(run.EndedAt);
            Assert.Equal("Manually dismissed", run.ErrorMessage);
        }
    }

    [Fact]
    public async Task MarkRunAsFailedAsync_AlreadyEndedOrNonexistentRun_ReturnsFalse()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new ScheduledTaskChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;

        int completedRunId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = new ScheduledTaskRun
            {
                TaskName = "Completed Task",
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                EndedAt = DateTime.UtcNow,
                Success = true
            };
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync();
            completedRunId = run.Id;
        }

        var runner = CreateRunner(factory, notifier: notifier);
        var nonexistentResult = await runner.MarkRunAsFailedAsync(999);
        var completedResult = await runner.MarkRunAsFailedAsync(completedRunId);

        Assert.False(nonexistentResult);
        Assert.False(completedResult);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task CancelRunAsync_ActiveRun_StopsTheTaskAndRecordsCancellation()
    {
        using var factory = new TestDbContextFactory();
        var registry = new ScheduledTaskCancellationRegistry();
        var started = new TaskCompletionSource();
        var task = new FakeTask("long-task", async (ct, _) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return "never";
        });

        var running = CreateRunner(factory, registry: registry).RunAsync(task, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        int runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            runId = (await db.ScheduledTaskRuns.SingleAsync()).Id;
        }
        Assert.True(registry.IsActive(runId));

        // A different runner instance, as the page resolves its own from a fresh scope.
        var result = await CreateRunner(factory, registry: registry).CancelRunAsync(runId);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        Assert.False(registry.IsActive(runId));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.ScheduledTaskRuns.SingleAsync(r => r.Id == runId);
            Assert.False(run.Success);
            Assert.NotNull(run.EndedAt);
            Assert.Equal("Task was cancelled.", run.ErrorMessage);
        }
    }

    [Fact]
    public async Task CancelRunAsync_OrphanedRun_FallsBackToMarkingFailed()
    {
        using var factory = new TestDbContextFactory();
        int runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = new ScheduledTaskRun { TaskName = "Orphaned Task", QueuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var result = await CreateRunner(factory).CancelRunAsync(runId);

        Assert.True(result);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.ScheduledTaskRuns.SingleAsync(r => r.Id == runId);
            Assert.False(run.Success);
            Assert.NotNull(run.EndedAt);
            Assert.Equal("Manually marked as failed", run.ErrorMessage);
        }
    }

    [Fact]
    public async Task RunAsync_CompletedRun_IsNoLongerRegisteredAsActive()
    {
        using var factory = new TestDbContextFactory();
        var registry = new ScheduledTaskCancellationRegistry();
        var task = new FakeTask("test-task", (_, _) => Task.FromResult<string?>("done"));

        await CreateRunner(factory, registry: registry).RunAsync(task, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var runId = (await db.ScheduledTaskRuns.SingleAsync()).Id;
        Assert.False(registry.IsActive(runId));
        Assert.False(registry.TryCancel(runId));
    }

    [Fact]
    public async Task RunAsync_TaskAlreadyRunning_IsRejectedWithoutARowOrASecondExecution()
    {
        using var factory = new TestDbContextFactory();
        var tracker = new ScheduledTaskRunTracker();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var executions = 0;
        var task = new FakeTask("Library Rescan", async (_, _) =>
        {
            Interlocked.Increment(ref executions);
            started.SetResult();
            await release.Task;
            return "done";
        });

        var first = CreateRunner(factory, tracker: tracker).RunAsync(task, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var second = await CreateRunner(factory, tracker: tracker).RunAsync(task, CancellationToken.None);
        release.SetResult();

        Assert.False(second);
        Assert.True(await first);
        Assert.Equal(1, executions);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.ScheduledTaskRuns.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_ReleasesTheTaskAfterAFailure_SoItCanRunAgain()
    {
        using var factory = new TestDbContextFactory();
        var tracker = new ScheduledTaskRunTracker();
        var runner = CreateRunner(factory, tracker: tracker);
        var task = new FakeTask("Flaky", (_, _) => throw new InvalidOperationException("boom"));

        Assert.True(await runner.RunAsync(task, CancellationToken.None));
        Assert.False(tracker.IsRunning("Flaky"));
        Assert.True(await runner.RunAsync(task, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_TaskWithoutHistory_RecordsItsLastCompletionInMemory()
    {
        using var factory = new TestDbContextFactory();
        var tracker = new ScheduledTaskRunTracker();
        var task = new FakeTask("Sync", (_, _) => Task.FromResult<string?>(null), recordRunHistory: false);
        var before = DateTime.UtcNow;

        await CreateRunner(factory, tracker: tracker).RunAsync(task, CancellationToken.None);

        Assert.True(tracker.GetLastCompletion("Sync") >= before);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ScheduledTaskRuns.ToListAsync());
    }
}
