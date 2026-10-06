using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 5 of the TorrentSort wizard: validating the reviewed results and fetching the
/// organize preview for each active one.</summary>
public sealed class PreviewStep(WizardSession session, ITorrentSortService torrentSortService)
{
    public Dictionary<string, OrganizePreviewResponseDto> Previews { get; } = new();
    public bool Previewing { get; private set; }
    public string? PreviewError { get; private set; }

    public void ClearError() => PreviewError = null;

    public async Task GoToPreviewAsync(CancellationToken ct = default)
    {
        if (session.JobData?.Results is null) return;

        var activeResults = session.JobData.Results.Values.Where(r => !session.IsExcluded(r)).ToList();
        if (activeResults.Count == 0)
        {
            session.ReviewError = "No active files to preview. All files are excluded.";
            session.NotifyStateChanged();
            return;
        }

        var unresolvable = activeResults.FirstOrDefault(r => r.Status == JavinizerJobStatus.Failed || r.Movie is null);
        if (unresolvable is not null)
        {
            session.ReviewError = $"The file \"{TorrentSortPreviewHelper.GetFileName(unresolvable.FilePath)}\" could not be matched. Please fix the match code or exclude it before continuing to preview.";
            session.NotifyStateChanged();
            return;
        }

        Previewing = true;
        session.ReviewError = null;
        PreviewError = null;
        Previews.Clear();
        session.NotifyStateChanged();

        foreach (var r in activeResults)
        {
            var result = await torrentSortService.PreviewAsync(session.TorrentDownloadId, r.ResultId, session.Destination, ct);
            if (!result.Success || result.Data is null)
            {
                PreviewError = result.ErrorMessage ?? $"Preview failed for {TorrentSortPreviewHelper.GetFileName(r.FilePath)}.";
                Previewing = false;
                session.NotifyStateChanged();
                return;
            }

            Previews[r.ResultId] = result.Data;
        }

        Previewing = false;
        session.Step = WizardStep.Preview;
        session.NotifyStateChanged();
    }
}
