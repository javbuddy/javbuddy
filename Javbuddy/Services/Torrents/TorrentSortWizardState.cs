using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Services.VrMerge;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents;

public enum WizardStep
{
    Merge,
    Scan,
    Destination,
    Scraping,
    Review,
    Preview,
    Done
}

/// <summary>Thin coordinator for the TorrentSort wizard page: owns one part per step (see
/// <c>Services/Torrents/SortWizard/</c>) and the <see cref="WizardSession"/> they share, does the
/// initial load, and holds the few actions that span parts. The page binds to the parts
/// (<c>State.Merge</c>, <c>State.Scan</c>, …) and to the shared session data forwarded here.</summary>
public sealed class TorrentSortWizardState : IAsyncDisposable
{
    private readonly ITorrentSortService torrentSortService;
    private readonly IPathMappingService pathMappingService;
    private readonly ILocalLibraryClient localLibraryClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory;

    public TorrentSortWizardState(
        ITorrentSortService torrentSortService,
        IPathMappingService pathMappingService,
        ILocalLibraryClient localLibraryClient,
        IDbContextFactory<AppDbContext> dbFactory,
        IConfiguration configuration,
        IVrMergeService vrMergeService,
        IVrMergeJobTracker vrMergeJobTracker,
        IFfmpegBinaryResolver ffmpegBinaryResolver,
        TaskActivityTracker? activities = null,
        TimeProvider? timeProvider = null,
        ISortEditorLookupService? sortEditorLookup = null)
    {
        this.torrentSortService = torrentSortService;
        this.pathMappingService = pathMappingService;
        this.localLibraryClient = localLibraryClient;
        this.dbFactory = dbFactory;

        var time = timeProvider ?? TimeProvider.System;
        Session = new WizardSession(activities);
        Merge = new MergeStep(Session, pathMappingService, vrMergeService, vrMergeJobTracker, ffmpegBinaryResolver, time);
        Scan = new ScanStep(Session, torrentSortService);
        Scrape = new ScrapeStep(Session, torrentSortService, configuration, time);
        Review = new ReviewStep(Session, torrentSortService);
        EditModal = new MovieEditModalState(Session, torrentSortService, dbFactory, sortEditorLookup ?? NullSortEditorLookup.Instance, time, Review.RefreshJobDataAsync);
        Preview = new PreviewStep(Session, torrentSortService);
        Organize = new OrganizeStep(Session, torrentSortService, time);
        Cleanup = new CleanupStep(Session, torrentSortService);
    }

    public WizardSession Session { get; }
    public MergeStep Merge { get; }
    public ScanStep Scan { get; }
    public ScrapeStep Scrape { get; }
    public ReviewStep Review { get; }
    public MovieEditModalState EditModal { get; }
    public PreviewStep Preview { get; }
    public OrganizeStep Organize { get; }
    public CleanupStep Cleanup { get; }

    public event Action? OnChange
    {
        add => Session.Changed += value;
        remove => Session.Changed -= value;
    }

    public int TorrentDownloadId => Session.TorrentDownloadId;
    public TorrentDownload? Torrent => Session.Torrent;
    public WizardStep Step { get => Session.Step; set => Session.Step = value; }
    public BatchJobResponseDto? JobData => Session.JobData;
    public string Destination { get => Session.Destination; set => Session.Destination = value; }
    public string? ReviewError => Session.ReviewError;
    public string? LocalRootHint { get; private set; }
    public string? JavinizerBaseUrl { get; private set; }

    public async Task InitializeAsync(int torrentDownloadId, CancellationToken ct = default)
    {
        Session.TorrentDownloadId = torrentDownloadId;
        JavinizerBaseUrl = await torrentSortService.GetJavinizerBaseUrlAsync(ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var torrent = await db.TorrentDownloads.FindAsync([torrentDownloadId], ct);
        Session.Torrent = torrent;
        if (torrent is null)
        {
            Session.NotifyStateChanged();
            return;
        }

        var rawSavePath = !string.IsNullOrWhiteSpace(torrent.ContentPath) ? torrent.ContentPath : (torrent.SavePath ?? "");
        var scanPath = await pathMappingService.TranslateAsync(rawSavePath, ct);
        Scan.SetSourcePath(scanPath, !string.IsNullOrWhiteSpace(rawSavePath) && scanPath != rawSavePath);

        if (!string.IsNullOrWhiteSpace(torrent.MovieCode))
        {
            LocalRootHint = await localLibraryClient.ResolveRootForCodeAsync(torrent.MovieCode, ct);
        }

        Scrape.LoadDestinationAliases();

        if (!string.IsNullOrWhiteSpace(torrent.JavinizerBatchJobId))
        {
            await Scrape.ResumeFromExistingJobAsync(ct);
            Scrape.MatchDestinationToAlias();
        }
        else
        {
            Scrape.ApplyDefaultDestination();
            await Merge.InitializeAsync(rawSavePath, scanPath, ct);
        }

        Session.NotifyStateChanged();
    }

    /// <summary>Sends the files selected in the Scan step to the batch scrape.</summary>
    public Task StartBatchScrapeAsync(CancellationToken ct = default) =>
        Scrape.StartBatchScrapeAsync(Scan.SelectedFilePaths, ct);

    public void RestartScan()
    {
        Session.Step = WizardStep.Scan;
        Session.ReviewError = null;
        Preview.ClearError();
        Organize.ClearError();
        Session.NotifyStateChanged();
    }

    public string GetJavinizerJobUrl(string jobId) =>
        $"{JavinizerBaseUrl?.TrimEnd('/')}/review/{Uri.EscapeDataString(jobId)}?view=detail";

    public string StepClass(WizardStep s)
    {
        var effectiveStep = Organize.Organizing ? WizardStep.Done : Step;
        return s == effectiveStep ? "sort-step-active" : s < effectiveStep ? "sort-step-done" : "";
    }

    public static string StatusBadgeClass(string status) => status switch
    {
        JavinizerJobStatus.Failed => "sort-badge-unmatched",
        JavinizerJobStatus.Completed or JavinizerJobStatus.Organized => "sort-badge-matched",
        _ => ""
    };

    public async ValueTask DisposeAsync()
    {
        await Scrape.DisposeAsync();
        await Merge.DisposeAsync();
        await Organize.DisposeAsync();
    }
}
