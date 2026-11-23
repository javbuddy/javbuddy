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
        Assert.Empty(Of("javbuddy_ffmpeg_input_bytes_total")); // ffprobe reads only headers
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
    public async Task RemuxChapters_Success_AddsTheInputDurationAsMediaSeconds_UngatedLabel()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "exit 0");

        var result = await Client(ffmpeg).RemuxWithChaptersAsync("in.mp4", ";FFMETADATA1\n", "out.mp4", TimeSpan.FromSeconds(90), null);

        Assert.True(result.Success);
        Assert.Equal(1L, Run("remux_chapters", "success").Value);
        var media = Assert.Single(Of("javbuddy_ffmpeg_input_media_seconds_total"));
        Assert.Equal(90d, media.Value);
        Assert.Equal("remux_chapters", media.Tags["operation"]);
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

    private string InputFile(string name, int bytes)
    {
        var path = Path.Combine(dir.FullName, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // A fake ffmpeg that writes 7 bytes to its last argument, which is the output path for every output-file operation.
    private const string WriteLastArgument = "for a; do last=\"$a\"; done; printf 'abcdefg' > \"$last\"";

    [Fact]
    public async Task Still_Success_CountsOutputBytes_ButNoInputBytes()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 1000);
        var output = Path.Combine(dir.FullName, "out.webp");

        var result = await Client(Script("ffmpeg", WriteLastArgument)).ExtractStillWebpAsync(input, 5, false, output);

        Assert.True(result.Success);
        Assert.Empty(Of("javbuddy_ffmpeg_input_bytes_total")); // a still reads only a few MB of the file
        var outBytes = Assert.Single(Of("javbuddy_ffmpeg_output_bytes_total"));
        Assert.Equal(7L, outBytes.Value);
        Assert.Equal("still", outBytes.Tags["operation"]);
    }

    [Fact]
    public async Task Success_WithoutAnOutputFile_AddsNoOutputBytes_AndDoesNotFail()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 10);

        var result = await Client(Script("ffmpeg", "exit 0")).ExtractStillWebpAsync(input, 5, false, Path.Combine(dir.FullName, "missing.webp"));

        Assert.True(result.Success);
        Assert.Empty(Of("javbuddy_ffmpeg_output_bytes_total"));
    }

    [Fact]
    public async Task Preview_Success_CountsOutputBytes_ButNoInputBytes()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 1000);

        var result = await Client(Script("ffmpeg", WriteLastArgument)).ExtractPreviewVideoAsync(input, 5, 3, false, Path.Combine(dir.FullName, "out.webm"));

        Assert.True(result.Success);
        Assert.Empty(Of("javbuddy_ffmpeg_input_bytes_total"));
        Assert.Equal(7L, Assert.Single(Of("javbuddy_ffmpeg_output_bytes_total")).Value);
    }

    [Fact]
    public async Task TrickplayClip_CountsNoInputBytes_ButTheWholeFileDoes()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 100);
        var client = Client(Script("ffmpeg", "exit 0"));

        await client.GenerateTrickplayAsync(input, new FfmpegTrickplayRequest(2, 320, 180, 5, 5, false, false, StartSeconds: 30, LengthSeconds: 10), dir.FullName, TimeSpan.FromHours(1), null);
        Assert.Empty(Of("javbuddy_ffmpeg_input_bytes_total"));

        await client.GenerateTrickplayAsync(input, new FfmpegTrickplayRequest(2, 320, 180, 5, 5, false, false), dir.FullName, TimeSpan.FromHours(1), null);
        Assert.Equal(100L, Assert.Single(Of("javbuddy_ffmpeg_input_bytes_total")).Value);
    }

    [Fact]
    public async Task FailedRun_CountsNoBytes()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 10);

        await Client(Script("ffmpeg", WriteLastArgument + "; exit 1")).ExtractStillWebpAsync(input, 5, false, Path.Combine(dir.FullName, "out.webp"));

        Assert.Empty(Of("javbuddy_ffmpeg_input_bytes_total"));
        Assert.Empty(Of("javbuddy_ffmpeg_output_bytes_total"));
    }

    [Fact]
    public async Task Trickplay_CountsTheBytesOfTheOutputDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 100);
        var outDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "sheets")).FullName;
        var ffmpeg = Script("ffmpeg", $"printf 'aaaa' > '{outDir}/0.webp'; printf 'bb' > '{outDir}/1.webp'");
        var request = new FfmpegTrickplayRequest(2, 320, 180, 5, 5, false, false);

        Assert.True((await Client(ffmpeg).GenerateTrickplayAsync(input, request, outDir, TimeSpan.FromMinutes(10), null)).Success);

        Assert.Equal(6L, Assert.Single(Of("javbuddy_ffmpeg_output_bytes_total")).Value);
        Assert.Equal(100L, Assert.Single(Of("javbuddy_ffmpeg_input_bytes_total")).Value);
    }

    [Fact]
    public async Task Concat_SumsEveryPartAsInput_AndCountsTheOutputFile()
    {
        if (OperatingSystem.IsWindows()) return;
        var output = Path.Combine(dir.FullName, "merged.mp4");
        var request = new FfmpegConcatRequest(
            [new FfmpegConcatPart(InputFile("p1.mp4", 300), "Part 1", 60_000_000), new FfmpegConcatPart(InputFile("p2.mp4", 200), "Part 2", 60_000_000)],
            output);

        Assert.True((await Client(Script("ffmpeg", WriteLastArgument)).ConcatWithChaptersAsync(request, null)).Success);

        Assert.Equal(500L, Assert.Single(Of("javbuddy_ffmpeg_input_bytes_total")).Value);
        Assert.Equal(7L, Assert.Single(Of("javbuddy_ffmpeg_output_bytes_total")).Value);
        Assert.Equal(120d, Assert.Single(Of("javbuddy_ffmpeg_input_media_seconds_total")).Value);
    }

    [Fact]
    public async Task SceneDetect_CountsInputBytesOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        var input = InputFile("in.mp4", 42);

        Assert.NotNull(await Client(Script("ffmpeg", "exit 0")).DetectSceneSignalsAsync(input, TimeSpan.FromMinutes(1), null));

        Assert.Equal(42L, Assert.Single(Of("javbuddy_ffmpeg_input_bytes_total")).Value);
        Assert.Empty(Of("javbuddy_ffmpeg_output_bytes_total"));
    }

    [Fact]
    public async Task SuccessWithAKnownDuration_RecordsTheSpeedRatio_AsMediaSecondsOverWallSeconds()
    {
        if (OperatingSystem.IsWindows()) return;

        await Client(Script("ffmpeg", "exit 0")).RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null);

        var ratio = Assert.Single(Of("javbuddy_ffmpeg_speed_ratio"));
        Assert.Equal("remux_dts2pts", ratio.Tags["operation"]);
        var wall = (double)Assert.Single(Of("javbuddy_ffmpeg_duration_seconds")).Value!;
        Assert.Equal(90d / wall, (double)ratio.Value!, precision: 6);
    }

    [Fact]
    public async Task NoSpeedRatio_WhenTheDurationIsUnknown_OrTheRunFailed()
    {
        if (OperatingSystem.IsWindows()) return;

        await Client(Script("ffmpeg-ok", "exit 0")).ExtractStillWebpAsync("in.mp4", 5, false, "out.webp"); // still: no known duration
        await Client(Script("ffmpeg-bad", "exit 1")).RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null);

        Assert.Empty(Of("javbuddy_ffmpeg_speed_ratio"));
    }

    [Theory]
    [InlineData("echo '[mov,mp4 @ 0x1] moov atom not found' >&2", "moov_atom_missing")]
    [InlineData("echo 'in.mp4: No such file or directory' >&2", "no_such_file")]
    [InlineData("echo 'in.mp4: Permission denied' >&2", "permission")]
    [InlineData("echo 'in.mp4: Invalid data found when processing input' >&2", "invalid_data")]
    [InlineData("echo 'Conversion failed!' >&2", "other")]
    [InlineData("", "other")]
    public async Task FailedRun_CountsTheReasonFromStderr(string stderrCommand, string reason)
    {
        if (OperatingSystem.IsWindows()) return;

        await Client(Script("ffmpeg", stderrCommand + "; exit 1")).ExtractStillWebpAsync("in.mp4", 5, false, "out.webp");

        var failure = Assert.Single(Of("javbuddy_ffmpeg_failures_total"));
        Assert.Equal(1L, failure.Value);
        Assert.Equal("still", failure.Tags["operation"]);
        Assert.Equal(reason, failure.Tags["reason"]);
    }

    [Fact]
    public async Task FailureReason_IsReadFromProgressStderr_ForLineReadingOperations()
    {
        if (OperatingSystem.IsWindows()) return;

        await Client(Script("ffmpeg", "echo 'frame=1 time=00:00:01.00' >&2; echo 'in.mp4: Invalid data found when processing input' >&2; exit 1"))
            .RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null);
        Assert.Null(await Client(Script("ffmpeg-scene", "echo 'moov atom not found' >&2; exit 1")).DetectSceneSignalsAsync("in.mp4", TimeSpan.FromMinutes(1), null));

        Assert.Equal("invalid_data", Assert.Single(Of("javbuddy_ffmpeg_failures_total"), m => (string)m.Tags["operation"]! == "remux_dts2pts").Tags["reason"]);
        Assert.Equal("moov_atom_missing", Assert.Single(Of("javbuddy_ffmpeg_failures_total"), m => (string)m.Tags["operation"]! == "scene_detect").Tags["reason"]);
    }

    [Fact]
    public async Task Probe_FailureReason_IsReadFromFfprobeStderr()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Null(await Client(null, Script("ffprobe", "echo 'moov atom not found' >&2; exit 1")).ProbeAsync("x.mp4"));

        Assert.Equal("moov_atom_missing", Assert.Single(Of("javbuddy_ffmpeg_failures_total")).Tags["reason"]);
    }

    [Fact]
    public async Task SuccessAndCancelledRuns_CountNoFailure()
    {
        if (OperatingSystem.IsWindows()) return;
        await Client(Script("ffmpeg", "exit 0")).RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(9), null);
        var client = Client(Script("ffmpeg-slow", "exec sleep 30"));
        using var cts = new CancellationTokenSource(200);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(9), null, cts.Token));

        Assert.Empty(Of("javbuddy_ffmpeg_failures_total"));
    }

    [Fact]
    public async Task CancellingARun_CountsAKillForItsOperation()
    {
        if (OperatingSystem.IsWindows()) return;
        var client = Client(Script("ffmpeg", "exec sleep 30"));
        using var cts = new CancellationTokenSource();

        var run = client.RemuxDts2PtsAsync("in.mp4", "out.mp4", TimeSpan.FromSeconds(90), null, cts.Token);
        await SampleUntilAsync(() => Of("javbuddy_ffmpeg_active_processes").Any(m => (string)m.Tags["gate"]! == "none" && (int)m.Value! == 1));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var kill = Assert.Single(Of("javbuddy_ffmpeg_kills_total"));
        Assert.Equal(1L, kill.Value);
        Assert.Equal("remux_dts2pts", kill.Tags["operation"]);
    }

    [Fact]
    public async Task CancellingAProbe_CountsAKill()
    {
        if (OperatingSystem.IsWindows()) return;
        var client = Client(null, Script("ffprobe", "exec sleep 30"));
        using var cts = new CancellationTokenSource();

        var run = client.ProbeAsync("x.mp4", cts.Token);
        await SampleUntilAsync(() => Of("javbuddy_ffmpeg_active_processes").Any(m => (string)m.Tags["gate"]! == "none" && (int)m.Value! == 1));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal("probe", Assert.Single(Of("javbuddy_ffmpeg_kills_total")).Tags["operation"]);
    }

    [Fact]
    public async Task Still_FailedRun_RecordsAFailedSpanWithTheFailureReason_AndNoWaitWhileASlotIsFree()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "echo 'No such file or directory' >&2; exit 1");
        using var capture = new TraceCapture(JavbuddyTracing.SourceName);

        await Client(ffmpeg, shortGate: new FfmpegGate(1, FfmpegGate.ShortName)).ExtractStillWebpAsync("in.mp4", 5, false, "out.webp");

        var run = Assert.Single(capture.Spans);
        Assert.Equal("ffmpeg still", run.DisplayName);
        Assert.Equal("short", run.GetTagItem("javbuddy.ffmpeg.gate"));
        Assert.Equal("failure", run.GetTagItem("javbuddy.outcome"));
        Assert.Equal(FfmpegFailureClassifier.NoSuchFile, run.GetTagItem("error.type"));
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, run.Status);
    }

    [Fact]
    public async Task Still_QueuedBehindAnotherRun_RecordsAGateWaitChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("ffmpeg", "exit 0");
        var gate = new FfmpegGate(1, FfmpegGate.ShortName);
        using var capture = new TraceCapture(JavbuddyTracing.SourceName);

        var held = await gate.EnterAsync(CancellationToken.None);
        var run = Client(ffmpeg, shortGate: gate).ExtractStillWebpAsync("in.mp4", 5, false, "out.webp");
        await Task.Delay(50);
        held.Dispose();
        await run;

        var span = Assert.Single(capture.Spans, s => s.DisplayName == "ffmpeg still");
        var wait = Assert.Single(capture.Spans, s => s.DisplayName == "ffmpeg gate wait");
        Assert.Equal(span.SpanId, wait.ParentSpanId);
    }

    [Fact]
    public async Task Probe_Success_RecordsASuccessfulSpanWithoutAGateWait()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffprobe = Script("ffprobe", "echo '{\"format\":{\"duration\":\"120\"}}'");
        using var capture = new TraceCapture(JavbuddyTracing.SourceName);

        await Client(null, ffprobe).ProbeAsync("x.mp4");

        var run = Assert.Single(capture.Spans);
        Assert.Equal("ffmpeg probe", run.DisplayName);
        Assert.Equal("success", run.GetTagItem("javbuddy.outcome"));
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Unset, run.Status);
    }
}
