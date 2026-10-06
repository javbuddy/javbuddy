using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class ScheduledTaskLauncherTests
{
    private sealed class FakeTask(string name, Func<CancellationToken, Task<string?>> run) : IScheduledTask
    {
        public string Name { get; } = name;
        public string Description => "";
        public TimeSpan GetInterval() => TimeSpan.FromHours(1);
        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress) => run(ct);
    }

    private static (ScheduledTaskLauncher Launcher, BackgroundJobRunner Runner) CreateLauncher(TestDbContextFactory factory, params IScheduledTask[] tasks)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        services.AddSingleton(new JavbuddyMetrics());
        services.AddSingleton(new ScheduledTaskChangeNotifier());
        services.AddSingleton(Substitute.For<ILogger<ScheduledTaskRunner>>());
        services.AddSingleton<ScheduledTaskCancellationRegistry>();
        services.AddScoped<ScheduledTaskRunner>();
        foreach (var task in tasks) services.AddSingleton(task);
        var provider = services.BuildServiceProvider();

        var runner = new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        return (new ScheduledTaskLauncher(runner), runner);
    }

    [Fact]
    public async Task Queue_RunsTheNamedTaskThroughTheRunner_RecordingHistory()
    {
        using var factory = new TestDbContextFactory();
        var (launcher, _) = CreateLauncher(factory,
            new FakeTask("Other", _ => throw new InvalidOperationException("wrong task")),
            new FakeTask("Library Rescan", _ => Task.FromResult<string?>("done")));

        await launcher.Queue("Library Rescan").WaitAsync(TimeSpan.FromSeconds(30));

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.Equal("Library Rescan", run.TaskName);
        Assert.True(run.Success);
        Assert.Equal("done", run.ResultSummary);
    }

    [Fact]
    public async Task Queue_UnknownTaskName_DoesNothing()
    {
        using var factory = new TestDbContextFactory();
        var (launcher, _) = CreateLauncher(factory, new FakeTask("Library Rescan", _ => Task.FromResult<string?>("done")));

        await launcher.Queue("Nope").WaitAsync(TimeSpan.FromSeconds(30));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.ScheduledTaskRuns);
    }

    [Fact]
    public async Task Queue_ApplicationShutsDownMidRun_RecordsTheRunAsCancelled()
    {
        using var factory = new TestDbContextFactory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (launcher, runner) = CreateLauncher(factory, new FakeTask("Library Rescan", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }));

        var job = launcher.Queue("Library Rescan");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(job.IsCompleted);
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.False(run.Success);
        Assert.Equal("Task was cancelled.", run.ErrorMessage);
        Assert.NotNull(run.EndedAt);
    }
}
