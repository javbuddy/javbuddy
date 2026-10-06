using System.Collections.Concurrent;
using System.Threading.Channels;
using Javbuddy.Services.Trickplay;

namespace Javbuddy.Services.SceneMedia;

/// <summary>Which media a queued job is for: a scene's or highlight's screenshot and
/// preview, a highlight's own trickplay set, or an apex's preview.</summary>
public enum MediaKind { Scene, Highlight, HighlightTrickplay, Apex }

/// <summary>Background generation of scene and highlight screenshots and hover previews, and of highlights' own trickplay sets. A single consumer works through the queue one scene or highlight at a time: each job is an ffmpeg decode of a
/// possibly 8K VR file, so running them serially is the throttle (the same concern as
/// ImageConversionGate,, but for ffmpeg processes rather than SkiaSharp bitmaps).
/// A job already queued isn't queued twice, and one whose generation failed (e.g. its file isn't
/// reachable locally) isn't retried for the same range until the app restarts; both are tracked per
/// kind, so scene 5 and highlight 5 are separate jobs. A job for a movie whose scenes or highlights
/// were edited less than SettleDelay ago is put back until the layout has
/// settled, so laying out scenes doesn't generate media for every intermediate
/// range; generation reads the ranges when it finally runs. Apexes are single moments with no layout
/// to settle, so their jobs run straight away.</summary>
public sealed class SceneMediaQueue(IServiceScopeFactory scopeFactory, ILogger<SceneMediaQueue> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly Channel<(MediaKind Kind, int Id)> channel = Channel.CreateUnbounded<(MediaKind, int)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<(MediaKind Kind, int Id), int> pending = new();
    private readonly ConcurrentDictionary<(MediaKind Kind, int Id, SceneMediaWindow Window), byte> failed = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> lastSceneChange = new();

    /// <summary>How long a movie's scenes must stay unchanged before its media is generated.</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Raised with the movie id after a scene's or highlight's media, or a highlight's
    /// trickplay, was (re)generated.</summary>
    public event Action<int>? Generated;

    public void Enqueue(int sceneId, int movieId, SceneMediaWindow window) => Enqueue((MediaKind.Scene, sceneId), movieId, window);

    public void EnqueueHighlight(int highlightId, int movieId, SceneMediaWindow window) => Enqueue((MediaKind.Highlight, highlightId), movieId, window);

    public void EnqueueApex(int apexId, int movieId, SceneMediaWindow window) => Enqueue((MediaKind.Apex, apexId), movieId, window);

    /// <summary>False when it's already queued or failed before.</summary>
    public bool EnqueueHighlightTrickplay(int highlightId, int movieId, SceneMediaWindow window) =>
        Enqueue((MediaKind.HighlightTrickplay, highlightId), movieId, window);

    private bool Enqueue((MediaKind Kind, int Id) job, int movieId, SceneMediaWindow window)
    {
        if (failed.ContainsKey((job.Kind, job.Id, window)) || !pending.TryAdd(job, movieId)) return false;
        channel.Writer.TryWrite(job);
        return true;
    }

    public void MarkFailed(int sceneId, SceneMediaWindow window) => failed.TryAdd((MediaKind.Scene, sceneId, window), 0);

    public void MarkHighlightFailed(int highlightId, SceneMediaWindow window) => failed.TryAdd((MediaKind.Highlight, highlightId, window), 0);

    public void MarkApexFailed(int apexId, SceneMediaWindow window) => failed.TryAdd((MediaKind.Apex, apexId, window), 0);

    public void MarkHighlightTrickplayFailed(int highlightId, SceneMediaWindow window) =>
        failed.TryAdd((MediaKind.HighlightTrickplay, highlightId, window), 0);

    public void NoteScenesChanged(int movieId) => lastSceneChange[movieId] = time.GetUtcNow();

    /// <summary>Time left until the movie's scenes count as settled; zero when they already do.</summary>
    private TimeSpan SettleRemaining(int movieId)
    {
        if (!lastSceneChange.TryGetValue(movieId, out var changedAt)) return TimeSpan.Zero;

        var remaining = changedAt + SettleDelay - time.GetUtcNow();
        if (remaining > TimeSpan.Zero) return remaining;

        // Settled: forget it, unless another edit landed in the meantime.
        lastSceneChange.TryRemove(new KeyValuePair<int, DateTimeOffset>(movieId, changedAt));
        return TimeSpan.Zero;
    }

    private async Task RequeueAfterAsync((MediaKind Kind, int Id) job, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, time, ct);
            channel.Writer.TryWrite(job);
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in channel.Reader.ReadAllAsync(stoppingToken))
        {
            // Still pending while deferred, so a repeat request doesn't queue it a second time.
            if (job.Kind != MediaKind.Apex && pending.TryGetValue(job, out var queuedMovieId) && SettleRemaining(queuedMovieId) is { } wait && wait > TimeSpan.Zero)
            {
                _ = RequeueAfterAsync(job, wait, stoppingToken);
                continue;
            }

            pending.TryRemove(job, out _);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var movieId = job.Kind switch
                {
                    MediaKind.Scene => await scope.ServiceProvider.GetRequiredService<ISceneMediaService>().GenerateAsync(job.Id, stoppingToken),
                    MediaKind.Highlight => await scope.ServiceProvider.GetRequiredService<IHighlightMediaService>().GenerateAsync(job.Id, stoppingToken),
                    MediaKind.Apex => await scope.ServiceProvider.GetRequiredService<IApexMediaService>().GenerateAsync(job.Id, stoppingToken),
                    _ => await scope.ServiceProvider.GetRequiredService<IHighlightTrickplayService>().GenerateAsync(job.Id, stoppingToken),
                };
                if (movieId is { } id)
                {
                    Generated?.Invoke(id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Generating media for {Kind} {Id} failed", job.Kind, job.Id);
            }
        }
    }
}
