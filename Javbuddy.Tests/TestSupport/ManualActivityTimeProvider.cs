namespace Javbuddy.Tests.TestSupport;

public sealed class ManualActivityTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private TimeSpan elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, elapsed + dueTime);
        timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        elapsed += duration;
        foreach (var timer in timers.ToArray())
        {
            if (timer.Due <= elapsed) timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        private bool disposed;
        public TimeSpan Due { get; } = due;
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
        public void Fire()
        {
            if (disposed) return;
            disposed = true;
            callback(state);
        }
    }
}
