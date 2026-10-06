using Javbuddy.Models;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.MediaInfo;

public class MediaProbeGateTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(80);

    private static MediaProbeResult Ok() => new() { Success = true, ContainerFormat = "MPEG-4" };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task RunAsync_NormalProbe_ReturnsItsResultAndFreesTheSlot()
    {
        var gate = new MediaProbeGate(2);

        var result = await gate.RunAsync("a.mp4", Ok, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Success);
        await WaitUntilAsync(() => gate.Outstanding == 0);
    }

    [Fact]
    public async Task RunAsync_TimedOutProbe_KeepsItsSlotUntilTheNativeCallReturns()
    {
        var gate = new MediaProbeGate(2);
        using var release = new ManualResetEventSlim();

        var result = await gate.RunAsync("hung.mp4", () => { release.Wait(); return Ok(); }, Short, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
        Assert.Equal(1, gate.Outstanding);

        release.Set();
        await WaitUntilAsync(() => gate.Outstanding == 0);
    }

    [Fact]
    public async Task RunAsync_RepeatedRetriesOfAHungFile_JoinTheRunningProbeInsteadOfStartingMore()
    {
        var gate = new MediaProbeGate(4);
        using var release = new ManualResetEventSlim();
        var starts = 0;
        MediaProbeResult Work()
        {
            Interlocked.Increment(ref starts);
            release.Wait();
            return Ok();
        }

        for (var i = 0; i < 5; i++)
        {
            var result = await gate.RunAsync("hung.mp4", Work, Short, CancellationToken.None);
            Assert.False(result.Success);
        }

        Assert.Equal(1, starts);
        Assert.Equal(1, gate.Outstanding);

        release.Set();
        await WaitUntilAsync(() => gate.Outstanding == 0);
        var after = await gate.RunAsync("hung.mp4", Work, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(after.Success);
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task RunAsync_WhenEverySlotIsHeldByHungProbes_FailsFastWithoutStartingMoreNativeWork()
    {
        var gate = new MediaProbeGate(2);
        using var release = new ManualResetEventSlim();
        var starts = 0;
        MediaProbeResult Work()
        {
            Interlocked.Increment(ref starts);
            release.Wait();
            return Ok();
        }

        try
        {
            await gate.RunAsync("a.mp4", Work, Short, CancellationToken.None);
            await gate.RunAsync("b.mp4", Work, Short, CancellationToken.None);
            await WaitUntilAsync(() => Volatile.Read(ref starts) == 2);
            var third = await gate.RunAsync("c.mp4", Work, Short, CancellationToken.None);

            Assert.False(third.Success);
            Assert.Contains("Too many MediaInfo probes", third.ErrorMessage);
            Assert.Equal(2, Volatile.Read(ref starts));
            Assert.Equal(2, gate.Outstanding);
        }
        finally
        {
            release.Set();
            await WaitUntilAsync(() => gate.Outstanding == 0);
        }

        Assert.True((await gate.RunAsync("c.mp4", Ok, TimeSpan.FromSeconds(5), CancellationToken.None)).Success);
    }

    [Fact]
    public async Task RunAsync_NeverRunsMoreProbesAtOnceThanTheLimit_AndLetsTheRestWait()
    {
        var gate = new MediaProbeGate(3);
        var running = 0;
        var peak = 0;
        MediaProbeResult Work()
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            Thread.Sleep(40);
            Interlocked.Decrement(ref running);
            return Ok();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => gate.RunAsync($"f{i}.mp4", Work, TimeSpan.FromSeconds(10), CancellationToken.None)));

        Assert.All(results, r => Assert.True(r.Success));
        Assert.True(peak <= 3, $"peak concurrent probes was {peak}");
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_Throws_AndTheProbeStillReleasesItsSlotWhenItReturns()
    {
        var gate = new MediaProbeGate(2);
        using var release = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource(Short);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.RunAsync("hung.mp4", () => { release.Wait(); return Ok(); }, TimeSpan.FromSeconds(30), cts.Token));

        Assert.Equal(1, gate.Outstanding);
        release.Set();
        await WaitUntilAsync(() => gate.Outstanding == 0);
    }

    [Fact]
    public async Task Prober_UsesTheGate_ATimedOutNativeProbeIsAFailedResult()
    {
        using var factory = new TestDbContextFactory();
        var gate = new MediaProbeGate(2);
        using var release = new ManualResetEventSlim();
        var prober = new MediaInfoProber(factory, gate, _ => { release.Wait(); return Ok(); }, Short);

        var result = await prober.ProbeAsync("/media/hung.mp4");

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
        Assert.Equal(1, gate.Outstanding);
        release.Set();
        await WaitUntilAsync(() => gate.Outstanding == 0);
    }

    [Fact]
    public async Task Prober_WhenMediaInfoIsDisabled_SkipsWithoutTouchingTheNativeLibrary()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MediaInfoSettings.Add(new MediaInfoSettings { Enabled = false });
            await db.SaveChangesAsync();
        }
        var called = false;
        var prober = new MediaInfoProber(factory, new MediaProbeGate(2), _ => { called = true; return Ok(); });

        var result = await prober.ProbeAsync("/media/a.mp4");

        Assert.True(result.Skipped);
        Assert.False(called);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }
}
