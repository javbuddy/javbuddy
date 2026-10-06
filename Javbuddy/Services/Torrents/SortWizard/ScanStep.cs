using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 1 of the TorrentSort wizard: scanning the download folder and choosing which
/// files to send to the batch scrape.</summary>
public sealed class ScanStep(WizardSession session, ITorrentSortService torrentSortService)
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".wmv", ".iso", ".ts", ".m2ts", ".mov", ".flv"];

    public string ScanPath { get; set; } = "";
    public bool ScanPathMapped { get; private set; }
    public bool Scanning { get; private set; }
    public string? ScanError { get; private set; }
    public string ScanFileFilter { get; set; } = "";
    public List<FileInfoDto>? ScannedFiles { get; private set; }
    public HashSet<string> SelectedFilePaths { get; } = new();

    public void SetSourcePath(string path, bool mapped)
    {
        ScanPath = path;
        ScanPathMapped = mapped;
    }

    public async Task ScanAsync(CancellationToken ct = default)
    {
        Scanning = true;
        ScanError = null;
        ScannedFiles = null;
        SelectedFilePaths.Clear();
        session.NotifyStateChanged();

        var pathToScan = ScanPath.Trim();
        var targetSingleFile = "";

        // If user entered a direct file path, scan the parent folder and auto-select that file
        var ext = Path.GetExtension(pathToScan);
        if (!string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext.ToLowerInvariant()))
        {
            targetSingleFile = pathToScan;
            pathToScan = Path.GetDirectoryName(pathToScan)?.Replace('\\', '/') ?? pathToScan;
        }

        var result = await torrentSortService.ScanAsync(session.TorrentDownloadId, pathToScan, ct);
        if (!result.Success)
        {
            ScanError = result.ErrorMessage ?? "Scan failed.";
            Scanning = false;
            session.NotifyStateChanged();
            return;
        }

        ScannedFiles = result.Files ?? new List<FileInfoDto>();

        var nonDirs = ScannedFiles.Where(f => !f.IsDir && f.Path is not null).ToList();
        if (!string.IsNullOrEmpty(targetSingleFile))
        {
            var exactMatch = nonDirs.FirstOrDefault(f => string.Equals(f.Path, targetSingleFile, StringComparison.OrdinalIgnoreCase));
            if (exactMatch?.Path is not null)
            {
                SelectedFilePaths.Add(exactMatch.Path);
            }
            else
            {
                SelectMatchedFiles();
            }
        }
        else
        {
            SelectMatchedFiles();
            if (SelectedFilePaths.Count == 0)
            {
                SelectVideoFiles();
            }
        }

        Scanning = false;
        session.NotifyStateChanged();
    }

    public void ToggleFileSelection(string path, bool isSelected)
    {
        if (isSelected) SelectedFilePaths.Add(path);
        else SelectedFilePaths.Remove(path);
        session.NotifyStateChanged();
    }

    public void SelectMatchedFiles()
    {
        SelectedFilePaths.Clear();
        if (ScannedFiles is not null)
        {
            foreach (var f in ScannedFiles.Where(f => !f.IsDir && f.Matched && f.Path is not null))
            {
                SelectedFilePaths.Add(f.Path!);
            }
        }
        session.NotifyStateChanged();
    }

    public void SelectVideoFiles()
    {
        SelectedFilePaths.Clear();
        if (ScannedFiles is not null)
        {
            foreach (var f in ScannedFiles.Where(f => !f.IsDir && f.Path is not null && IsVideoFile(f.Name)))
            {
                SelectedFilePaths.Add(f.Path!);
            }
        }
        session.NotifyStateChanged();
    }

    public void SelectAllFiles()
    {
        SelectedFilePaths.Clear();
        if (ScannedFiles is not null)
        {
            foreach (var f in ScannedFiles.Where(f => !f.IsDir && f.Path is not null))
            {
                SelectedFilePaths.Add(f.Path!);
            }
        }
        session.NotifyStateChanged();
    }

    public void DeselectAllFiles()
    {
        SelectedFilePaths.Clear();
        session.NotifyStateChanged();
    }

    public void GoToDestination()
    {
        session.Step = WizardStep.Destination;
        session.NotifyStateChanged();
    }

    public static bool IsVideoFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var ext = Path.GetExtension(name);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext.ToLowerInvariant());
    }
}
