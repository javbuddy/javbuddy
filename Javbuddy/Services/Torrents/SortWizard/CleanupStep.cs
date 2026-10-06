namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 7 (Done) of the TorrentSort wizard: previewing and confirming deletion of the
/// source download folder after a successful organize.</summary>
public sealed class CleanupStep(WizardSession session, ITorrentSortService torrentSortService)
{
    public bool CleanupPreviewActive { get; private set; }
    public bool PreviewingCleanup { get; private set; }
    public string? CleanupPreviewError { get; private set; }
    public string? CleanupPreviewResolvedPath { get; private set; }
    public List<string>? CleanupPreviewEntries { get; private set; }
    public bool CleaningUp { get; private set; }
    public string? CleanupError { get; private set; }
    public bool CleanupSucceeded { get; private set; }

    public async Task StartCleanupPreviewAsync(CancellationToken ct = default)
    {
        CleanupPreviewActive = true;
        PreviewingCleanup = true;
        CleanupPreviewError = null;
        CleanupPreviewResolvedPath = null;
        CleanupPreviewEntries = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.PreviewCleanupAsync(session.TorrentDownloadId, ct);

        PreviewingCleanup = false;
        if (!result.Success)
        {
            CleanupPreviewError = result.ErrorMessage ?? "Could not list the files to delete.";
            session.NotifyStateChanged();
            return;
        }

        CleanupPreviewResolvedPath = result.ResolvedPath;
        CleanupPreviewEntries = result.EntryPaths;
        session.NotifyStateChanged();
    }

    public void CancelCleanupPreview()
    {
        CleanupPreviewActive = false;
        PreviewingCleanup = false;
        CleanupPreviewError = null;
        CleanupPreviewResolvedPath = null;
        CleanupPreviewEntries = null;
        session.NotifyStateChanged();
    }

    public async Task ConfirmCleanupAsync(CancellationToken ct = default)
    {
        if (!CleanupPreviewActive) return;

        CleaningUp = true;
        CleanupError = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.CleanupSourceAsync(session.TorrentDownloadId, ct);

        CleaningUp = false;
        if (!result.Success)
        {
            CleanupError = result.ErrorMessage ?? "Cleanup failed.";
            session.NotifyStateChanged();
            return;
        }

        CleanupPreviewActive = false;
        CleanupPreviewResolvedPath = null;
        CleanupPreviewEntries = null;
        CleanupSucceeded = true;
        session.NotifyStateChanged();
    }
}
