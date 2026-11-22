using Microsoft.Extensions.Time.Testing;

namespace Javbuddy.Tests.TestSupport;

/// <summary>A <see cref="FakeTimeProvider"/> that signals every timer the code under test creates, so a test advances
/// the clock only once the code is actually waiting on it, instead of sleeping and hoping it got there.</summary>
public sealed class SignalingFakeTimeProvider() : FakeTimeProvider(DateTimeOffset.UtcNow)
{
    private static readonly TimeSpan Failsafe = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim timerCreated = new(0);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        timerCreated.Release();
        return timer;
    }

    /// <summary>Completes once the code under test has created one more timer than this has already reported. The
    /// timeout is only a failsafe against a hung test.</summary>
    public Task WaitForTimerAsync() => timerCreated.WaitAsync(Failsafe);

    /// <summary>For asserting that the code under test did NOT start waiting on the clock again: an absence can only be
    /// observed by waiting, so this waits up to <paramref name="within"/> and reports whether a timer appeared.</summary>
    public Task<bool> TryWaitForTimerAsync(TimeSpan within) => timerCreated.WaitAsync(within);

    /// <summary>Waits for the code under test to start waiting on the clock, then moves it forward by <paramref name="by"/>.</summary>
    public async Task AdvanceWhenWaitingAsync(TimeSpan by)
    {
        await WaitForTimerAsync();
        Advance(by);
    }
}
