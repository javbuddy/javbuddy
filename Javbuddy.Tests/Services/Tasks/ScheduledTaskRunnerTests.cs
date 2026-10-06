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

    private static ScheduledTaskRunner CreateRunner(TestDbContextFactory factory, JavbuddyMetrics? metrics = null, ScheduledTaskChangeNotifier? notifier = null, ScheduledTaskCancellationRegistry? registry = null) =>
        new(factory, metrics ?? new JavbuddyMetrics(), notifier ?? new ScheduledTaskChangeNotifier(), registry ?? new ScheduledTaskCancellationRegistry(), Substitute.For<ILogger<ScheduledTaskRunner>>());

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

        await new ScheduledTaskRunner(factory, new JavbuddyMetrics(), new ScheduledTaskChangeNotifier(), new ScheduledTaskCancellationRegistry(), logger).RunAsync(task, CancellationToken.None);

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
}
