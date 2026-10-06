namespace Javbuddy.Services.Torrents;

/// <summary>Lightweight in-process pub/sub so an open Activity &gt; Queue/History page learns
/// about a grab or a sync update made from another circuit, without needing a refresh. Singleton,
/// same shape as MovieChangeNotifier — see that type's remarks for why this needs to be a
/// singleton at all.</summary>
public class TorrentChangeNotifier
{
    /// <summary>Raised after a TorrentDownload row is added or updated. Handlers run on whatever
    /// thread called NotifyChanged — a Blazor component handling this must marshal back onto its
    /// own circuit via InvokeAsync before touching its state or calling StateHasChanged.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
