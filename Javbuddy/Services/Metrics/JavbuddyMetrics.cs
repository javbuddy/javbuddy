using System.Diagnostics.Metrics;

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
/// instead and the gauge callbacks just read the latest cached value.</summary>
public class JavbuddyMetrics : IDisposable
{
    public const string MeterName = "Javbuddy";

    private readonly Meter meter;

    private int vrMergeActiveJobs;
    private int imageCacheFiles;
    private long imageCacheBytes;
    private int moviesGot;
    private int moviesMissing;
    private int actorsTotal;

    public JavbuddyMetrics()
    {
        meter = new Meter(MeterName);

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

        meter.CreateObservableGauge("javbuddy_vr_merge_active_jobs",
            () => Volatile.Read(ref vrMergeActiveJobs), description: "VR part-merge jobs currently running.");
        meter.CreateObservableGauge("javbuddy_image_cache_files",
            () => Volatile.Read(ref imageCacheFiles), description: "Files in the local image cache.");
        meter.CreateObservableGauge("javbuddy_image_cache_bytes",
            () => Volatile.Read(ref imageCacheBytes), unit: "By", description: "Total size of the local image cache.");
        meter.CreateObservableGauge("javbuddy_movies_total", ObserveMoviesTotal, description: "Tracked movies, by status.");
        meter.CreateObservableGauge("javbuddy_actors_total",
            () => Volatile.Read(ref actorsTotal), description: "Tracked actors.");
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

    private IEnumerable<Measurement<int>> ObserveMoviesTotal()
    {
        yield return new Measurement<int>(Volatile.Read(ref moviesGot), new KeyValuePair<string, object?>("status", "Got"));
        yield return new Measurement<int>(Volatile.Read(ref moviesMissing), new KeyValuePair<string, object?>("status", "Missing"));
    }

    public void Dispose() => meter.Dispose();
}
