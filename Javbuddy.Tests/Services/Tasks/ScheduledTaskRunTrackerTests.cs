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

        tracker.End("a");
        Assert.False(tracker.IsRunning("a"));
        Assert.True(tracker.TryBegin("a"));
    }
}
