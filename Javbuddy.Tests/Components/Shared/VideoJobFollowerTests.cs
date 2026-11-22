using Javbuddy.Components.Shared;
using Javbuddy.Services.VideoRepair;

namespace Javbuddy.Tests.Components.Shared;

public class VideoJobFollowerTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(10);

    private static Task Inline(Func<Task> work) => work();

    private static async Task<string?> FollowToEndAsync(Task<VideoRepairResult> task)
    {
        using var follower = new VideoJobFollower(Inline, Fast);
        var finished = new TaskCompletionSource<string?>();
        follower.Follow(new VideoRepairJob { Task = task }, "Scene detection", () => Task.CompletedTask, error =>
        {
            finished.TrySetResult(error);
            return Task.CompletedTask;
        });
        return await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Success_ReportsNoError() =>
        Assert.Null(await FollowToEndAsync(Task.FromResult(VideoRepairResult.Ok("out.mp4"))));

    [Fact]
    public async Task Failure_ReportsTheResultMessage_OrAGenericOne()
    {
        Assert.Equal("disk full", await FollowToEndAsync(Task.FromResult(VideoRepairResult.Failed("disk full"))));
        Assert.Equal("Scene detection failed.", await FollowToEndAsync(Task.FromResult(new VideoRepairResult())));
    }

    [Fact]
    public async Task Cancellation_ReportsCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.Equal("Scene detection was cancelled.", await FollowToEndAsync(Task.FromCanceled<VideoRepairResult>(cts.Token)));
    }

    [Fact]
    public async Task Exception_ReportsGenericFailure() =>
        Assert.Equal("Scene detection failed; see the server log.",
            await FollowToEndAsync(Task.FromException<VideoRepairResult>(new InvalidOperationException("boom"))));

    [Fact]
    public async Task TicksWhileTheJobRuns()
    {
        using var follower = new VideoJobFollower(Inline, Fast);
        var running = new TaskCompletionSource<VideoRepairResult>();
        var ticks = 0;
        var tickedTwice = new TaskCompletionSource();
        var finished = new TaskCompletionSource();
        follower.Follow(new VideoRepairJob { Task = running.Task }, "X", () =>
        {
            if (Interlocked.Increment(ref ticks) >= 2) tickedTwice.TrySetResult();
            return Task.CompletedTask;
        }, _ =>
        {
            finished.TrySetResult();
            return Task.CompletedTask;
        });

        await tickedTwice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(finished.Task.IsCompleted);
        running.SetResult(VideoRepairResult.Ok("out"));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispose_StopsReporting()
    {
        var running = new TaskCompletionSource<VideoRepairResult>();
        var finished = false;
        var follower = new VideoJobFollower(Inline, Fast);
        follower.Follow(new VideoRepairJob { Task = running.Task }, "X", () => Task.CompletedTask, _ =>
        {
            finished = true;
            return Task.CompletedTask;
        });
        follower.Dispose();
        running.SetResult(VideoRepairResult.Ok("out"));
        await Task.Delay(100);
        Assert.False(finished);
    }
}
