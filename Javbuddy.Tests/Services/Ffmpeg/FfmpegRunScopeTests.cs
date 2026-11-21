using System.Diagnostics;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Ffmpeg;

public sealed class FfmpegRunScopeTests : IDisposable
{
    private readonly JavbuddyMetrics metrics = new();
    private readonly MetricsRecorder recorder;

    public FfmpegRunScopeTests() => recorder = new MetricsRecorder(metrics);

    public void Dispose()
    {
        recorder.Dispose();
        metrics.Dispose();
    }

    [Theory]
    [InlineData("[mov,mp4,m4a,3gp,3g2,mj2 @ 0x55] moov atom not found", "moov_atom_missing")]
    [InlineData("in.mp4: No such file or directory", "no_such_file")]
    [InlineData("in.mp4: Permission denied", "permission")]
    [InlineData("out.mp4: Operation not permitted", "permission")]
    [InlineData("in.mkv: Invalid data found when processing input", "invalid_data")]
    [InlineData("INVALID DATA FOUND WHEN PROCESSING INPUT", "invalid_data")]
    [InlineData("Conversion failed!", "other")]
    [InlineData("", "other")]
    [InlineData(null, "other")]
    public void Classify_BucketsStderrIntoTheBoundedReasons(string? text, string expected) =>
        Assert.Equal(expected, FfmpegFailureClassifier.Classify(text));

    [Fact]
    public void Classify_PrefersTheMostSpecificReasonInAWholeStderr() =>
        Assert.Equal("moov_atom_missing", FfmpegFailureClassifier.Classify("moov atom not found\nin.mp4: Invalid data found when processing input\n"));

    [Fact]
    public void Observe_KeepsTheMostSpecificReasonAcrossLines_RegardlessOfOrder()
    {
        var scope = new FfmpegRunScope("probe", metrics);
        Assert.Equal("other", scope.FailureReason);

        scope.Observe("in.mp4: Invalid data found when processing input");
        scope.Observe("some unrelated line");
        Assert.Equal("invalid_data", scope.FailureReason);

        scope.Observe("moov atom not found");
        scope.Observe("in.mp4: Invalid data found when processing input");
        Assert.Equal("moov_atom_missing", scope.FailureReason);
    }

    private static Process ShellProcess(string script) =>
        new() { StartInfo = new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", script }, UseShellExecute = false, CreateNoWindow = true } };

    [Fact]
    public void StartAtLowPriority_WhenThePriorityCannotBeSet_CountsItAndStillRuns()
    {
        if (OperatingSystem.IsWindows()) return;
        var scope = new FfmpegRunScope("still", metrics, _ => throw new InvalidOperationException("refused"));
        using var process = ShellProcess("exit 0");

        scope.StartAtLowPriority(process);
        process.WaitForExit();

        Assert.Equal(1L, Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_priority_downgrade_failures_total").Value);
    }

    [Fact]
    public void StartAtLowPriority_WhenItWorks_CountsNothing()
    {
        if (OperatingSystem.IsWindows()) return;
        var scope = new FfmpegRunScope("still", metrics, _ => { });
        using var process = ShellProcess("exit 0");

        scope.StartAtLowPriority(process);
        process.WaitForExit();

        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_priority_downgrade_failures_total");
    }

    [Fact]
    public void Kill_CountsByOperation()
    {
        if (OperatingSystem.IsWindows()) return;
        var scope = new FfmpegRunScope("trickplay", metrics, _ => { });
        using var process = ShellProcess("exec sleep 30");
        scope.StartAtLowPriority(process);

        scope.Kill(process);
        process.WaitForExit();

        var kill = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_kills_total");
        Assert.Equal(1L, kill.Value);
        Assert.Equal("trickplay", kill.Tags["operation"]);
    }

    [Fact]
    public void Kill_OnAProcessThatNeverStarted_IsIgnoredAndNotCounted()
    {
        var scope = new FfmpegRunScope("probe", metrics);
        using var process = new Process();

        scope.Kill(process);

        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_kills_total");
    }

    [Fact]
    public void Kill_OnAProcessThatAlreadyExited_IsNotCounted()
    {
        if (OperatingSystem.IsWindows()) return;
        var scope = new FfmpegRunScope("still", metrics, _ => { });
        using var process = ShellProcess("exit 0");
        scope.StartAtLowPriority(process);
        process.WaitForExit();

        scope.Kill(process);

        Assert.DoesNotContain(recorder.Measurements, m => m.InstrumentName == "javbuddy_ffmpeg_kills_total");
    }
}
