using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public sealed class ScheduledTaskHostedServiceTests : IAsyncDisposable
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Failsafe = TimeSpan.FromSeconds(30);

    private sealed class CountingTask(string name, TimeSpan interval, bool recordRunHistory = true, bool runImmediately = true, Func<int>? onInterval = null) : IScheduledTask
    {
        private int runs;
        private readonly SemaphoreSlim ran = new(0);

        public string Name { get; } = name;
        public string Description => "";
        public bool RecordRunHistory { get; } = recordRunHistory;
        public bool RunImmediatelyWhenNeverRun { get; } = runImmediately;
        public int Runs => Volatile.Read(ref runs);

        public TimeSpan GetInterval()
        {
            onInterval?.Invoke();
            return interval;
        }

        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
        {
            Interlocked.Increment(ref runs);
            ran.Release();
            return Task.FromResult<string?>("ok");
        }

        public Task WaitForRunAsync() => ran.WaitAsync(Failsafe);
    }

    private readonly TestDbContextFactory factory = new();
    private readonly JavbuddyMetrics metrics = new();
    private readonly SignalingFakeTimeProvider time = new();
    private readonly ScheduledTaskRunTracker tracker = new();
    private readonly ILogger<ScheduledTaskHostedService> logger = Substitute.For<ILogger<ScheduledTaskHostedService>>();
    private ServiceProvider? provider;
    private ScheduledTaskHostedService? service;

    public async ValueTask DisposeAsync()
    {
        if (service is not null) await service.StopAsync(CancellationToken.None);
        if (provider is not null) await provider.DisposeAsync();
        factory.Dispose();
        metrics.Dispose();
    }

    private async Task StartAsync(params IScheduledTask[] tasks)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        services.AddSingleton(metrics);
        services.AddSingleton(new ScheduledTaskChangeNotifier());
        services.AddSingleton<ILogger<ScheduledTaskRunner>>(NullLogger<ScheduledTaskRunner>.Instance);
        services.AddSingleton<ILogger<BackgroundJobRunner>>(NullLogger<BackgroundJobRunner>.Instance);
        services.AddSingleton<ScheduledTaskCancellationRegistry>();
        services.AddSingleton(tracker);
        services.AddScoped<ScheduledTaskRunner>();
        foreach (var task in tasks) services.AddSingleton(task);
        services.AddSingleton<BackgroundJobRunner>();
        provider = services.BuildServiceProvider();

        var jobs = provider.GetRequiredService<BackgroundJobRunner>();
        await jobs.StartAsync(CancellationToken.None);
        service = new ScheduledTaskHostedService(provider.GetRequiredService<IServiceScopeFactory>(), new ScheduledTaskLauncher(jobs, tracker), tracker, logger, time);
        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NothingRunsBeforeTheStartupDelayHasPassed()
    {
        var task = new CountingTask("Soon", Minute);
        await StartAsync(task);
        await time.WaitForTimerAsync();

        time.Advance(Minute - TimeSpan.FromSeconds(1));

        // Had the startup delay ended, the loop would have ticked and gone back to waiting on a new timer.
        Assert.False(await time.TryWaitForTimerAsync(TimeSpan.FromMilliseconds(100)), "the loop ticked before the startup delay ended");
        Assert.Equal(0, task.Runs);
        Assert.False(tracker.IsRunning("Soon"));
    }

    [Fact]
    public async Task ADueTaskIsRunAfterTheStartupDelay_AndRecordedInHistory()
    {
        var task = new CountingTask("Due", Minute);
        await StartAsync(task);

        await time.AdvanceWhenWaitingAsync(Minute);
        await task.WaitForRunAsync();
        await tracker.WaitUntilIdleAsync("Due");

        Assert.Equal(1, task.Runs);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("Due", (await db.ScheduledTaskRuns.SingleAsync()).TaskName);
    }

    [Fact]
    public async Task ANeverRunTaskWithoutRunImmediately_WaitsAnIntervalFromProcessStart()
    {
        var waiting = new CountingTask("Waiting", TimeSpan.FromHours(6), runImmediately: false);
        var probe = new CountingTask("Probe", Minute);
        await StartAsync(waiting, probe);

        await time.AdvanceWhenWaitingAsync(Minute);
        await probe.WaitForRunAsync();
        await time.WaitForTimerAsync(); // the tick has decided about every task

        // A launch claims the task at once, so a task that had been launched would still be running or have run.
        Assert.Equal(0, waiting.Runs);
        Assert.False(tracker.IsRunning("Waiting"));
        Assert.Null(tracker.GetLastCompletion("Waiting"));
    }

    [Fact]
    public async Task ATaskWithoutRunHistory_RunsOncePerInterval_NotOnEveryTick()
    {
        // F-18: a task that records no history has no database row to find a last run in, so it was treated as
        // never run on every tick and fired on every poll. Its in-memory last completion must hold it back.
        var noHistory = new CountingTask("No history", TimeSpan.FromHours(1), recordRunHistory: false);
        var probe = new CountingTask("Probe", Minute);
        await StartAsync(noHistory, probe);

        await time.AdvanceWhenWaitingAsync(Minute);
        await noHistory.WaitForRunAsync();
        await probe.WaitForRunAsync();
        await tracker.WaitUntilIdleAsync("No history");
        await tracker.WaitUntilIdleAsync("Probe");

        await time.AdvanceWhenWaitingAsync(Minute); // the second tick: the probe runs again, the task is not yet due
        await probe.WaitForRunAsync();
        await time.WaitForTimerAsync(); // the second tick has decided about every task

        Assert.False(tracker.IsRunning("No history"));
        Assert.Equal(1, noHistory.Runs);
        Assert.Equal(2, probe.Runs);
    }

    [Fact]
    public async Task ATaskIsRunAgainOnceItsIntervalHasPassedSinceItsLastRun()
    {
        var task = new CountingTask("Frequent", Minute, recordRunHistory: false);
        await StartAsync(task);

        await time.AdvanceWhenWaitingAsync(Minute);
        await task.WaitForRunAsync();
        await tracker.WaitUntilIdleAsync("Frequent");
        await time.AdvanceWhenWaitingAsync(Minute);
        await task.WaitForRunAsync();

        Assert.Equal(2, task.Runs);
    }

    [Fact]
    public async Task ATaskStillRunning_IsNotLaunchedASecondTime()
    {
        var release = new TaskCompletionSource<string?>();
        var started = new SemaphoreSlim(0);
        var slow = new BlockingTask(release.Task, started);
        await StartAsync(slow);

        await time.AdvanceWhenWaitingAsync(Minute);
        await started.WaitAsync(Failsafe);
        await time.AdvanceWhenWaitingAsync(Minute);
        await time.AdvanceWhenWaitingAsync(Minute);
        await time.WaitForTimerAsync(); // the third tick is over
        // A launch from tick two or three would start on the background runner a moment later.
        Assert.False(await started.WaitAsync(TimeSpan.FromMilliseconds(100)), "the running task was started again");
        Assert.Equal(1, slow.Runs);

        release.SetResult("done");
    }

    private sealed class BlockingTask(Task<string?> release, SemaphoreSlim started) : IScheduledTask
    {
        private int runs;
        public string Name => "Slow";
        public string Description => "";
        public bool RunImmediatelyWhenNeverRun => true;
        public int Runs => Volatile.Read(ref runs);
        public TimeSpan GetInterval() => TimeSpan.FromSeconds(1);

        public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
        {
            Interlocked.Increment(ref runs);
            started.Release();
            return await release;
        }
    }

    [Fact]
    public async Task AFailingTick_IsLoggedAndTheLoopKeepsGoing()
    {
        var calls = 0;
        var flaky = new CountingTask("Flaky", Minute, onInterval: () => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("bad interval") : 0);
        await StartAsync(flaky);

        await time.AdvanceWhenWaitingAsync(Minute);
        await time.WaitForTimerAsync(); // the loop got back to waiting after the failed tick
        Assert.Equal(0, flaky.Runs);
        Assert.False(tracker.IsRunning("Flaky"));
        time.Advance(Minute);
        await flaky.WaitForRunAsync();

        Assert.Equal(1, flaky.Runs);
        logger.Received().Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Is<Exception?>(e => e is InvalidOperationException), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task StoppingDuringTheStartupDelay_ReturnsWithoutRunningAnything()
    {
        var task = new CountingTask("Never", Minute);
        await StartAsync(task);
        await time.WaitForTimerAsync();

        await service!.StopAsync(CancellationToken.None);

        Assert.Equal(0, task.Runs);
    }

    [Fact]
    public async Task StoppingBetweenTicks_EndsTheLoop()
    {
        var task = new CountingTask("Once", TimeSpan.FromHours(1), recordRunHistory: false);
        await StartAsync(task);
        await time.AdvanceWhenWaitingAsync(Minute);
        await task.WaitForRunAsync();
        await time.WaitForTimerAsync(); // waiting for the next tick

        await service!.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompleted);
    }
}
