namespace Javbuddy.Services.Ffmpeg;

/// <summary>Caps how many ffmpeg processes run at once. The scene-media, trickplay, repair, VR-merge and scene-detection
/// jobs each have their own queue, so without a shared cap they all decode video together; a decode can use every core
/// and holds a lot of memory, so the extra processes only slow each other down. ffprobe is cheap and not gated, so
/// scanning keeps going while a long job runs. <see cref="Shared"/> is the one the app uses (the client itself is
/// scoped, so the cap has to live outside it).</summary>
public sealed class FfmpegGate(int maxConcurrency)
{
    /// <summary>Two at a time on a machine with four or more cores, otherwise one.</summary>
    public static readonly int DefaultMaxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);

    public static FfmpegGate Shared { get; } = new(DefaultMaxConcurrency);

    private readonly SemaphoreSlim slots = new(maxConcurrency, maxConcurrency);

    /// <summary>Waits for a free slot; dispose the result to give it back.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        return new Slot(slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) slots.Release();
        }
    }
}
