using Javbuddy.Services.Common;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.VrMerge;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 0 of the TorrentSort wizard: detecting and merging multi-part VR releases before
/// the scan. Owns the merge job poll loop.</summary>
public sealed class MergeStep(
    WizardSession session,
    IPathMappingService pathMappingService,
    IVrMergeService vrMergeService,
    IVrMergeJobTracker vrMergeJobTracker,
    IFfmpegBinaryResolver ffmpegBinaryResolver,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private CancellationTokenSource? mergePollCts;

    public bool FfmpegAvailable { get; private set; }
    public bool DetectingMergeCandidate { get; private set; }
    public string MergeSourcePath { get; set; } = "";
    public List<string> MergeSourcePathAttempts { get; } = [];
    public VrMergeCandidate? MergeCandidate { get; private set; }
    public List<VrMergeCandidatePart> MergeAllParts { get; private set; } = [];
    public HashSet<string> MergeExcludedPaths { get; } = [];
    public string MergeCodeBase { get; set; } = "";
    public bool Merging { get; private set; }
    public FfmpegProgress? MergeProgress { get; private set; }
    public string? MergeError { get; private set; }
    public string? MergedFilePath { get; private set; }

    public IReadOnlyList<VrMergeCandidatePart> MergeIncludedPartsInOrder =>
        MergeAllParts.Where(p => !MergeExcludedPaths.Contains(p.Path)).ToList();

    /// <summary>Resolves the download folder as Javbuddy's own process sees it and, if ≥2 VR parts
    /// are detected there, shows the Merge step ahead of Scan. Reattaches to a merge already running
    /// in VrMergeJobTracker (e.g. after navigating away and back) rather than starting a second one.
    /// Tries three candidate paths before giving up and letting the user type one in directly.</summary>
    public async Task InitializeAsync(string rawSavePath, string scanPath, CancellationToken ct)
    {
        FfmpegAvailable = ffmpegBinaryResolver.IsAvailable;

        var existingMergeJob = vrMergeJobTracker.Get(session.TorrentDownloadId);
        if (existingMergeJob is { Task.IsCompleted: false })
        {
            session.Step = WizardStep.Merge;
            Merging = true;
            AttachToMergeJob(existingMergeJob);
            return;
        }

        if (!FfmpegAvailable) return; // ffmpeg missing: nothing to offer here, stay on the default Scan step

        MergeSourcePathAttempts.Clear();
        var appPath = await pathMappingService.TranslateToAppPathAsync(rawSavePath, ct);
        if (!string.IsNullOrWhiteSpace(appPath)) MergeSourcePathAttempts.Add(appPath);
        var resolved = !string.IsNullOrWhiteSpace(appPath) && Directory.Exists(appPath) ? appPath : null;

        if (resolved is null && !string.IsNullOrWhiteSpace(scanPath))
        {
            MergeSourcePathAttempts.Add(scanPath);
            if (Directory.Exists(scanPath)) resolved = scanPath;
        }

        MergeSourcePath = resolved ?? "";

        if (resolved is not null)
        {
            await DetectMergeCandidateAsync(ct);
            if (MergeCandidate is null || MergeCandidate.Parts.Count < 2)
            {
                return; // nothing to merge here — stay on the default Scan step
            }
        }

        // Either ≥2 parts were found, or no folder could be resolved at all — in the latter case the
        // step itself lets the user type an override path and detect manually.
        session.Step = WizardStep.Merge;
    }

    public async Task DetectMergeCandidateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(MergeSourcePath))
        {
            MergeError = "Enter a folder path to scan for parts.";
            session.NotifyStateChanged();
            return;
        }

        DetectingMergeCandidate = true;
        MergeError = null;
        session.NotifyStateChanged();

        var candidate = await vrMergeService.DetectAsync(MergeSourcePath, ct);
        MergeCandidate = candidate;
        MergeAllParts = candidate?.Parts.ToList() ?? [];
        MergeExcludedPaths.Clear();
        if (candidate is not null) MergeCodeBase = candidate.CodeBase;

        if (candidate is null)
        {
            MergeError = $"No multi-part VR release detected at \"{MergeSourcePath}\".";
        }

        DetectingMergeCandidate = false;
        session.NotifyStateChanged();
    }

    public string? ChapterTitleFor(string path)
    {
        var included = MergeIncludedPartsInOrder;
        for (var i = 0; i < included.Count; i++)
        {
            if (included[i].Path == path) return VrPartDetector.ChapterTitle(MergeCodeBase, i);
        }

        return null;
    }

    public void ToggleMergePart(string path)
    {
        MergeExcludedPaths.Toggle(path);
        session.NotifyStateChanged();
    }

    public void MoveMergePart(int from, int to)
    {
        if (from < 0 || from >= MergeAllParts.Count || to < 0 || to >= MergeAllParts.Count || from == to) return;

        var item = MergeAllParts[from];
        MergeAllParts.RemoveAt(from);
        MergeAllParts.Insert(to, item);
        session.NotifyStateChanged();
    }

    public void SkipMerge()
    {
        session.Step = WizardStep.Scan;
        session.NotifyStateChanged();
    }

    public Task StartMergeAsync()
    {
        var included = MergeIncludedPartsInOrder;
        if (included.Count < 2 || string.IsNullOrWhiteSpace(MergeSourcePath))
        {
            MergeError = "At least two parts must be selected to merge.";
            session.NotifyStateChanged();
            return Task.CompletedTask;
        }

        Merging = true;
        MergeError = null;
        MergeProgress = null;
        session.NotifyStateChanged();

        var orderedWithTitles = included
            .Select((p, i) => p with { ChapterTitle = VrPartDetector.ChapterTitle(MergeCodeBase, i) })
            .ToList();
        var folderPath = MergeSourcePath;
        var codeBase = MergeCodeBase;

        var job = vrMergeJobTracker.GetOrStart(session.TorrentDownloadId, session.Torrent?.MovieCode ?? "movie sort", (progress, jobCt) =>
            vrMergeService.MergeAsync(
                new VrMergeRequest { FolderPath = folderPath, CodeBase = codeBase, OrderedParts = orderedWithTitles },
                progress,
                jobCt));

        AttachToMergeJob(job);
        return Task.CompletedTask;
    }

    public void CancelMerge()
    {
        vrMergeJobTracker.Cancel(session.TorrentDownloadId);
        session.NotifyStateChanged();
    }

    private void AttachToMergeJob(VrMergeJob job)
    {
        mergePollCts?.Cancel();
        mergePollCts = new CancellationTokenSource();
        _ = MergePollLoopAsync(job, mergePollCts.Token);
    }

    private async Task MergePollLoopAsync(VrMergeJob job, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
        try
        {
            while (job.Task is { IsCompleted: false })
            {
                MergeProgress = job.LatestProgress;
                session.NotifyStateChanged();
                if (!await timer.WaitForNextTickAsync(ct)) return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        MergeProgress = job.LatestProgress;
        Merging = false;

        try
        {
            var result = job.Task is null ? null : await job.Task;
            if (result is null)
            {
                MergeError = "Merge job vanished unexpectedly.";
            }
            else if (result.Success)
            {
                MergedFilePath = result.MergedFilePath;
                session.Step = WizardStep.Scan;
            }
            else
            {
                MergeError = result.ErrorMessage ?? "Merge failed.";
            }
        }
        catch (OperationCanceledException)
        {
            MergeError = "Merge cancelled.";
        }

        session.NotifyStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (mergePollCts is not null)
        {
            await mergePollCts.CancelAsync();
            mergePollCts.Dispose();
            mergePollCts = null;
        }
    }
}
