namespace Javbuddy.Services.Movies;

/// <summary>Lightweight in-process pub/sub so a Movies grid already open in one Blazor Server
/// circuit (browser tab) learns that a movie was added from another circuit — a different tab
/// running Add New or Library Import — without the user needing to refresh the page. Singleton:
/// each circuit is a separate component-state world, so this is the one thing every circuit
/// shares to signal across that boundary.</summary>
public class MovieChangeNotifier
{
    /// <summary>Raised after one or more movies have been added. Handlers run on whatever thread
    /// called NotifyChanged — a Blazor component handling this must marshal back onto its own
    /// circuit via InvokeAsync before touching its state or calling StateHasChanged.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
