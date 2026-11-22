using Javbuddy.Services.Tasks;

namespace Javbuddy.Tests.Services.Tasks;

public class ScheduledTaskRunTrackerTests
{
    [Fact]
    public void TryBegin_SecondClaimFailsUntilEnded()
    {
        var tracker = new ScheduledTaskRunTracker();

        Assert.True(tracker.TryBegin("a"));
        Assert.False(tracker.TryBegin("a"));
        Assert.True(tracker.TryBegin("b"));

        tracker.End("a", DateTime.UtcNow);
        Assert.False(tracker.IsRunning("a"));
        Assert.True(tracker.TryBegin("a"));
    }

    [Fact]
    public void End_KeepsTheLatestCompletion()
    {
        var tracker = new ScheduledTaskRunTracker();
        var newer = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        tracker.End("a", newer);
        tracker.End("a", newer.AddHours(-1));

        Assert.Equal(newer, tracker.GetLastCompletion("a"));
        Assert.Null(tracker.GetLastCompletion("never"));
    }

    [Fact]
    public void Latest_PicksTheLaterTimestampAndToleratesNulls()
    {
        var a = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var b = a.AddDays(1);

        Assert.Equal(b, ScheduledTaskRunTracker.Latest(a, b));
        Assert.Equal(b, ScheduledTaskRunTracker.Latest(b, a));
        Assert.Equal(a, ScheduledTaskRunTracker.Latest(a, null));
        Assert.Equal(a, ScheduledTaskRunTracker.Latest(null, a));
        Assert.Null(ScheduledTaskRunTracker.Latest(null, null));
    }

    [Fact]
    public async Task WaitUntilIdleAsync_ReturnsOnceTheTaskEnds()
    {
        var tracker = new ScheduledTaskRunTracker();
        tracker.TryBegin("a");

        var wait = tracker.WaitUntilIdleAsync("a");
        Assert.False(wait.IsCompleted);
        tracker.End("a", DateTime.UtcNow);

        await wait.WaitAsync(TimeSpan.FromSeconds(30));
        await tracker.WaitUntilIdleAsync("never-ran");
    }

    [Fact]
    public void Release_FreesTheClaimWithoutRecordingACompletion()
    {
        var tracker = new ScheduledTaskRunTracker();
        tracker.TryBegin("a");

        tracker.Release("a");

        Assert.False(tracker.IsRunning("a"));
        Assert.Null(tracker.GetLastCompletion("a"));
    }
}
