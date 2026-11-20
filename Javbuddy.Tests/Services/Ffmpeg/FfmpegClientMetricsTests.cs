using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Ffmpeg;

/// <summary>Runs the client against tiny shell scripts standing in for ffmpeg/ffprobe (built in a temp dir, so no
/// real ffmpeg is needed), which is enough to exercise the real process lifecycle and its metrics. POSIX only.</summary>
public sealed class FfmpegClientMetricsTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-metrics-");
    private readonly JavbuddyMetrics metrics = new();
    private readonly MetricsRecorder recorder;

    public FfmpegClientMetricsTests() => recorder = new MetricsRecorder(metrics);

    public void Dispose()
    {
        recorder.Dispose();
        metrics.Dispose();
        dir.Delete(recursive: true);
    }

    private string Script(string name, string body)
    {
        var path = Path.Combine(dir.FullName, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private FfmpegClient Client(string? ffmpeg, string? ffprobe = null, FfmpegGate? longGate = null, FfmpegGate? shortGate = null)
    {
        var resolver = Substitute.For<IFfmpegBinaryResolver>();
        resolver.FfmpegPath.Returns(ffmpeg);
        resolver.FfprobePath.Returns(ffprobe);
        return new FfmpegClient(resolver, longGate, shortGate, metrics);
    }

    /// <summary>Samples the gauges until <paramref name="condition"/> holds (or fails after a timeout), instead of a fixed sleep.</summary>
    private async Task SampleUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            recorder.SampleGauges();
            if (condition()) return;
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the gauge to reach the expected value.");
            await Task.Delay(20);
        }
    }

    private IEnumerable<MetricsRecorder.Measurement> Of(string instrument) => recorder.Measurements.Where(m => m.InstrumentName == instrument);

    private MetricsRecorder.Measurement Run(string operation, string outcome) =>
        Assert.Single(Of("javbuddy_ffmpeg_runs_total"), m => (string)m.Tags["operation"]! == operation && (string)m.Tags["outcome"]! == outcome);

    [Fact]
    public async Task Probe_Success_CountsARunWithADuration_AndNoMediaSeconds()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffprobe = Script("ffprobe", "echo '{\"format\":{\"duration\":\"120\"}}'");

        var info = await Client(null, ffprobe).ProbeAsync("x.mp4");

        Assert.NotNull(info);
        Assert.Equal(1L, Run("probe", "success").Value);
        var duration = Assert.Single(Of("javbuddy_ffmpeg_duration_seconds"));
        Assert.Equal("probe", duration.Tags["operation"]);
        Assert.Empty(Of("javbuddy_ffmpeg_input_media_seconds_total"));
        Assert.Empty(Of("javbuddy_ffmpeg_gate_wait_seconds")); // ffprobe is not gated
    }

    [Fact]
    public async Task Probe_NonZeroExit_CountsAFailedRun()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffprobe = Script("ffprobe", "exit 1");

        Assert.Null(await Client(null, ffprobe).ProbeAsync("x.mp4"));

        Assert.Equal(1L, Run("probe", "failed").Value);
    }

    [Fact]
    public async Task Probe_BinaryMissing_RecordsNothing()
    {
        Assert.Null(await Client(null, null).ProbeAsync("x.mp4"));

        Assert.Empty(Of("javbuddy_ffmpeg_runs_total"));
    }

    [Fact]
    public async Task Still_FailedRun_IsCountedOnTheShortGate_WithoutMediaSeconds()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "echo oops >&2; exit 1");

        var result = await Client(ffmpeg).ExtractStillWebpAsync("in.mp4", 5, false, "out.webp");

        Assert.False(result.Success);
        Assert.Equal(1L, Run("still", "failed").Value);
        var wait = Assert.Single(Of("javbuddy_ffmpeg_gate_wait_seconds"));
        Assert.Equal("short", wait.Tags["gate"]);
        Assert.Empty(Of("javbuddy_ffmpeg_input_media_seconds_total"));
    }

    [Fact]
    public async Task Dts2Pts_Success_AddsTheInputDurationAsMediaSeconds_UngatedLabel()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "exit 0");

        var result = await Client(ffmpeg).RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null);

        Assert.True(result.Success);
        Assert.Equal(1L, Run("remux_dts2pts", "success").Value);
        var media = Assert.Single(Of("javbuddy_ffmpeg_input_media_seconds_total"));
        Assert.Equal(90d, media.Value);
        Assert.Equal("remux_dts2pts", media.Tags["operation"]);
        Assert.Empty(Of("javbuddy_ffmpeg_gate_wait_seconds"));
    }

    [Fact]
    public async Task Dts2Pts_FailedRun_AddsNoMediaSeconds()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "exit 1");

        await Client(ffmpeg).RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null);

        Assert.Equal(1L, Run("remux_dts2pts", "failed").Value);
        Assert.Empty(Of("javbuddy_ffmpeg_input_media_seconds_total"));
    }

    [Fact]
    public async Task SceneDetect_NonZeroExit_IsFailed_AndSuccessUsesTheLongGate()
    {
        if (OperatingSystem.IsWindows()) return;
        var okClient = Client(Script("ffmpeg-ok", "exit 0"));
        var failClient = Client(Script("ffmpeg-fail", "exit 1"));

        Assert.NotNull(await okClient.DetectSceneSignalsAsync("in.mp4", TimeSpan.FromMinutes(2), null));
        Assert.Null(await failClient.DetectSceneSignalsAsync("in.mp4", TimeSpan.FromMinutes(2), null));

        Assert.Equal(1L, Run("scene_detect", "success").Value);
        Assert.Equal(1L, Run("scene_detect", "failed").Value);
        Assert.All(Of("javbuddy_ffmpeg_gate_wait_seconds"), m => Assert.Equal("long", m.Tags["gate"]));
        Assert.Equal(120d, Assert.Single(Of("javbuddy_ffmpeg_input_media_seconds_total")).Value);
    }

    [Fact]
    public async Task TrickplayClip_UsesTheShortGate_AndCountsOnlyTheClipLength()
    {
        if (OperatingSystem.IsWindows()) return;
        var client = Client(Script("ffmpeg", "exit 0"));
        var request = new FfmpegTrickplayRequest(2, 320, 180, 5, 5, false, false, StartSeconds: 30, LengthSeconds: 10);

        var result = await client.GenerateTrickplayAsync("in.mp4", request, dir.FullName, TimeSpan.FromHours(1), null);

        Assert.True(result.Success);
        Assert.Equal(1L, Run("trickplay", "success").Value);
        Assert.Equal("short", Assert.Single(Of("javbuddy_ffmpeg_gate_wait_seconds")).Tags["gate"]);
        Assert.Equal(10d, Assert.Single(Of("javbuddy_ffmpeg_input_media_seconds_total")).Value);
    }

    [Fact]
    public async Task CancelledRun_IsCountedAsCancelled_AndFreesTheActiveGauge()
    {
        if (OperatingSystem.IsWindows()) return;
        var client = Client(Script("ffmpeg", "exec sleep 30"));
        using var cts = new CancellationTokenSource();

        var run = client.RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null, cts.Token);
        await SampleUntilAsync(() => Of("javbuddy_ffmpeg_active_processes").Any(m => (string)m.Tags["gate"]! == "none" && (int)m.Value! == 1));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(1L, Run("remux_dts2pts", "cancelled").Value);
        Assert.Empty(Of("javbuddy_ffmpeg_input_media_seconds_total"));
        var before = recorder.Measurements.Count;
        recorder.SampleGauges();
        Assert.All(recorder.Measurements.Skip(before).Where(m => m.InstrumentName == "javbuddy_ffmpeg_active_processes"), m => Assert.Equal(0, m.Value));
    }

    [Fact]
    public async Task QueuedRun_ShowsInTheQueueDepthGauge_ThenRecordsItsWait()
    {
        if (OperatingSystem.IsWindows()) return;
        var gate = new FfmpegGate(1, "test-gate");
        var client = Client(Script("ffmpeg", "exit 0"), shortGate: gate);
        var held = await gate.EnterAsync(CancellationToken.None);

        var queued = client.ExtractStillWebpAsync("in.mp4", 5, false, "out.webp");
        await SampleUntilAsync(() => Of("javbuddy_ffmpeg_gate_queue_depth").Any(m => (string)m.Tags["gate"]! == "test-gate" && (int)m.Value! == 1));

        held.Dispose();
        Assert.True((await queued).Success);

        var wait = Assert.Single(Of("javbuddy_ffmpeg_gate_wait_seconds"));
        Assert.Equal("test-gate", wait.Tags["gate"]);
        Assert.True((double)wait.Value! > 0);
        var before = recorder.Measurements.Count;
        recorder.SampleGauges();
        Assert.All(recorder.Measurements.Skip(before).Where(m => m.InstrumentName == "javbuddy_ffmpeg_gate_queue_depth" && (string)m.Tags["gate"]! == "test-gate"), m => Assert.Equal(0, m.Value));
    }

    [Fact]
    public async Task CancelledWhileQueued_CountsNoRun_AndLeavesTheQueueEmpty()
    {
        if (OperatingSystem.IsWindows()) return;
        var gate = new FfmpegGate(1, "test-gate");
        var client = Client(Script("ffmpeg", "exit 0"), shortGate: gate);
        using var held = await gate.EnterAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExtractStillWebpAsync("in.mp4", 5, false, "out.webp", cts.Token));

        Assert.Empty(Of("javbuddy_ffmpeg_runs_total"));
        recorder.SampleGauges();
        Assert.Contains(Of("javbuddy_ffmpeg_gate_queue_depth"), m => (string)m.Tags["gate"]! == "test-gate" && (int)m.Value! == 0);
    }
}
