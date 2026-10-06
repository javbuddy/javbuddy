using Javbuddy.Services.Monitoring;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Monitoring;

public class TaskActivityTrackerTests
{
    [Fact]
    public async Task CompletionExpiresAfterFiveSeconds_AndNotifiesSubscribers()
    {
        var clock = new ManualActivityTimeProvider();
        var notifier = new ScheduledTaskChangeNotifier();
        var tracker = new TaskActivityTracker(notifier, clock);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        notifier.Changed += () =>
        {
            notifications++;
            if (tracker.Current is null) expired.TrySetResult();
        };

        var id = tracker.Start("Scan", "Checking");
        tracker.Complete(id, "Failed", failed: true);
        Assert.Equal(new TaskActivity("Scan", "Failed", false, true), tracker.Current);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.NotNull(tracker.Current);
        clock.Advance(TimeSpan.FromSeconds(1));
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(tracker.Current);
        Assert.Equal(3, notifications);
    }

    [Fact]
    public void Update_ChangesMessageOnRunningActivity_AndNotifiesSubscribers()
    {
        var clock = new ManualActivityTimeProvider();
        var notifier = new ScheduledTaskChangeNotifier();
        var tracker = new TaskActivityTracker(notifier, clock);
        var notifications = 0;
        notifier.Changed += () => notifications++;

        var id = tracker.Start("Batch enrichment", "Starting…");
        tracker.Update(id, "Enriching actor 1 of 2 (Mikami Yua)… (50%)");

        Assert.Equal(new TaskActivity("Batch enrichment", "Enriching actor 1 of 2 (Mikami Yua)… (50%)", true), tracker.Current);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Update_AfterCompletion_IsIgnored()
    {
        var clock = new ManualActivityTimeProvider();
        var notifier = new ScheduledTaskChangeNotifier();
        var tracker = new TaskActivityTracker(notifier, clock);

        var id = tracker.Start("Batch enrichment", "Starting…");
        tracker.Complete(id, "Done");
        tracker.Update(id, "Should not apply");

        Assert.Equal(new TaskActivity("Batch enrichment", "Done", false), tracker.Current);
    }

    [Fact]
    public async Task ExpiringOlderCompletion_PreservesNewerRunningActivity()
    {
        var clock = new ManualActivityTimeProvider();
        var notifier = new ScheduledTaskChangeNotifier();
        var tracker = new TaskActivityTracker(notifier, clock);
        var first = tracker.Start("First", "Checking");
        var second = tracker.Start("Second", "Checking");
        Assert.Equal("Second", tracker.Current?.Name);
        tracker.Complete(second, "Done");
        Assert.Equal("First", tracker.Current?.Name);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notifier.Changed += () => expired.TrySetResult();

        clock.Advance(TimeSpan.FromSeconds(5));
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new TaskActivity("First", "Checking", true), tracker.Current);
        tracker.Complete(first, "First done");
        Assert.Equal(new TaskActivity("First", "First done", false), tracker.Current);
    }
}
