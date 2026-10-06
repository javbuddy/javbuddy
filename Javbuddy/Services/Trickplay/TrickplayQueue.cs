using System.Collections.Concurrent;
using System.Threading.Channels;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.Trickplay;

/// <summary>Background trickplay generation, fed by the new-file trigger and the
/// backfill task. A single consumer works through it one movie at a time: each job decodes a whole
/// video, possibly 8K VR, so running them serially is the throttle (as for SceneMediaQueue). A movie
/// already queued isn't queued twice, and one whose generation failed isn't retried for the same
/// file identity until the app restarts or a manual rescan asks for it. While it has work, the sidebar's task indicator shows its
/// progress (TaskActivityTracker), since a library backfill can run for days: the current movie's
/// percentage and an estimate of the time left for the whole queue.</summary>
public sealed class TrickplayQueue(IServiceScopeFactory scopeFactory, TaskActivityTracker activities, TimeProvider timeProvider, ILogger<TrickplayQueue> logger) : BackgroundService
{
    private const string ActivityName = "Trickplay";

    /// <summary>How long the batch must have been generating before its decode speed is trusted for
    /// an estimate; ffmpeg's first seconds (opening a file on a network share) aren't representative.</summary>
    public static readonly TimeSpan MinimumSampleTime = TimeSpan.FromSeconds(30);

    private readonly Channel<Job> channel = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, byte> pending = new();
    private readonly ConcurrentDictionary<(int MovieId, string Identity), byte> failed = new();
    private readonly Lock gate = new();

    /// <summary>Total video duration of the queued jobs not yet started.</summary>
    private double waitingSeconds;

    private sealed record Job(int MovieId, string Code, string Identity, double? DurationSeconds);

    /// <summary>Queues a movie's generation; false when it's already queued or already failed for
    /// this identity. <paramref name="durationSeconds"/> is the main file's probed duration, for the
    /// queue's time-left estimate.</summary>
    public bool Enqueue(int movieId, string code, string identity, double? durationSeconds)
    {
        if (failed.ContainsKey((movieId, identity))) return false;
        if (!pending.TryAdd(movieId, 0)) return false;
        lock (gate)
        {
            waitingSeconds += durationSeconds ?? 0;
        }
        channel.Writer.TryWrite(new Job(movieId, code, identity, durationSeconds));
        return true;
    }

    public bool HasFailed(int movieId, string identity) => failed.ContainsKey((movieId, identity));

    /// <summary>Lets a failed generation be queued again, for a manual rescan.</summary>
    public void ForgetFailure(int movieId, string identity) => failed.TryRemove((movieId, identity), out _);

    /// <summary>Movies waiting or being generated.</summary>
    public int Count => pending.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long? activityId = null;
        int done = 0, generated = 0, failures = 0;
        // The batch's decode speed so far: the video seconds of its finished generations and the
        // time they took.
        double decodedSeconds = 0, decodingSeconds = 0;
        await foreach (var job in channel.Reader.ReadAllAsync(stoppingToken))
        {
            lock (gate)
            {
                waitingSeconds = Math.Max(0, waitingSeconds - (job.DurationSeconds ?? 0));
            }
            activityId ??= activities.Start(ActivityName, "Generating trickplay…");
            var id = activityId.Value;
            var started = timeProvider.GetTimestamp();
            string? lastMessage = null;

            // Called on ffmpeg's stderr reader, one report at a time, while this job runs.
            void Report(FfmpegProgress? progress)
            {
                var videoDone = progress?.Elapsed.TotalSeconds ?? 0;
                double waiting;
                lock (gate)
                {
                    waiting = waitingSeconds;
                }
                var remaining = EstimateRemaining(
                    decodedSeconds + videoDone,
                    decodingSeconds + timeProvider.GetElapsedTime(started).TotalSeconds,
                    Math.Max(0, (job.DurationSeconds ?? 0) - videoDone) + waiting);
                var message = ProgressMessage(done + 1, done + pending.Count, job.Code, progress?.PercentComplete, remaining);
                // ffmpeg reports several times a second; the sidebar only needs the visible changes.
                if (message == lastMessage) return;
                lastMessage = message;
                activities.Update(id, message);
            }

            Report(null);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ITrickplayGenerator>()
                    .GenerateAsync(job.MovieId, new ImmediateProgress<FfmpegProgress>(Report), stoppingToken);
                if (result == TrickplayGenerateResult.Generated)
                {
                    generated++;
                    if (job.DurationSeconds is { } duration)
                    {
                        decodedSeconds += duration;
                        decodingSeconds += timeProvider.GetElapsedTime(started).TotalSeconds;
                    }
                }
                else if (result is TrickplayGenerateResult.Failed or TrickplayGenerateResult.NoFile or TrickplayGenerateResult.NotProbed)
                {
                    failures++;
                    failed.TryAdd((job.MovieId, job.Identity), 0);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                failed.TryAdd((job.MovieId, job.Identity), 0);
                logger.LogWarning(ex, "Generating trickplay for {Code} failed", job.Code);
            }
            finally
            {
                pending.TryRemove(job.MovieId, out _);
                done++;
            }

            if (pending.IsEmpty)
            {
                var summary = $"Generated trickplay for {generated} of {done} {(done == 1 ? "movie" : "movies")}" + (failures > 0 ? $", {failures} failed" : "");
                activities.Complete(activityId.Value, summary, failed: generated == 0 && failures > 0);
                activityId = null;
                done = generated = failures = 0;
                decodedSeconds = decodingSeconds = 0;
            }
        }
    }

    /// <summary>The time left for <paramref name="remainingSeconds"/> of video at the batch's decode
    /// speed so far, or null until it has been generating for <see cref="MinimumSampleTime"/>.</summary>
    public static TimeSpan? EstimateRemaining(double decodedSeconds, double decodingSeconds, double remainingSeconds)
    {
        if (decodedSeconds <= 0 || decodingSeconds < MinimumSampleTime.TotalSeconds) return null;
        return TimeSpan.FromSeconds(remainingSeconds * decodingSeconds / decodedSeconds);
    }

    /// <summary>The sidebar line, e.g. "Generating trickplay 1 of 3: ABC-123 (42%, about 25 min left)".</summary>
    public static string ProgressMessage(int position, int total, string code, double? percent, TimeSpan? remaining)
    {
        var details = new List<string>(2);
        if (percent is { } p) details.Add($"{(int)p}%");
        if (remaining is { } r) details.Add(FormatRemaining(r));
        var message = $"Generating trickplay {position} of {total}: {code}";
        return details.Count == 0 ? message : $"{message} ({string.Join(", ", details)})";
    }

    /// <summary>Rounded to the minute, since the estimate is rough anyway; days once a backfill
    /// goes past one.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        var minutes = (int)Math.Round(remaining.TotalMinutes);
        if (minutes < 1) return "less than a minute left";
        if (minutes < 60) return $"about {minutes} min left";
        var hours = minutes / 60;
        if (hours < 24) return minutes % 60 == 0 ? $"about {hours} h left" : $"about {hours} h {minutes % 60} min left";
        return hours % 24 == 0 ? $"about {hours / 24} d left" : $"about {hours / 24} d {hours % 24} h left";
    }
}
