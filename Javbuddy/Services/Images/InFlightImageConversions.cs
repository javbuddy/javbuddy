using System.Collections.Concurrent;

namespace Javbuddy.Services.Images;

/// <summary>De-duplicates concurrent cache-miss conversions of the same image: the
/// first caller for a key runs the work, and every caller that arrives while it's still running
/// awaits that same task instead of decoding the source again. Singleton, since the requests it
/// joins up come from different scopes (one per HTTP request).</summary>
public sealed class InFlightImageConversions
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> inFlight = new();

    /// <summary>Runs work for key unless a run for key is already in flight, in which case it
    /// awaits that one. work runs detached from any single caller's cancellation — ct only stops
    /// this caller waiting — so one cancelled request can't fail the others sharing it. A key must
    /// always be used with the same T.</summary>
    public async Task<T> RunOnceAsync<T>(string key, Func<Task<T>> work, CancellationToken ct = default)
    {
        Lazy<Task>? entry = null;
        entry = new Lazy<Task>(() => RunAndRemoveAsync(key, work, entry!));
        var task = (Task<T>)inFlight.GetOrAdd(key, entry).Value;
        return await task.WaitAsync(ct);
    }

    private async Task<T> RunAndRemoveAsync<T>(string key, Func<Task<T>> work, Lazy<Task> entry)
    {
        try
        {
            return await work();
        }
        finally
        {
            inFlight.TryRemove(KeyValuePair.Create(key, entry));
        }
    }
}
