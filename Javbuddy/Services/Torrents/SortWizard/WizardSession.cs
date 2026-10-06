using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>The data every wizard step shares — the torrent being sorted, the current step, the
/// javinizer-go job and the chosen destination — plus the single change notification the page
/// re-renders on. Each step part takes this instead of the whole wizard state, so a part only sees
/// what it can legitimately read or move.</summary>
public sealed class WizardSession(TaskActivityTracker? activities = null)
{
    public event Action? Changed;

    public void NotifyStateChanged() => Changed?.Invoke();

    public int TorrentDownloadId { get; set; }
    public TorrentDownload? Torrent { get; set; }
    public WizardStep Step { get; set; } = WizardStep.Scan;
    public BatchJobResponseDto? JobData { get; set; }
    public string Destination { get; set; } = "";

    /// <summary>Shown on the Review step; written by the review actions and by the review-to-preview
    /// validation, so it lives here rather than in either step.</summary>
    public string? ReviewError { get; set; }

    public bool IsExcluded(BatchFileResultDto? result)
    {
        if (result is null || JobData?.Excluded is null) return false;
        if (!string.IsNullOrEmpty(result.ResultId) && JobData.Excluded.TryGetValue(result.ResultId, out var byId) && byId) return true;
        if (!string.IsNullOrEmpty(result.FilePath) && JobData.Excluded.TryGetValue(result.FilePath, out var byPath) && byPath) return true;
        return false;
    }

    public long? StartActivity(string message) =>
        activities?.Start($"Sorting · {Torrent?.MovieCode ?? "movie sort"}", message);

    public void CompleteActivity(long? activityId, string message, bool failed = false)
    {
        if (activityId is { } id)
        {
            activities?.Complete(id, message, failed);
        }
    }
}
