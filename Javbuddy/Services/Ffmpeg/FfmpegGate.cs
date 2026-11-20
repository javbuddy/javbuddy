namespace Javbuddy.Services.Ffmpeg;

/// <summary>Caps how many ffmpeg processes run at once in one lane. The scene-media, trickplay, VR-merge and
/// scene-detection jobs each have their own queue, so without a shared cap they all decode video together; a decode can
/// use every core and holds a lot of memory, so the extra processes only slow each other down. There are two lanes of
/// one slot each: <see cref="Long"/> for whole-file decodes and remuxes, <see cref="Short"/> for stills, hover previews
/// and highlight-clip trickplay, so those stay quick while an hours-long job runs. ffprobe is cheap and not gated, so
/// scanning keeps going too. The lanes are static because the client itself is scoped, so the cap has to live outside
/// it.</summary>
public sealed class FfmpegGate(int maxConcurrency, string name = "custom")
{
    public const string LongName = "long";
    public const string ShortName = "short";

    /// <summary>The gate label used for runs that go through no gate (ffprobe, the dts2pts repair).</summary>
    public const string UngatedName = "none";

    /// <summary>Low-cardinality label for this lane in metrics.</summary>
    public string Name { get; } = name;

    /// <summary>Whole-file work: full trickplay, scene detection, chapter remux and VR merge (the dts2pts repair is ungated).</summary>
    public static FfmpegGate Long { get; } = new(1, LongName);

    /// <summary>A few seconds of video: scene stills, hover previews and highlight-clip trickplay.</summary>
    public static FfmpegGate Short { get; } = new(1, ShortName);

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
