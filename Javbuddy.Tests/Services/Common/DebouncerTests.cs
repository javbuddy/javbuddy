using Javbuddy.Services.Common;
using Microsoft.Extensions.Time.Testing;

namespace Javbuddy.Tests.Services.Common;

public class DebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task RunAsync_RunsActionOnlyAfterTheDelay()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);
        var ran = false;

        var run = debouncer.RunAsync(_ => { ran = true; return Task.CompletedTask; });
        time.Advance(Delay - TimeSpan.FromMilliseconds(1));
        Assert.False(ran);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await run;
        Assert.True(ran);
    }

    [Fact]
    public async Task RunAsync_NewerCallSupersedesThePendingOne()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);
        var seen = new List<string>();

        var first = debouncer.RunAsync(_ => { seen.Add("a"); return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(100));
        var second = debouncer.RunAsync(_ => { seen.Add("ab"); return Task.CompletedTask; });
        time.Advance(Delay);
        await Task.WhenAll(first, second);

        Assert.Equal(["ab"], seen);
    }

    [Fact]
    public async Task RunAsync_RunningActionsTokenIsCancelledByANewerCall()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);
        var started = new TaskCompletionSource<CancellationToken>();
        var release = new TaskCompletionSource();

        var first = debouncer.RunAsync(async ct =>
        {
            started.SetResult(ct);
            await release.Task;
        });
        time.Advance(Delay);
        var firstToken = await started.Task;
        Assert.False(firstToken.IsCancellationRequested);

        var second = debouncer.RunAsync(_ => Task.CompletedTask);
        Assert.True(firstToken.IsCancellationRequested);

        release.SetResult();
        time.Advance(Delay);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Cancel_DropsThePendingCall()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);
        var ran = false;

        var run = debouncer.RunAsync(_ => { ran = true; return Task.CompletedTask; });
        debouncer.Cancel();
        time.Advance(Delay);
        await run;

        Assert.False(ran);
    }

    [Fact]
    public async Task Dispose_CancelsThePendingCall()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var ran = false;

        var run = debouncer.RunAsync(_ => { ran = true; return Task.CompletedTask; });
        debouncer.Dispose();
        time.Advance(Delay);
        await run;

        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_ActionExceptionsPropagate()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);

        var run = debouncer.RunAsync(_ => throw new InvalidOperationException("boom"));
        time.Advance(Delay);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
    }
}
