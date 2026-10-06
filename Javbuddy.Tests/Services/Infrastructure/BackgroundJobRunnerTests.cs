using Javbuddy.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Infrastructure;

public class BackgroundJobRunnerTests
{
    private sealed class ScopedProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static (BackgroundJobRunner Runner, ILogger<BackgroundJobRunner> Logger) CreateRunner()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedProbe>();
        var provider = services.BuildServiceProvider();
        var logger = Substitute.For<ILogger<BackgroundJobRunner>>();
        return (new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), logger), logger);
    }

    [Fact]
    public async Task Enqueue_ReturnsBeforeTheJobFinishes()
    {
        var (runner, _) = CreateRunner();
        var release = new TaskCompletionSource();

        var job = runner.Enqueue("slow", (_, _) => release.Task);

        Assert.False(job.IsCompleted);
        release.SetResult();
        await job.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Enqueue_RunsEachJobInItsOwnScope_DisposedWhenTheJobEnds()
    {
        var (runner, _) = CreateRunner();
        ScopedProbe? first = null;
        ScopedProbe? second = null;

        await runner.Enqueue("a", (services, _) => { first = services.GetRequiredService<ScopedProbe>(); return Task.CompletedTask; });
        await runner.Enqueue("b", (services, _) => { second = services.GetRequiredService<ScopedProbe>(); return Task.CompletedTask; });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task Enqueue_JobThrows_LogsTheErrorAndTheReturnedTaskDoesNotFault()
    {
        var (runner, logger) = CreateRunner();

        var job = runner.Enqueue("broken", (_, _) => throw new InvalidOperationException("boom"));
        await job.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(job.IsCompletedSuccessfully);
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(e => e is InvalidOperationException),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightJobsAndWaitsForThemToFinish()
    {
        var (runner, logger) = CreateRunner();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanedUp = false;

        var job = runner.Enqueue("long", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                await Task.Delay(50, CancellationToken.None);
                cleanedUp = true;
            }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(job.IsCompleted);
        Assert.True(cleanedUp);
        logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task StopAsync_ReturnsWhenTheShutdownTimeoutElapses_EvenIfAJobIgnoresCancellation()
    {
        var (runner, _) = CreateRunner();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource();

        var job = runner.Enqueue("stubborn", async (_, _) =>
        {
            started.SetResult();
            await release.Task;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await runner.StopAsync(shutdownTimeout.Token).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(job.IsCompleted);
        release.SetResult();
        await job.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Enqueue_AfterShutdownHasBegun_DoesNotRunTheJob()
    {
        var (runner, _) = CreateRunner();
        await runner.StopAsync(CancellationToken.None);
        var ran = false;

        await runner.Enqueue("late", (_, _) => { ran = true; return Task.CompletedTask; });

        Assert.False(ran);
    }
}
