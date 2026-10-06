namespace Javbuddy.Services.MediaInfo;

/// <summary>Bounds the native MediaInfo probes actually running. A probe call can't be cancelled, so a caller
/// that times out only stops waiting; the native call keeps its thread until it returns, or forever on a
/// corrupt file. The gate keeps a probe's slot until the native call itself returns (not until the caller
/// gives up), so hung probes can't accumulate without limit, and a retry of a file whose probe is still
/// running joins it instead of starting another. Singleton: it must outlive a probing scope.</summary>
public sealed class MediaProbeGate(int maxOutstanding = MediaProbeGate.DefaultMaxOutstanding, ILogger<MediaProbeGate>? logger = null)
{
    /// <summary>Above a scan's own concurrency (4 in LibraryRescanTask) so on-demand rescans still run beside it.</summary>
    public const int DefaultMaxOutstanding = 8;

    private readonly SemaphoreSlim slots = new(maxOutstanding, maxOutstanding);
    private readonly Dictionary<string, Task<MediaProbeResult>> running = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>Native probes currently holding a slot, including any abandoned by a timed-out caller.</summary>
    public int Outstanding => maxOutstanding - slots.CurrentCount;

    public async Task<MediaProbeResult> RunAsync(string key, Func<MediaProbeResult> work, TimeSpan timeout, CancellationToken ct)
    {
        var task = TryJoin(key);
        if (task is null)
        {
            if (!await slots.WaitAsync(timeout, ct))
            {
                logger?.LogWarning("MediaInfo probe of {Path} not started: {Outstanding} native probes are still running", key, Outstanding);
                return MediaProbeResult.Failed($"Too many MediaInfo probes are still running ({Outstanding}); a corrupt file or an unresponsive share may be holding them. Retry later.");
            }

            lock (gate)
            {
                if (running.TryGetValue(key, out var joined))
                {
                    slots.Release(); // another caller started this file's probe while this one waited for a slot
                    task = joined;
                }
                else
                {
                    task = Task.Run(() =>
                    {
                        try
                        {
                            return work();
                        }
                        finally
                        {
                            lock (gate)
                            {
                                running.Remove(key);
                            }
                            slots.Release();
                        }
                    });
                    running[key] = task;
                }
            }
        }

        var result = await MediaInfoProber.RaceWithTimeoutAsync(task, timeout, ct);
        if (!task.IsCompleted)
        {
            logger?.LogWarning("MediaInfo probe of {Path} timed out; the native call is still running ({Outstanding} outstanding)", key, Outstanding);
        }
        return result;
    }

    private Task<MediaProbeResult>? TryJoin(string key)
    {
        lock (gate)
        {
            return running.GetValueOrDefault(key);
        }
    }
}
