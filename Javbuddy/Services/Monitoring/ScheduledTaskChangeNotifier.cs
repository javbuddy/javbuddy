namespace Javbuddy.Services.Monitoring;

/// <summary>Lightweight in-process pub/sub so the sidebar's active-task indicator (any open
/// circuit) learns that a ScheduledTaskRun or user-triggered TaskActivity changed state —
/// without polling. Singleton, same shape as TorrentChangeNotifier/MovieChangeNotifier.</summary>
public class ScheduledTaskChangeNotifier
{
    /// <summary>Raised after a scheduled run or transient activity changes. Handlers run on whatever thread
    /// called NotifyChanged — a Blazor component handling this must marshal back onto its own
    /// circuit via InvokeAsync before touching its state or calling StateHasChanged.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
