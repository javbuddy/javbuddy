namespace Javbuddy.Services.Images;

/// <summary>Running estimate of the image cache's size on disk, so enforcing the
/// size limit doesn't walk the whole cache directory after every conversion. A directory scan sets
/// the baseline and writers add what they wrote; the estimate only ever errs high (overwrites,
/// deletions outside eviction), which costs at most an early rescan that corrects it. Singleton,
/// since conversions report into it from different scopes.</summary>
public sealed class ImageCacheSizeTracker
{
    private long trackedBytes;
    private int hasBaseline;

    /// <summary>Held by every scan that rebases the estimate. Two overlapping rebases would each
    /// subtract the same evictions, and concurrent conversions that cross the limit together should
    /// run one eviction pass rather than race each other over the same oldest files.</summary>
    public SemaphoreSlim ScanGate { get; } = new(1, 1);

    public bool HasBaseline => Volatile.Read(ref hasBaseline) == 1;

    public long TrackedBytes => Interlocked.Read(ref trackedBytes);

    /// <summary>Records bytes written to the cache and returns the new estimate.</summary>
    public long Add(long bytes) => Interlocked.Add(ref trackedBytes, bytes);

    /// <summary>Replaces the estimate with a scan's result. snapshot is TrackedBytes read before the
    /// scan started, so anything added while it ran is kept rather than lost: a file the scan
    /// missed is still counted, and one it saw is counted twice (erring high).</summary>
    public void Rebase(long snapshot, long scannedBytes)
    {
        Interlocked.Add(ref trackedBytes, scannedBytes - snapshot);
        Volatile.Write(ref hasBaseline, 1);
    }

    /// <summary>Drops the baseline so the next enforcement rescans the directory.</summary>
    public void Invalidate() => Volatile.Write(ref hasBaseline, 0);
}
