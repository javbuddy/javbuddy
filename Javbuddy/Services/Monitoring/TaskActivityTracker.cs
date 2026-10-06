namespace Javbuddy.Services.Monitoring;

/// <summary>Short-lived, user-triggered work shown alongside scheduled tasks in the sidebar.
/// Singleton so progress and completion remain visible across pages and open circuits.</summary>
public sealed class TaskActivityTracker(ScheduledTaskChangeNotifier changeNotifier, TimeProvider timeProvider)
{
    private readonly Lock gate = new();
    private readonly Dictionary<long, TaskActivity> activities = [];
    private long nextId;

    public TaskActivity? Current
    {
        get
        {
            lock (gate)
            {
                return activities.OrderByDescending(a => a.Value.IsRunning)
                    .ThenByDescending(a => a.Key).Select(a => a.Value).FirstOrDefault();
            }
        }
    }

    public long Start(string name, string message)
    {
        long id;
        lock (gate)
        {
            id = ++nextId;
            activities[id] = new TaskActivity(name, message, IsRunning: true);
        }
        changeNotifier.NotifyChanged();
        return id;
    }

    /// <summary>Updates the message of a still-running activity, e.g. to reflect batch progress.</summary>
    public void Update(long id, string message)
    {
        lock (gate)
        {
            if (!activities.TryGetValue(id, out var activity) || !activity.IsRunning) return;
            activities[id] = activity with { Message = message };
        }
        changeNotifier.NotifyChanged();
    }

    public void Complete(long id, string message, bool failed = false)
    {
        lock (gate)
        {
            if (!activities.TryGetValue(id, out var activity) || !activity.IsRunning) return;
            activities[id] = activity with { Message = message, IsRunning = false, Failed = failed };
        }
        changeNotifier.NotifyChanged();
        _ = ClearCompletedAsync(id);
    }

    private async Task ClearCompletedAsync(long id)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), timeProvider);
        lock (gate)
        {
            activities.Remove(id);
        }
        changeNotifier.NotifyChanged();
    }
}

public sealed record TaskActivity(string Name, string Message, bool IsRunning, bool Failed = false);
