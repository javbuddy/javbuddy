using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.QBittorrent;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Javbuddy.Services.Torrents;

public record TorrentScanResult(bool Success, List<FileInfoDto>? Files, string? ErrorMessage);
public record TorrentBatchScrapeStartResult(bool Success, string? JobId, string? ErrorMessage);
public record TorrentOrganizeResult(bool Success, string? ErrorMessage);

public record TorrentCleanupResult(bool Success, string? ErrorMessage);

/// <summary>The poster to crop, with its pixel size (crop bounds are measured on it). Normally
/// javinizer-go's full-size <c>{id}-full.jpg</c>; <paramref name="IsFullSize"/> is false when only
/// the already-cropped <c>{id}.jpg</c> exists (a legacy envelope), which javinizer-go's crop
/// endpoint then crops itself but stores no crop geometry for.</summary>
public record PosterCropSource(byte[] Bytes, string ContentType, int Width, int Height, bool IsFullSize = true);

/// <summary>Preview of what <see cref="ITorrentSortService.CleanupSourceAsync"/> would delete —
/// purely informational (read-only filesystem listing); the actual delete always goes through
/// qBittorrent's own API, which is independently responsible for only touching this torrent's own
/// files. <paramref name="EntryPaths"/> holds every file/directory under
/// <paramref name="ResolvedPath"/>, relative to it (directories suffixed "/"), or just
/// <paramref name="ResolvedPath"/>'s own name when it resolves to a single file rather than a
/// folder. Note this can overstate what qBittorrent will actually remove when the torrent has no
/// dedicated subfolder (ContentPath == SavePath, a directory shared with other torrents) — safe
/// direction to be wrong in, since nothing here ever deletes the filesystem itself.</summary>
public record TorrentCleanupPreviewResult(bool Success, string? ResolvedPath, List<string>? EntryPaths, string? ErrorMessage);

public interface ITorrentSortService
{
    Task<TorrentScanResult> ScanAsync(int torrentDownloadId, string path, CancellationToken ct = default);
    Task<TorrentBatchScrapeStartResult> StartBatchScrapeAsync(int torrentDownloadId, List<string> filePaths, string destination, CancellationToken ct = default);
    Task<JavinizerBatchJobResult> PollJobAsync(int torrentDownloadId, bool includeData = false, CancellationToken ct = default);
    Task<JavinizerOrganizePreviewResult> PreviewAsync(int torrentDownloadId, string resultId, string destination, CancellationToken ct = default);

    /// <summary>Organize preview of unsaved edits: the metadata editor's live rename preview.</summary>
    Task<JavinizerOrganizePreviewResult> PreviewWithEditsAsync(int torrentDownloadId, string resultId, string destination, MovieViewDto movie, CancellationToken ct = default);
    Task<TorrentOrganizeResult> OrganizeAsync(int torrentDownloadId, string destination, CancellationToken ct = default);
    Task<JavinizerRescrapeResult> RescrapeAsync(int torrentDownloadId, string resultId, string manualSearchInput, CancellationToken ct = default);
    Task<JavinizerExcludeResult> ExcludeResultAsync(int torrentDownloadId, string resultId, CancellationToken ct = default);
    /// <summary>PATCHes the whole result. <paramref name="expectedResultRevision"/> (the result's
    /// revision) is required by javinizer-go when the cast changes.</summary>
    Task<JavinizerUpdateResult> UpdateResultAsync(int torrentDownloadId, string resultId, MovieViewDto movie, CancellationToken ct = default, ulong? expectedResultRevision = null);
    Task<JavinizerPosterFromUrlResult> SetPosterFromUrlAsync(int torrentDownloadId, string resultId, string url, CancellationToken ct = default);

    /// <summary>Re-scrape limited to <paramref name="scrapers"/> (the editor's "Re-scrape with").</summary>
    Task<JavinizerRescrapeResult> RescrapeWithScrapersAsync(int torrentDownloadId, string resultId, string manualSearchInput, IReadOnlyList<string> scrapers, CancellationToken ct = default);

    /// <summary>Each scraper's raw values for a result, for the editor's source viewer.</summary>
    Task<JavinizerSourcesResult> GetResultSourcesAsync(int torrentDownloadId, string resultId, CancellationToken ct = default);

    /// <summary>Takes one field's value from another source; javinizer-go persists it immediately.</summary>
    Task<JavinizerFieldOverrideResult> OverrideFieldAsync(int torrentDownloadId, string resultId, string field, string source, CancellationToken ct = default);

    Task<JavinizerScrapersResult> GetScrapersAsync(CancellationToken ct = default);

    /// <summary>Loads the full-size poster javinizer-go crops from, for the editor's crop dialog.</summary>
    Task<(PosterCropSource? Source, string? Error)> GetPosterCropSourceAsync(int torrentDownloadId, string movieId, CancellationToken ct = default);

    /// <summary>Applies a manual crop drawn as a normalized rectangle on a source of the given pixel size.</summary>
    Task<JavinizerPosterCropResult> CropPosterAsync(int torrentDownloadId, string resultId, NormalizedCropRect rect, int sourceWidth, int sourceHeight, CancellationToken ct = default);
    Task<string?> GetJavinizerBaseUrlAsync(CancellationToken ct = default);

    /// <summary>Resolves the torrent's source folder (or single file) and lists everything under
    /// it, so the Done step can show the user exactly what a cleanup would delete before they
    /// confirm it.</summary>
    Task<TorrentCleanupPreviewResult> PreviewCleanupAsync(int torrentDownloadId, CancellationToken ct = default);

    /// <summary>Deletes the torrent and its files through qBittorrent's own API (requires the row
    /// to have a known qBittorrent hash) — the only cleanup mechanism offered, since qBittorrent
    /// itself is the only thing that reliably knows which files belong to this specific torrent
    /// as opposed to a directly-deleted filesystem path, which risks removing a folder shared with
    /// other torrents when this one has no dedicated subfolder. Also marks the row removed from
    /// the client, matching ActivityQueue's own Remove flow.</summary>
    Task<TorrentCleanupResult> CleanupSourceAsync(int torrentDownloadId, CancellationToken ct = default);
}

/// <summary>Orchestrates the torrent sorting wizard (TorrentSort.razor) against javinizer-go's
/// batch-job API: resolves a TorrentDownload row to its javinizer-go job id, calls the external
/// API, and writes the row/Movie back — the same "resolve state → call external API → write DB"
/// shape TorrentGrabService already uses for grabbing.</summary>
public class TorrentSortService(
    IJavinizerClient javinizerClient,
    ILocalLibraryClient localLibraryClient,
    IJellyfinClient jellyfinClient,
    IPathMappingService pathMappingService,
    IQBittorrentClient qBittorrentClient,
    IDbContextFactory<AppDbContext> dbFactory,
    TorrentChangeNotifier changeNotifier,
    ILogger<TorrentSortService> logger,
    MovieChangeNotifier? movieChangeNotifier = null) : ITorrentSortService
{
    private static readonly TimeSpan OrganizePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OrganizePollTimeout = TimeSpan.FromMinutes(45);

    private readonly IJavinizerClient javinizerClient = javinizerClient;
    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly IJellyfinClient jellyfinClient = jellyfinClient;
    private readonly IPathMappingService pathMappingService = pathMappingService;
    private readonly IQBittorrentClient qBittorrentClient = qBittorrentClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly TorrentChangeNotifier changeNotifier = changeNotifier;
    private readonly ILogger<TorrentSortService> logger = logger;
    private readonly MovieChangeNotifier? movieChangeNotifier = movieChangeNotifier;

    public async Task<TorrentScanResult> ScanAsync(int torrentDownloadId, string path, CancellationToken ct = default)
    {
        var result = await javinizerClient.ScanAsync(path, recursive: true, ct);
        return new TorrentScanResult(result.Success, result.Data?.Files, result.ErrorMessage);
    }

    public async Task<TorrentBatchScrapeStartResult> StartBatchScrapeAsync(int torrentDownloadId, List<string> filePaths, string destination, CancellationToken ct = default)
    {
        var request = new BatchScrapeRequestDto
        {
            Files = filePaths,
            Destination = destination,
            Update = true
        };

        var result = await javinizerClient.BatchScrapeAsync(request, ct);
        if (!result.Success || result.Data is null)
        {
            return new TorrentBatchScrapeStartResult(false, null, result.ErrorMessage ?? "javinizer-go did not return a job id.");
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var row = await db.TorrentDownloads.FindAsync(new object[] { torrentDownloadId }, ct);
            if (row is not null)
            {
                row.JavinizerBatchJobId = result.Data.JobId;
                await db.SaveChangesAsync(ct);
            }
        }

        return new TorrentBatchScrapeStartResult(true, result.Data.JobId, null);
    }

    public async Task<JavinizerBatchJobResult> PollJobAsync(int torrentDownloadId, bool includeData = false, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerBatchJobResult(false, null, "This torrent has no active sorting job.");
        }

        return await javinizerClient.GetBatchJobAsync(jobId, includeData, ct);
    }

    public async Task<JavinizerOrganizePreviewResult> PreviewAsync(int torrentDownloadId, string resultId, string destination, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerOrganizePreviewResult(false, null, "This torrent has no active sorting job.");
        }

        var request = new OrganizePreviewRequestDto { Destination = destination };
        return await javinizerClient.PreviewOrganizeAsync(jobId, resultId, request, ct);
    }

    public async Task<JavinizerOrganizePreviewResult> PreviewWithEditsAsync(int torrentDownloadId, string resultId, string destination, MovieViewDto movie, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerOrganizePreviewResult(false, null, "This torrent has no active sorting job.");
        }

        var request = new OrganizePreviewRequestDto { Destination = destination, Movie = movie };
        return await javinizerClient.PreviewOrganizeAsync(jobId, resultId, request, ct);
    }

    public async Task<TorrentOrganizeResult> OrganizeAsync(int torrentDownloadId, string destination, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new TorrentOrganizeResult(false, "This torrent has no active sorting job.");
        }

        var batchJob = await javinizerClient.GetBatchJobAsync(jobId, includeData: true, ct);
        if (!batchJob.Success || batchJob.Data is null)
        {
            return new TorrentOrganizeResult(false, batchJob.ErrorMessage ?? "Could not load poster settings before organizing.");
        }

        foreach (var result in batchJob.Data.Results?.Values ?? Enumerable.Empty<BatchFileResultDto>())
        {
            if (result.Movie is not { } movie || string.IsNullOrWhiteSpace(movie.PosterUrl)
                || batchJob.Data.Excluded?.GetValueOrDefault(result.ResultId) == true
                || (result.FilePath is not null && batchJob.Data.Excluded?.GetValueOrDefault(result.FilePath) == true))
            {
                continue;
            }

            // A manual crop (javinizer-go poster-crop) stores PosterCropBounds and clears the
            // auto-crop flag on purpose; forcing the flag back on would re-crop over it.
            if (movie.PosterCropBounds is not null)
            {
                continue;
            }

            var shouldCrop = !TorrentSortEditingHelper.HasCustomPoster(movie);
            if (movie.ShouldCropPoster == shouldCrop)
            {
                continue;
            }

            movie.ShouldCropPoster = shouldCrop;
            var update = await javinizerClient.UpdateResultAsync(jobId, result.ResultId, movie, ct);
            if (!update.Success)
            {
                return new TorrentOrganizeResult(false, update.ErrorMessage ?? "Could not save poster settings before organizing.");
            }
        }

        var request = new OrganizePreviewRequestDto { Destination = destination };
        var organizeResult = await javinizerClient.OrganizeAsync(jobId, request, ct);
        if (!organizeResult.Success)
        {
            return new TorrentOrganizeResult(false, organizeResult.ErrorMessage);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OrganizePollTimeout);

        try
        {
            using var timer = new PeriodicTimer(OrganizePollInterval);
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                var job = await javinizerClient.GetBatchJobAsync(jobId, includeData: false, cts.Token);
                if (!job.Success || job.Data is null)
                {
                    return new TorrentOrganizeResult(false, job.ErrorMessage ?? "Lost contact with javinizer-go while organizing.");
                }

                if (job.Data.Status == JavinizerJobStatus.Organized)
                {
                    await MarkSortedAsync(torrentDownloadId, ct);
                    await TrySyncJellyfinLibraryAsync(destination, ct);
                    return new TorrentOrganizeResult(true, null);
                }

                if (job.Data.Status == JavinizerJobStatus.Failed)
                {
                    return new TorrentOrganizeResult(false, job.Data.PersistError ?? "javinizer-go reported the organize job failed.");
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new TorrentOrganizeResult(false, "Timed out waiting for javinizer-go to finish organizing.");
        }

        return new TorrentOrganizeResult(false, "Timed out waiting for javinizer-go to finish organizing.");
    }

    public async Task<JavinizerRescrapeResult> RescrapeAsync(int torrentDownloadId, string resultId, string manualSearchInput, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerRescrapeResult(false, "This torrent has no active sorting job.");
        }

        var request = new BatchRescrapeRequestDto { ManualSearchInput = manualSearchInput, Force = true };
        return await javinizerClient.RescrapeAsync(jobId, resultId, request, ct);
    }

    public async Task<JavinizerExcludeResult> ExcludeResultAsync(int torrentDownloadId, string resultId, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerExcludeResult(false, "This torrent has no active sorting job.");
        }

        return await javinizerClient.ExcludeResultAsync(jobId, resultId, ct);
    }

    public async Task<JavinizerUpdateResult> UpdateResultAsync(int torrentDownloadId, string resultId, MovieViewDto movie, CancellationToken ct = default, ulong? expectedResultRevision = null)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerUpdateResult(false, "This torrent has no active sorting job.");
        }

        return await javinizerClient.UpdateResultAsync(jobId, resultId, movie, ct, expectedResultRevision);
    }

    public async Task<JavinizerPosterFromUrlResult> SetPosterFromUrlAsync(int torrentDownloadId, string resultId, string url, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerPosterFromUrlResult(false, "This torrent has no active sorting job.");
        }

        return await javinizerClient.SetPosterFromUrlAsync(jobId, resultId, url, ct);
    }

    public async Task<JavinizerRescrapeResult> RescrapeWithScrapersAsync(int torrentDownloadId, string resultId, string manualSearchInput, IReadOnlyList<string> scrapers, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerRescrapeResult(false, "This torrent has no active sorting job.");
        }

        var request = new BatchRescrapeRequestDto { ManualSearchInput = manualSearchInput, Force = true, SelectedScrapers = scrapers.ToList() };
        return await javinizerClient.RescrapeAsync(jobId, resultId, request, ct);
    }

    public async Task<JavinizerSourcesResult> GetResultSourcesAsync(int torrentDownloadId, string resultId, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerSourcesResult(false, null, "This torrent has no active sorting job.");
        }

        return await javinizerClient.GetResultSourcesAsync(jobId, resultId, ct);
    }

    public async Task<JavinizerFieldOverrideResult> OverrideFieldAsync(int torrentDownloadId, string resultId, string field, string source, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerFieldOverrideResult(false, null, "This torrent has no active sorting job.");
        }

        return await javinizerClient.OverrideFieldAsync(jobId, resultId, field, source, ct);
    }

    public Task<JavinizerScrapersResult> GetScrapersAsync(CancellationToken ct = default) =>
        javinizerClient.GetScrapersAsync(ct);

    public async Task<(PosterCropSource? Source, string? Error)> GetPosterCropSourceAsync(int torrentDownloadId, string movieId, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return (null, "This torrent has no active sorting job.");
        }

        var fetched = await javinizerClient.GetTempPosterAsync(jobId, movieId, fullSize: true, ct);
        var isFullSize = true;
        if (fetched.NotFound)
        {
            // A legacy envelope has no -full.jpg: crop the cropped poster instead, as javinizer-go's
            // own review UI and crop endpoint both fall back to (poster-crop-controller.ts).
            fetched = await javinizerClient.GetTempPosterAsync(jobId, movieId, fullSize: false, ct);
            isFullSize = false;
        }

        if (!fetched.Success || fetched.Bytes is null)
        {
            return (null, fetched.ErrorMessage ?? "Could not load the poster.");
        }

        using var codec = SKCodec.Create(new MemoryStream(fetched.Bytes));
        if (codec is null)
        {
            return (null, "javinizer-go returned an image Javbuddy can't read.");
        }

        return (new PosterCropSource(fetched.Bytes, fetched.ContentType ?? "image/jpeg", codec.Info.Width, codec.Info.Height, isFullSize), null);
    }

    /// <summary>Pixel bounds on the same bytes the user cropped (javinizer-go measures
    /// <c>{movieId}-full.jpg</c>, or <c>{movieId}.jpg</c> when there is none), clamped inside the image.</summary>
    public async Task<JavinizerPosterCropResult> CropPosterAsync(int torrentDownloadId, string resultId, NormalizedCropRect rect, int sourceWidth, int sourceHeight, CancellationToken ct = default)
    {
        var jobId = await GetJobIdAsync(torrentDownloadId, ct);
        if (jobId is null)
        {
            return new JavinizerPosterCropResult(false, null, "This torrent has no active sorting job.");
        }

        var x = Math.Clamp((int)Math.Round(rect.X * sourceWidth), 0, sourceWidth - 1);
        var y = Math.Clamp((int)Math.Round(rect.Y * sourceHeight), 0, sourceHeight - 1);
        var request = new PosterCropRequestDto
        {
            X = x,
            Y = y,
            Width = Math.Max(1, Math.Min((int)Math.Round(rect.Width * sourceWidth), sourceWidth - x)),
            Height = Math.Max(1, Math.Min((int)Math.Round(rect.Height * sourceHeight), sourceHeight - y))
        };
        return await javinizerClient.CropPosterAsync(jobId, resultId, request, ct);
    }

    public async Task<string?> GetJavinizerBaseUrlAsync(CancellationToken ct = default)
    {
        return await javinizerClient.GetExternalUrlAsync(ct);
    }

    public async Task<TorrentCleanupPreviewResult> PreviewCleanupAsync(int torrentDownloadId, CancellationToken ct = default)
    {
        var (resolvedPath, error) = await ResolveCleanupPathAsync(torrentDownloadId, ct);
        if (error is not null)
        {
            return new TorrentCleanupPreviewResult(false, null, null, error);
        }

        if (Directory.Exists(resolvedPath))
        {
            var entries = Directory.EnumerateFileSystemEntries(resolvedPath, "*", SearchOption.AllDirectories)
                .Select(entry => Directory.Exists(entry)
                    ? Path.GetRelativePath(resolvedPath!, entry) + "/"
                    : Path.GetRelativePath(resolvedPath!, entry))
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new TorrentCleanupPreviewResult(true, resolvedPath, entries, null);
        }

        if (File.Exists(resolvedPath))
        {
            return new TorrentCleanupPreviewResult(true, resolvedPath, [Path.GetFileName(resolvedPath)], null);
        }

        return new TorrentCleanupPreviewResult(false, resolvedPath, null, $"Path not found: {resolvedPath}");
    }

    public async Task<TorrentCleanupResult> CleanupSourceAsync(int torrentDownloadId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TorrentDownloads.FindAsync(new object[] { torrentDownloadId }, ct);
        if (row is null)
        {
            return new TorrentCleanupResult(false, "Torrent not found.");
        }

        if (string.IsNullOrWhiteSpace(row.Hash))
        {
            return new TorrentCleanupResult(false, "qBittorrent doesn't know this torrent's hash.");
        }

        var deleted = await qBittorrentClient.DeleteTorrentAsync(row.Hash, deleteFiles: true, ct);
        if (!deleted)
        {
            return new TorrentCleanupResult(false, "qBittorrent failed to delete the torrent.");
        }

        row.RemovedFromClientAt = DateTime.UtcNow;
        row.Status = TorrentDownloadStatus.Removed;
        await db.SaveChangesAsync(ct);
        changeNotifier.NotifyChanged();
        return new TorrentCleanupResult(true, null);
    }

    /// <summary>Resolves a torrent's source folder/file to Javbuddy's own process view, for
    /// <see cref="PreviewCleanupAsync"/>'s read-only listing, via the same
    /// ContentPath-preferred-over-SavePath rule the wizard's scan step uses.</summary>
    private async Task<(string? Path, string? Error)> ResolveCleanupPathAsync(int torrentDownloadId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TorrentDownloads.FindAsync(new object[] { torrentDownloadId }, ct);
        if (row is null)
        {
            return (null, "Torrent not found.");
        }

        var rawPath = !string.IsNullOrWhiteSpace(row.ContentPath) ? row.ContentPath : row.SavePath;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return (null, "No source path is known for this torrent.");
        }

        var appPath = await pathMappingService.TranslateToAppPathAsync(rawPath, ct);
        return (appPath, null);
    }

    private async Task MarkSortedAsync(int torrentDownloadId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TorrentDownloads.FindAsync(new object[] { torrentDownloadId }, ct);
        if (row is null) return;

        row.SortedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var refreshResult = await localLibraryClient.RefreshLocalMetadataAsync(row.MovieId, ct);
        if (refreshResult.Success)
        {
            await using var movieDb = await dbFactory.CreateDbContextAsync(ct);
            var movie = await movieDb.Movies.FindAsync(new object[] { row.MovieId }, ct);
            if (movie is not null)
            {
                movie.Status = MovieStatus.Got;
                await movieDb.SaveChangesAsync(ct);
                movieChangeNotifier?.NotifyChanged();
            }
        }

        changeNotifier.NotifyChanged();
    }

    /// <summary>Best-effort: matches the organize destination against the monitored folder paths
    /// of each configured Jellyfin library and refreshes the matching one, so a multi-library
    /// setup (e.g. separate "jav"/"jav-vr" libraries) gets the correct library rescanned instead
    /// of relying on Jellyfin's own periodic scan. Never throws — any failure to translate, fetch
    /// libraries, match, or refresh is logged and swallowed, since it must not fail the organize
    /// operation that already succeeded against javinizer-go.</summary>
    private async Task TrySyncJellyfinLibraryAsync(string destination, CancellationToken ct)
    {
        try
        {
            var appPath = await pathMappingService.TranslateJavinizerPathToAppPathAsync(destination, ct) ?? destination;

            var librariesResult = await jellyfinClient.GetLibrariesAsync(ct);
            if (librariesResult is null || !librariesResult.Success || librariesResult.Libraries is null)
            {
                logger.LogDebug("Skipping Jellyfin library sync for {Destination}: {Error}", destination, librariesResult?.ErrorMessage);
                return;
            }

            var library = FindLibraryForPath(librariesResult.Libraries, appPath);
            if (library?.ItemId is null)
            {
                logger.LogDebug("No Jellyfin library matched destination {Destination} (app path {AppPath})", destination, appPath);
                return;
            }

            var refreshResult = await jellyfinClient.RefreshItemAsync(library.ItemId, ct);
            if (refreshResult is not { Success: true })
            {
                logger.LogWarning("Failed to refresh Jellyfin library {LibraryName}: {Error}", library.Name, refreshResult?.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Jellyfin library sync failed for destination {Destination}", destination);
        }
    }

    /// <summary>Finds the library whose monitored path is the longest prefix match of
    /// <paramref name="path"/>, on a path-segment boundary (same rule PathMappingService uses),
    /// so a location like "/media/jav" doesn't also match "/media/javfoo/...".</summary>
    private static JellyfinVirtualFolderDto? FindLibraryForPath(List<JellyfinVirtualFolderDto> libraries, string path)
    {
        JellyfinVirtualFolderDto? best = null;
        var bestLength = -1;

        foreach (var library in libraries)
        {
            foreach (var location in library.Locations ?? [])
            {
                if (string.IsNullOrEmpty(location) || location.Length > path.Length || location.Length <= bestLength) continue;

                var isSegmentMatch = path.Length == location.Length || path[location.Length] is '/' or '\\';
                if (!isSegmentMatch || !path.StartsWith(location, StringComparison.OrdinalIgnoreCase)) continue;

                best = library;
                bestLength = location.Length;
            }
        }

        return best;
    }

    private async Task<string?> GetJobIdAsync(int torrentDownloadId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TorrentDownloads.FindAsync(new object[] { torrentDownloadId }, ct);
        return row?.JavinizerBatchJobId;
    }
}
