namespace Javbuddy.Services.Common;

/// <summary>Runs an action only once input has been quiet for <c>delay</c>: each <see cref="RunAsync"/>
/// supersedes the previous one still waiting (or running), whose token is cancelled. For a search box
/// or text filter that shouldn't reload on every keystroke. Create one per input and dispose it with
/// the owning component, which also cancels a pending run.</summary>
public sealed class Debouncer(TimeSpan delay, TimeProvider? timeProvider = null) : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? current;

    public Debouncer(TimeProvider? timeProvider = null) : this(DefaultDelay, timeProvider)
    {
    }

    /// <summary>Waits out the delay, then runs <paramref name="action"/> with a token that is
    /// cancelled if a newer call (or <see cref="Cancel"/>) supersedes this one. A superseded call
    /// returns quietly; exceptions from the action itself propagate.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        Replace(cts);

        try
        {
            await Task.Delay(delay, timeProvider, token);
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by a newer call, or cancelled.
        }
    }

    /// <summary>Cancels the pending (or running) call, if any.</summary>
    public void Cancel() => Replace(null);

    public void Dispose() => Replace(null);

    private void Replace(CancellationTokenSource? next)
    {
        var previous = Interlocked.Exchange(ref current, next);
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }
}
