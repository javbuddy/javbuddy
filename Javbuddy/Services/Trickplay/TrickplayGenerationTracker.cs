using System.Collections.Concurrent;
using Javbuddy.Services.SceneMedia;

namespace Javbuddy.Services.Trickplay;

/// <summary>Which trickplay set: a movie's whole main file, or (with Clip) a highlight's own set for
/// that range of it, keyed like its media by HighlightMediaService.Window.</summary>
public readonly record struct TrickplayTarget(int MovieId, SceneMediaWindow? Clip = null)
{
    public static TrickplayTarget ForHighlight(int movieId, double startSeconds, double endSeconds) =>
        new(movieId, HighlightMediaService.Window(startSeconds, endSeconds));
}

/// <summary>A set being generated, and how far ffmpeg is through it: whole percent, null until it
/// first reports.</summary>
public sealed record TrickplayGenerating(int? Percent);

/// <summary>The trickplay sets being generated right now and their progress, so a
/// player's scrub-bar preview can show it and pick the set up once it's written. Only running jobs
/// are tracked, not queued ones. TrickplayGenerator reports every run, movie or highlight.</summary>
public sealed class TrickplayGenerationTracker
{
    private readonly ConcurrentDictionary<TrickplayTarget, TrickplayGenerating> running = new();

    /// <summary>Raised, on the generating thread, when a set starts, moves to another whole percent,
    /// or finishes (written or not).</summary>
    public event Action<TrickplayTarget>? Changed;

    /// <summary>Null when the set isn't being generated.</summary>
    public TrickplayGenerating? Get(TrickplayTarget target) => running.GetValueOrDefault(target);

    /// <summary>Tracks the target until the returned run is disposed.</summary>
    public Run Start(TrickplayTarget target)
    {
        running[target] = new TrickplayGenerating(null);
        Changed?.Invoke(target);
        return new Run(this, target);
    }

    public sealed class Run(TrickplayGenerationTracker tracker, TrickplayTarget target) : IDisposable
    {
        // A late report from ffmpeg's stderr reader mustn't bring a finished run back.
        private readonly Lock gate = new();
        private bool disposed;
        private int? percent;

        public void Report(double percentComplete)
        {
            var whole = (int)Math.Clamp(Math.Floor(percentComplete), 0, 100);
            lock (gate)
            {
                if (disposed || whole == percent) return;
                percent = whole;
                tracker.running[target] = new TrickplayGenerating(whole);
            }
            tracker.Changed?.Invoke(target);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                tracker.running.TryRemove(target, out _);
            }
            tracker.Changed?.Invoke(target);
        }
    }
}
