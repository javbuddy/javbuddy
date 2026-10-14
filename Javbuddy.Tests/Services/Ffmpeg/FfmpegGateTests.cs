using Javbuddy.Services.Ffmpeg;
using NSubstitute;

namespace Javbuddy.Tests.Services.Ffmpeg;

public class FfmpegGateTests
{
    [Fact]
    public async Task EnterAsync_WaitsForAFreeSlot_AndGivesItBackOnDispose()
    {
        var gate = new FfmpegGate(1);
        var first = await gate.EnterAsync(CancellationToken.None);

        var second = gate.EnterAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        first.Dispose(); // a second dispose must not hand out an extra slot
        using var secondSlot = await second.WaitAsync(TimeSpan.FromSeconds(5));

        var third = gate.EnterAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(third.IsCompleted);
    }

    [Fact]
    public async Task EnterAsync_CancelledWhileWaiting_Throws()
    {
        var gate = new FfmpegGate(1);
        using var held = await gate.EnterAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnterAsync(cts.Token));
    }

    [Fact]
    public void Shared_AllowsOneOrTwoAtATime() =>
        Assert.InRange(FfmpegGate.DefaultMaxConcurrency, 1, 2);

    [Fact]
    public async Task ClientRuns_ShareTheGate_SoOnlyOneFfmpegRunsAtATime()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-gate-test-").FullName;
        try
        {
            // Each fake ffmpeg appends "start"/"end" around a short sleep; with one slot the lines must alternate.
            var log = Path.Combine(dir, "log.txt");
            var script = Path.Combine(dir, "ffmpeg");
            await File.WriteAllTextAsync(script, $"#!/bin/sh\necho start >> '{log}'\nsleep 0.3\necho end >> '{log}'\nexit 0\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfmpegPath.Returns(script);
            var gate = new FfmpegGate(1);

            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
                new FfmpegClient(resolver, gate).ExtractStillWebpAsync("in.mp4", 0, leftEyeOnly: false, "out.webp")));

            Assert.All(results, r => Assert.True(r.Success, r.ErrorMessage));
            Assert.Equal(["start", "end", "start", "end", "start", "end"], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AsksFfmpegForErrorsOnly_WithoutStats()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-loglevel-test-").FullName;
        try
        {
            var script = Path.Combine(dir, "ffmpeg");
            await File.WriteAllTextAsync(script, "#!/bin/sh\necho \"$*\" >&2\nexit 1\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfmpegPath.Returns(script);

            var result = await new FfmpegClient(resolver, new FfmpegGate(1)).ExtractStillWebpAsync("in.mp4", 0, leftEyeOnly: false, "out.webp");

            Assert.Contains("-loglevel error -nostats", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeAsync_Cancelled_KillsFfprobe()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("javbuddy-ffprobe-cancel-test-").FullName;
        try
        {
            var pidFile = Path.Combine(dir, "pid");
            var script = Path.Combine(dir, "ffprobe");
            await File.WriteAllTextAsync(script, $"#!/bin/sh\necho $$ > '{pidFile}'\nexec sleep 30\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var resolver = Substitute.For<IFfmpegBinaryResolver>();
            resolver.FfprobePath.Returns(script);
            using var cts = new CancellationTokenSource();

            var probe = new FfmpegClient(resolver).ProbeAsync("in.mp4", cts.Token);
            await WaitForAsync(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0);
            var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
            await WaitForAsync(() => !Directory.Exists($"/proc/{pid}") || IsZombie(pid));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool IsZombie(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/stat").Contains(") Z ", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}
