namespace Javbuddy.Services.Images;

/// <summary>Process-wide cap on how many ImageConverter decodes run at once. Each
/// conversion holds tens of MB of native SkiaSharp bitmaps, and a cold Movies grid can fire dozens
/// of cache misses together — unbounded, those stack up to GB of native memory. Conversions are
/// CPU-bound, so running more than one per core wouldn't finish the batch any sooner anyway.
/// Static because the memory it guards is the whole process's, whichever service converts.</summary>
public static class ImageConversionGate
{
    public static readonly int MaxConcurrency = Math.Clamp(Environment.ProcessorCount, 1, 4);

    private static readonly SemaphoreSlim Gate = new(MaxConcurrency, MaxConcurrency);

    /// <summary>Waits for a free conversion slot, then runs convert on the calling thread.
    /// convert must not itself call RunAsync (it would wait on a slot it's holding).</summary>
    public static async Task<T> RunAsync<T>(Func<T> convert, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return convert();
        }
        finally
        {
            Gate.Release();
        }
    }
}
