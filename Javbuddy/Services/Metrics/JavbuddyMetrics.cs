using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.Metrics;

/// <summary>App-specific instruments exposed alongside the built-in OpenTelemetry ASP.NET
/// Core/HttpClient/runtime instrumentation on the /metrics endpoint. Singleton
/// (see Program.cs) so every service that records a metric shares the same Meter/instrument
/// instances, same pattern as MovieChangeNotifier/TorrentChangeNotifier.
///
/// Counters are recorded directly by the service whose event they describe. Gauges are pull-based
/// (ObservableGauge, read only when something scrapes /metrics) but their underlying values are
/// push-updated: cheap in-memory counts (VR merge's active job count) are updated by whichever
/// service owns that state; DB-/filesystem-backed counts (image cache stats, library size) are
/// too expensive to compute on every scrape, so MetricsGaugeRefreshService updates them on a timer
/// instead and the gauge callbacks just read the latest cached value. A gauge with no value to report (no backup
/// exists yet) yields no measurement at all rather than a placeholder number.</summary>
public class JavbuddyMetrics : IDisposable
{
    public const string MeterName = "Javbuddy";

    /// <summary>How long after the last scrape the DB-/filesystem-backed gauges count as unread.</summary>
    public static readonly TimeSpan ObservationIdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Histogram buckets (seconds) for ffmpeg run durations, one set for every operation because a
    /// View/advice applies per instrument, not per tag value: sub-second for a probe or still up to hours for a
    /// full-file decode.</summary>
    private static readonly double[] FfmpegDurationBuckets = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 1200, 1800, 3600, 7200];

    /// <summary>Histogram buckets (seconds) for time spent queued on an ffmpeg gate: usually instant, but behind a
    /// whole-file decode it can be hours.</summary>
    private static readonly double[] FfmpegGateWaitBuckets = [0.001, 0.01, 0.1, 1, 5, 30, 60, 300, 900, 1800, 3600, 7200, 14400];

    /// <summary>Histogram buckets for ffmpeg's speed ratio (media seconds per wall second): well under realtime on a slow
    /// NAS mount up to hundreds of times for a stream copy or keyframe-only decode.</summary>
    private static readonly double[] FfmpegSpeedRatioBuckets = [0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];

    private sealed class FfmpegGateState
    {
        public int Active;
        public int Waiting;
    }

    private readonly Meter meter;
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<string, FfmpegGateState> ffmpegGates = new();
    private readonly ConcurrentDictionary<string, long> taskLastSuccessUnixSeconds = new();
    private long lastBackupUtcTicks;
    private int ffmpegBinaryAvailable = -1; // -1 = not determined yet
    private string? ffmpegVersion;
    private IReadOnlyDictionary<string, int> libraryCodecs = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, int> libraryResolutions = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, int> libraryContainers = new Dictionary<string, int>();

    private int vrMergeActiveJobs;
    private int imageCacheFiles;
    private long imageCacheBytes;
    private int moviesGot;
    private int moviesMissing;
    private int actorsTotal;
    private long lastObservedTimestamp;
    private readonly SemaphoreSlim scrapedAfterIdle = new(0);

    public JavbuddyMetrics(TimeProvider? time = null)
    {
        this.time = time ?? TimeProvider.System;
        meter = new Meter(MeterName);
        // Seeded so the gauges report 0 from the start rather than appearing on first use.
        foreach (var gate in new[] { FfmpegGate.LongName, FfmpegGate.ShortName, FfmpegGate.UngatedName })
            ffmpegGates[gate] = new FfmpegGateState();

        ScheduledTaskRuns = meter.CreateCounter<long>(
            "javbuddy_scheduled_task_runs_total", description: "Scheduled task executions, by task and outcome.");
        ScheduledTaskDurationSeconds = meter.CreateHistogram<double>(
            "javbuddy_scheduled_task_duration_seconds", unit: "s", description: "Scheduled task execution duration.");
        ImageCacheConversions = meter.CreateCounter<long>(
            "javbuddy_image_cache_conversions_total", description: "On-demand image cache conversions.");
        TorrentGrabs = meter.CreateCounter<long>(
            "javbuddy_torrent_grabs_total", description: "Torrent grab attempts, by outcome.");
        VrMergeJobs = meter.CreateCounter<long>(
            "javbuddy_vr_merge_jobs_total", description: "VR part-merge jobs, by outcome.");

        FfmpegRuns = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_runs_total", description: "ffmpeg/ffprobe process runs, by operation and outcome (success, failed, cancelled).");
        FfmpegDurationSeconds = meter.CreateHistogram<double>(
            "javbuddy_ffmpeg_duration_seconds", unit: "s", description: "ffmpeg/ffprobe run time from process start to exit, excluding time queued on a gate.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FfmpegDurationBuckets });
        FfmpegGateWaitSeconds = meter.CreateHistogram<double>(
            "javbuddy_ffmpeg_gate_wait_seconds", unit: "s", description: "Time spent waiting for a free slot on an ffmpeg gate.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FfmpegGateWaitBuckets });
        FfmpegInputMediaSeconds = meter.CreateCounter<double>(
            "javbuddy_ffmpeg_input_media_seconds_total", unit: "s",
            description: "Seconds of media covered by successful ffmpeg runs whose input duration is known (not decoded time; a keyframe-only run counts the whole video).");

        FfmpegOutputBytes = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_output_bytes_total", unit: "By",
            description: "Bytes written by successful ffmpeg runs (output file, or the files in the trickplay output directory), measured after the run.");
        FfmpegInputBytes = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_input_bytes_total", unit: "By",
            description: "Size of the input file(s) of successful ffmpeg runs (not ffprobe, which reads only headers), i.e. the most they read from the library (a seek or keyframe-only run reads less).");
        FfmpegSpeedRatio = meter.CreateHistogram<double>(
            "javbuddy_ffmpeg_speed_ratio", unit: "{ratio}",
            description: "Media seconds processed per wall-clock second, for successful runs whose input duration is known. Low values point at slow disks or NAS mounts.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FfmpegSpeedRatioBuckets });
        FfmpegFailures = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_failures_total",
            description: "Failed ffmpeg/ffprobe runs, by operation and reason bucketed from stderr (invalid_data, no_such_file, moov_atom_missing, permission, other).");
        FfmpegKills = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_kills_total", description: "ffmpeg/ffprobe process trees killed, by operation (a cancelled run kills its process).");
        FfmpegPriorityDowngradeFailures = meter.CreateCounter<long>(
            "javbuddy_ffmpeg_priority_downgrade_failures_total", description: "ffmpeg/ffprobe processes that could not be lowered to below-normal priority and ran at normal priority.");

        meter.CreateObservableGauge("javbuddy_ffmpeg_binary_available", ObserveFfmpegBinaryAvailable,
            description: "1 when both ffmpeg and ffprobe were found, else 0.");
        meter.CreateObservableGauge("javbuddy_ffmpeg_binary_info", ObserveFfmpegBinaryInfo,
            description: "Constant 1, labelled with the ffmpeg version. Independent of ffprobe. Absent when ffmpeg is missing or its version could not be read.");
        meter.CreateObservableGauge("javbuddy_library_video_codec_total", () => ObserveComposition(() => libraryCodecs, "codec"),
            description: "Library video files, by normalized video codec.");
        meter.CreateObservableGauge("javbuddy_library_video_resolution_total", () => ObserveComposition(() => libraryResolutions, "resolution_bucket"),
            description: "Library video files, by resolution bucket (from frame width).");
        meter.CreateObservableGauge("javbuddy_library_video_container_total", () => ObserveComposition(() => libraryContainers, "container"),
            description: "Library video files, by normalized container.");

        meter.CreateObservableGauge("javbuddy_ffmpeg_active_processes", () => ObserveFfmpegGates(g => Volatile.Read(ref g.Active)),
            description: "ffmpeg/ffprobe processes currently running, by gate (none = not gated).");
        meter.CreateObservableGauge("javbuddy_ffmpeg_gate_queue_depth", () => ObserveFfmpegGates(g => Volatile.Read(ref g.Waiting), includeUngated: false),
            description: "Callers waiting for a free slot on an ffmpeg gate. Persistently above zero means ffmpeg is the bottleneck.");
        meter.CreateObservableGauge("javbuddy_last_backup_age_seconds", ObserveLastBackupAge, unit: "s",
            description: "Seconds since the newest database backup (automatic or manual) was made. Absent when there is no backup.");
        meter.CreateObservableGauge("javbuddy_scheduled_task_last_success_timestamp_seconds", ObserveTaskLastSuccess, unit: "s",
            description: "Unix time of each scheduled task's last successful run. Absent for a task that has never succeeded.");

        meter.CreateObservableGauge("javbuddy_vr_merge_active_jobs",
            () => Volatile.Read(ref vrMergeActiveJobs), description: "VR part-merge jobs currently running.");
        meter.CreateObservableGauge("javbuddy_image_cache_files",
            () => { MarkObserved(); return Volatile.Read(ref imageCacheFiles); }, description: "Files in the local image cache.");
        meter.CreateObservableGauge("javbuddy_image_cache_bytes",
            () => { MarkObserved(); return Volatile.Read(ref imageCacheBytes); }, unit: "By", description: "Total size of the local image cache.");
        meter.CreateObservableGauge("javbuddy_movies_total", ObserveMoviesTotal, description: "Tracked movies, by status.");
        meter.CreateObservableGauge("javbuddy_actors_total",
            () => { MarkObserved(); return Volatile.Read(ref actorsTotal); }, description: "Tracked actors.");
    }

    /// <summary>Exposed so tests can scope a MeterListener to exactly this instance's instruments
    /// (see Javbuddy.Tests' MetricsRecorder) — several JavbuddyMetrics instances can coexist in one
    /// test process, all sharing the "Javbuddy" meter name.</summary>
    public Meter Meter => meter;

    public Counter<long> ScheduledTaskRuns { get; }
    public Histogram<double> ScheduledTaskDurationSeconds { get; }
    public Counter<long> ImageCacheConversions { get; }
    public Counter<long> TorrentGrabs { get; }
    public Counter<long> VrMergeJobs { get; }
    public Counter<long> FfmpegRuns { get; }
    public Histogram<double> FfmpegDurationSeconds { get; }
    public Histogram<double> FfmpegGateWaitSeconds { get; }
    public Counter<double> FfmpegInputMediaSeconds { get; }
    public Counter<long> FfmpegOutputBytes { get; }
    public Counter<long> FfmpegInputBytes { get; }
    public Histogram<double> FfmpegSpeedRatio { get; }
    public Counter<long> FfmpegFailures { get; }
    public Counter<long> FfmpegKills { get; }
    public Counter<long> FfmpegPriorityDowngradeFailures { get; }

    public void FfmpegWaitStarted(string gate) => Interlocked.Increment(ref GateState(gate).Waiting);

    public void FfmpegWaitEnded(string gate) => Interlocked.Decrement(ref GateState(gate).Waiting);

    public void FfmpegProcessStarted(string gate) => Interlocked.Increment(ref GateState(gate).Active);

    public void FfmpegProcessEnded(string gate) => Interlocked.Decrement(ref GateState(gate).Active);

    private FfmpegGateState GateState(string gate) => ffmpegGates.GetOrAdd(gate, _ => new FfmpegGateState());

    /// <summary>Records the creation time of the newest backup, or null when there is none. The age is worked out
    /// at scrape time from this, so the gauge stays current between MetricsGaugeRefreshService passes.</summary>
    public void UpdateLastBackup(DateTime? createdUtc) =>
        Interlocked.Exchange(ref lastBackupUtcTicks, createdUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc).Ticks : 0);

    /// <summary>Notes a successful scheduled task run. Keeps the later of the recorded and given times, so seeding
    /// from persisted history can never move a task's timestamp backwards.</summary>
    public void RecordScheduledTaskSuccess(string task, DateTime whenUtc)
    {
        var unix = new DateTimeOffset(DateTime.SpecifyKind(whenUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        taskLastSuccessUnixSeconds.AddOrUpdate(task, unix, (_, existing) => Math.Max(existing, unix));
    }

    /// <summary>Records whether both ffmpeg and ffprobe were found and, when known, the ffmpeg version (independent of
    /// ffprobe's presence). The resolver caches its result, so only the version read can need a retry.</summary>
    public void UpdateFfmpegBinary(bool available, string? version)
    {
        Volatile.Write(ref ffmpegVersion, version);
        Volatile.Write(ref ffmpegBinaryAvailable, available ? 1 : 0);
    }

    /// <summary>Replaces the library composition gauges' values (label value to file count).</summary>
    public void UpdateLibraryComposition(IReadOnlyDictionary<string, int> codecs, IReadOnlyDictionary<string, int> resolutions, IReadOnlyDictionary<string, int> containers)
    {
        Volatile.Write(ref libraryCodecs, codecs);
        Volatile.Write(ref libraryResolutions, resolutions);
        Volatile.Write(ref libraryContainers, containers);
    }

    public void VrMergeJobStarted() => Interlocked.Increment(ref vrMergeActiveJobs);

    public void VrMergeJobEnded() => Interlocked.Decrement(ref vrMergeActiveJobs);

    public void UpdateImageCacheStats(int fileCount, long totalBytes)
    {
        Volatile.Write(ref imageCacheFiles, fileCount);
        Volatile.Write(ref imageCacheBytes, totalBytes);
    }

    public void UpdateLibraryStats(int got, int missing, int actorCount)
    {
        Volatile.Write(ref moviesGot, got);
        Volatile.Write(ref moviesMissing, missing);
        Volatile.Write(ref actorsTotal, actorCount);
    }

    /// <summary>True when something scraped a DB-/filesystem-backed gauge within <paramref name="window"/>. Lets
    /// MetricsGaugeRefreshService skip its expensive refresh while nothing is reading /metrics.</summary>
    public bool WasObservedWithin(TimeSpan window)
    {
        var last = Interlocked.Read(ref lastObservedTimestamp);
        return last != 0 && Stopwatch.GetElapsedTime(last) <= window;
    }

    /// <summary>Waits up to <paramref name="timeout"/> for the first scrape after the gauges went unread for
    /// ObservationIdleAfter (or were never read), so the refresh can start then rather than at its next tick.
    /// True when such a scrape arrived.</summary>
    public Task<bool> WaitForScrapeAfterIdleAsync(TimeSpan timeout, CancellationToken ct) =>
        scrapedAfterIdle.WaitAsync(timeout, ct);

    private void MarkObserved()
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Exchange(ref lastObservedTimestamp, now);
        // One scrape reads several gauges; only the first sees the old timestamp, so one wake per idle stretch.
        if (previous == 0 || Stopwatch.GetElapsedTime(previous, now) > ObservationIdleAfter)
            scrapedAfterIdle.Release();
    }

    private IEnumerable<Measurement<int>> ObserveMoviesTotal()
    {
        MarkObserved();
        yield return new Measurement<int>(Volatile.Read(ref moviesGot), new KeyValuePair<string, object?>("status", "Got"));
        yield return new Measurement<int>(Volatile.Read(ref moviesMissing), new KeyValuePair<string, object?>("status", "Missing"));
    }

    private IEnumerable<Measurement<int>> ObserveFfmpegBinaryAvailable()
    {
        var available = Volatile.Read(ref ffmpegBinaryAvailable);
        if (available >= 0) yield return new Measurement<int>(available);
    }

    private IEnumerable<Measurement<int>> ObserveFfmpegBinaryInfo()
    {
        if (Volatile.Read(ref ffmpegVersion) is { } version)
            yield return new Measurement<int>(1, new KeyValuePair<string, object?>("version", version));
    }

    private IEnumerable<Measurement<int>> ObserveComposition(Func<IReadOnlyDictionary<string, int>> read, string label)
    {
        MarkObserved();
        foreach (var (value, count) in read())
            yield return new Measurement<int>(count, new KeyValuePair<string, object?>(label, value));
    }

    private IEnumerable<Measurement<int>> ObserveFfmpegGates(Func<FfmpegGateState, int> read, bool includeUngated = true)
    {
        foreach (var (name, state) in ffmpegGates)
        {
            if (!includeUngated && name == FfmpegGate.UngatedName) continue;
            yield return new Measurement<int>(read(state), new KeyValuePair<string, object?>("gate", name));
        }
    }

    private IEnumerable<Measurement<double>> ObserveLastBackupAge()
    {
        MarkObserved();
        var ticks = Interlocked.Read(ref lastBackupUtcTicks);
        if (ticks == 0) yield break;
        yield return new Measurement<double>(Math.Max(0, (time.GetUtcNow().UtcDateTime - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds));
    }

    private IEnumerable<Measurement<double>> ObserveTaskLastSuccess()
    {
        foreach (var (task, unix) in taskLastSuccessUnixSeconds)
            yield return new Measurement<double>(unix, new KeyValuePair<string, object?>("task", task));
    }

    public void Dispose()
    {
        meter.Dispose();
        scrapedAfterIdle.Dispose();
    }
}
