using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Trickplay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayQueueTests
{
    private static (TrickplayQueue Queue, ITrickplayGenerator Generator, TaskActivityTracker Activities) Create(TimeProvider? clock = null)
    {
        var generator = Substitute.For<ITrickplayGenerator>();
        var services = new ServiceCollection().AddSingleton(generator).BuildServiceProvider();
        var activities = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var queue = new TrickplayQueue(services.GetRequiredService<IServiceScopeFactory>(), activities, clock ?? TimeProvider.System, NullLogger<TrickplayQueue>.Instance);
        return (queue, generator, activities);
    }

    [Fact]
    public async Task Jobs_RunOneAfterAnother_AndReportTheirProgressInTheSidebar()
    {
        var (queue, generator, activities) = Create();
        var first = new TaskCompletionSource<TrickplayGenerateResult>();
        generator.GenerateAsync(1, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(first.Task);
        generator.GenerateAsync(2, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(TrickplayGenerateResult.Generated);

        // Both queued before the consumer starts, so the first job already counts the second one;
        // otherwise it could pick up job 1 alone and report "1 of 1".
        Assert.True(queue.Enqueue(1, "ABC-1", "0000000000000001", 7200));
        Assert.True(queue.Enqueue(2, "ABC-2", "0000000000000002", 3600));
        Assert.False(queue.Enqueue(2, "ABC-2", "0000000000000002", 3600));
        await queue.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => activities.Current?.Message == "Generating trickplay 1 of 2: ABC-1");
        await generator.DidNotReceive().GenerateAsync(2, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>());

        first.SetResult(TrickplayGenerateResult.Generated);
        await WaitUntilAsync(() => activities.Current is { IsRunning: false });
        Assert.Equal("Generated trickplay for 2 of 2 movies", activities.Current!.Message);
        Assert.Equal(0, queue.Count);
        await queue.StopAsync(CancellationToken.None);
        queue.Dispose();
    }

    [Fact]
    public async Task FfmpegProgress_ReachesTheSidebar_WithTheTimeLeftForTheQueue()
    {
        var clock = new ManualClock();
        var (queue, generator, activities) = Create(clock);
        var first = new TaskCompletionSource<TrickplayGenerateResult>();
        var second = new TaskCompletionSource<TrickplayGenerateResult>();
        generator.GenerateAsync(1, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            // A minute in, ffmpeg is 10 minutes into the 2-hour video: 10x real time.
            clock.Advance(TimeSpan.FromMinutes(1));
            call.ArgAt<IProgress<FfmpegProgress>?>(1)!.Report(new FfmpegProgress(8.3, TimeSpan.FromMinutes(10)));
            return first.Task;
        });
        generator.GenerateAsync(2, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(second.Task);

        // Both queued before the consumer starts, so the first job's estimate includes the second.
        queue.Enqueue(1, "ABC-1", "0000000000000001", 7200);
        queue.Enqueue(2, "ABC-2", "0000000000000002", 3600);
        await queue.StartAsync(CancellationToken.None);

        // 110 min of this video and 60 min of the next one left, at 10x: 17 min.
        await WaitUntilAsync(() => activities.Current?.Message == "Generating trickplay 1 of 2: ABC-1 (8%, about 17 min left)");

        // The first movie took 12 min for 2 h of video, so the next hour takes about 6 min.
        clock.Advance(TimeSpan.FromMinutes(11));
        first.SetResult(TrickplayGenerateResult.Generated);
        await WaitUntilAsync(() => activities.Current?.Message == "Generating trickplay 2 of 2: ABC-2 (about 6 min left)");

        second.SetResult(TrickplayGenerateResult.Generated);
        await WaitUntilAsync(() => activities.Current is { IsRunning: false });
        await queue.StopAsync(CancellationToken.None);
        queue.Dispose();
    }

    [Fact]
    public async Task AFailure_IsNotRetriedForTheSameFile_UntilRestart()
    {
        var (queue, generator, _) = Create();
        generator.GenerateAsync(1, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(TrickplayGenerateResult.Failed);
        await queue.StartAsync(CancellationToken.None);

        queue.Enqueue(1, "ABC-1", "0000000000000001", 60);
        // The failure is recorded before the job leaves the queue, so wait for both.
        await WaitUntilAsync(() => queue.HasFailed(1, "0000000000000001") && queue.Count == 0);

        Assert.False(queue.Enqueue(1, "ABC-1", "0000000000000001", 60));
        // A replaced file is a new identity, so it gets its chance.
        Assert.True(queue.Enqueue(1, "ABC-1", "0000000000000009", 60));
        await queue.StopAsync(CancellationToken.None);
        queue.Dispose();
    }

    [Fact]
    public void NoEstimate_UntilTheBatchHasRunForTheSampleTime()
    {
        Assert.Null(TrickplayQueue.EstimateRemaining(300, 29, 7200));
        Assert.Null(TrickplayQueue.EstimateRemaining(0, 60, 7200));
        Assert.Equal(TimeSpan.FromSeconds(720), TrickplayQueue.EstimateRemaining(600, 60, 7200));
    }

    [Theory]
    [InlineData(20, "less than a minute left")]
    [InlineData(90, "about 2 min left")]
    [InlineData(59 * 60, "about 59 min left")]
    [InlineData(60 * 60, "about 1 h left")]
    [InlineData(125 * 60, "about 2 h 5 min left")]
    [InlineData(48 * 3600, "about 2 d left")]
    [InlineData(27 * 3600 + 1200, "about 1 d 3 h left")]
    public void FormatRemaining_RoundsToTheMinute(int seconds, string expected) =>
        Assert.Equal(expected, TrickplayQueue.FormatRemaining(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ProgressMessage_ShowsWhatIsKnown()
    {
        Assert.Equal("Generating trickplay 1 of 3: ABC-1", TrickplayQueue.ProgressMessage(1, 3, "ABC-1", null, null));
        Assert.Equal("Generating trickplay 1 of 3: ABC-1 (99%)", TrickplayQueue.ProgressMessage(1, 3, "ABC-1", 99.9, null));
        Assert.Equal("Generating trickplay 2 of 3: ABC-2 (about 5 min left)", TrickplayQueue.ProgressMessage(2, 3, "ABC-2", null, TimeSpan.FromMinutes(5)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    /// <summary>A clock that only moves when told to, for the queue's elapsed-time measurements.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
    }
}
