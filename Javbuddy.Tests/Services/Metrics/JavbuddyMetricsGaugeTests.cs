using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Javbuddy.Tests.Services.Metrics;

public class JavbuddyMetricsGaugeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LastBackupAge_NoBackup_EmitsNoMeasurement()
    {
        using var metrics = new JavbuddyMetrics(new FakeTimeProvider(Now));
        using var recorder = new MetricsRecorder(metrics);

        recorder.SampleGauges();

        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_last_backup_age_seconds");
    }

    [Fact]
    public void LastBackupAge_IsComputedAtScrapeTime()
    {
        var time = new FakeTimeProvider(Now);
        using var metrics = new JavbuddyMetrics(time);
        using var recorder = new MetricsRecorder(metrics);
        metrics.UpdateLastBackup(Now.UtcDateTime.AddHours(-2));

        recorder.SampleGauges();
        time.Advance(TimeSpan.FromMinutes(30));
        recorder.SampleGauges();

        var ages = recorder.Measurements.Where(m => m.InstrumentName == "javbuddy_last_backup_age_seconds").Select(m => (double)m.Value!).ToList();
        Assert.Equal([7200d, 9000d], ages);
    }

    [Fact]
    public void LastBackupAge_BackupRemoved_StopsEmitting()
    {
        using var metrics = new JavbuddyMetrics(new FakeTimeProvider(Now));
        using var recorder = new MetricsRecorder(metrics);
        metrics.UpdateLastBackup(Now.UtcDateTime.AddHours(-1));
        metrics.UpdateLastBackup(null);

        recorder.SampleGauges();

        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_last_backup_age_seconds");
    }

    [Fact]
    public void LastBackupAge_ScrapeCountsAsObserved()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);

        recorder.SampleGauges();

        Assert.True(metrics.WasObservedWithin(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ScheduledTaskLastSuccess_ReportsUnixSecondsPerTask_AndNeverMovesBackwards()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        metrics.RecordScheduledTaskSuccess("a", new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        metrics.RecordScheduledTaskSuccess("a", new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc)); // older seed: ignored
        metrics.RecordScheduledTaskSuccess("b", new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Unspecified));

        recorder.SampleGauges();

        var byTask = recorder.Measurements
            .Where(m => m.InstrumentName == "javbuddy_scheduled_task_last_success_timestamp_seconds")
            .ToDictionary(m => (string)m.Tags["task"]!, m => (double)m.Value!);
        Assert.Equal(2, byTask.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), byTask["a"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), byTask["b"]);
    }

    [Fact]
    public void FfmpegGateGauges_ReportActiveAndQueuedPerGate_AndNoQueueForUngated()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        metrics.FfmpegProcessStarted("none");
        metrics.FfmpegWaitStarted("short");
        metrics.FfmpegWaitStarted("short");
        metrics.FfmpegWaitEnded("short");

        recorder.SampleGauges();

        int Read(string instrument, string gate) =>
            (int)recorder.Measurements.Single(m => m.InstrumentName == instrument && (string)m.Tags["gate"]! == gate).Value!;
        Assert.Equal(1, Read("javbuddy_ffmpeg_active_processes", "none"));
        Assert.Equal(0, Read("javbuddy_ffmpeg_active_processes", "long"));
        Assert.Equal(1, Read("javbuddy_ffmpeg_gate_queue_depth", "short"));
        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_gate_queue_depth" && (string)m.Tags["gate"]! == "none");
    }

    [Fact]
    public void FfmpegHistograms_CarryExplicitBucketBoundaries()
    {
        using var metrics = new JavbuddyMetrics();
        var advised = new Dictionary<string, IReadOnlyList<double>?>();
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter == metrics.Meter && instrument is System.Diagnostics.Metrics.Histogram<double> h)
                    advised[h.Name] = h.Advice?.HistogramBucketBoundaries;
            }
        };
        listener.Start();

        Assert.Contains(0.05, advised["javbuddy_ffmpeg_duration_seconds"]!);
        Assert.Contains(7200d, advised["javbuddy_ffmpeg_duration_seconds"]!);
        Assert.Contains(14400d, advised["javbuddy_ffmpeg_gate_wait_seconds"]!);
    }
}
