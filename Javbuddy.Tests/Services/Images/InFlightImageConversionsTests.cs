using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class InFlightImageConversionsTests
{
    [Fact]
    public async Task RunOnceAsync_ConcurrentCallersForOneKey_ShareOneRun()
    {
        var inFlight = new InFlightImageConversions();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        async Task<int> Work()
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            return 42;
        }

        var callers = Enumerable.Range(0, 5).Select(_ => inFlight.RunOnceAsync("key", Work)).ToList();
        release.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, runs);
        Assert.All(results, r => Assert.Equal(42, r));
    }

    [Fact]
    public async Task RunOnceAsync_AfterRunCompletes_RunsAgain()
    {
        var inFlight = new InFlightImageConversions();
        var runs = 0;

        await inFlight.RunOnceAsync("key", () => Task.FromResult(Interlocked.Increment(ref runs)));
        await inFlight.RunOnceAsync("key", () => Task.FromResult(Interlocked.Increment(ref runs)));

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task RunOnceAsync_DifferentKeys_RunSeparately()
    {
        var inFlight = new InFlightImageConversions();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        async Task<int> Work()
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            return 0;
        }

        var a = inFlight.RunOnceAsync("a", Work);
        var b = inFlight.RunOnceAsync("b", Work);
        release.SetResult();
        await Task.WhenAll(a, b);

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task RunOnceAsync_CancelledCaller_DoesNotCancelTheSharedRun()
    {
        var inFlight = new InFlightImageConversions();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Work()
        {
            await release.Task;
            return "done";
        }

        using var cts = new CancellationTokenSource();
        var cancelled = inFlight.RunOnceAsync("key", Work, cts.Token);
        var other = inFlight.RunOnceAsync("key", Work);
        await cts.CancelAsync();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal("done", await other);
    }

    [Fact]
    public async Task RunOnceAsync_FailedRun_FailsEveryCallerAndIsNotReused()
    {
        var inFlight = new InFlightImageConversions();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Failing()
        {
            await release.Task;
            throw new InvalidOperationException("boom");
        }

        var first = inFlight.RunOnceAsync("key", Failing);
        var second = inFlight.RunOnceAsync("key", Failing);
        release.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(7, await inFlight.RunOnceAsync("key", () => Task.FromResult(7)));
    }
}
