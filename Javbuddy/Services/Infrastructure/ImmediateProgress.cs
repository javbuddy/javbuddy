namespace Javbuddy.Services.Infrastructure;

/// <summary>Reports synchronously on the calling thread — unlike System.Progress&lt;T&gt;, which
/// marshals back to whatever SynchronizationContext was captured at construction time (or, with
/// none, to the thread pool, where reports can arrive out of order). For singletons that just keep
/// the freshest value, not deliver it to a particular UI thread.</summary>
public sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
