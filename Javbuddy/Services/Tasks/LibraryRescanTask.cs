using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Periodic full sync between the configured local library folders and the tracked Movies
/// table — the automatic counterpart to LibraryImport.razor's manual "Discover from Local Library"
/// button, plus the MediaInfo technical rescan that used to be its own task. One run does seven
/// things against the same folder-name listing:
///
/// 1. Discovers new movies: any top-level folder with no matching tracked Movie is imported as Got,
///    exactly like LibraryImport.razor's Discover flow (create the row, then apply local .nfo/image
///    metadata onto it).
/// 2. Reverts deleted movies: any Got movie whose folder no longer exists goes back to Missing, with
///    its local-file-derived fields cleared via LocalLibraryMetadataMapper.ClearLocalFileData —
///    descriptive Meta* fields are left alone since they're not tied to the file's presence. Logs a
///    debug message per reverted movie.
/// 3. Syncs the sidecar-subtitle-file flag for every Got movie, every run — a subtitle file can be
///    dropped into an already-fully-probed movie's folder at any time, so this can't share MediaInfo
///    probing's retry gate below. Logs a debug message when the flag actually flips either way.
/// 4. Syncs the trailer-file flag the same way (RefreshTrailerFlagOnlyAsync) — a "{code}-trailer.{ext}"
///    file next to the movie's own video file.
/// 5. Probes MediaInfo, including detecting a video file upgrade/replacement: every Got movie gets a
///    cheap file-size/last-write-time check first (RefreshMediaInfoOnlyAsync), and only pays for the
///    expensive native probe when that changed since the last successful probe, or there wasn't one
///    yet. Logs a debug message when the resulting resolution actually differs from what was
///    previously known.
/// 6. Syncs descriptive metadata when a movie's .nfo file was edited (RefreshLocalMetadataIfNfoChangedAsync):
///    if the movie has no descriptive metadata yet, populates it; if the movie already has metadata in
///    Javbuddy, preserves it (Javbuddy has absolute authority) and records observation time.
/// 7. Syncs a poster/fanart/extrafanart signature (SyncImagesSignatureAsync) — detection only; an
///    actual change is left for ImageCacheTask's own daily recache to pick up, this just logs
///    a debug message noticing it.
/// 8. Detects metadata conflicts between local .nfo files and Javbuddy (DetectConflictsAsync).
///
/// Steps 5-7 use a null-stored-value-means-"never observed, establish the baseline, don't log or
/// re-apply" rule (see each LocalLibraryClient method) specifically so rolling this feature out to
/// an already-fully-scanned library doesn't fire a false "changed" event. Info in Javbuddy is authoritative.
///
/// Steps 1 and 2 both hinge on knowing which folders currently exist — if every configured root is
/// unreachable (e.g. a network share briefly unmounted), that would read as "the whole library was
/// deleted" and revert every Got movie at once. AnyRootReachableAsync guards against that: both
/// steps are skipped entirely for a run where no root can be reached, leaving the tracked library
/// untouched until the share comes back. Steps 3-7 stay safe to run regardless — each movie's own
/// folder lookup just comes back "not found" and leaves that movie's fields untouched.</summary>
public class LibraryRescanTask(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    ILogger<LibraryRescanTask> logger,
    INfoSyncService? nfoSyncService = null,
    MovieChangeNotifier? movieChangeNotifier = null,
    TimeSpan? movieChangeNotifyInterval = null) : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);
    private const int MaxConcurrency = 4;

    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly ILogger<LibraryRescanTask> logger = logger;
    private readonly INfoSyncService? nfoSyncService = nfoSyncService;
    private readonly MovieChangeNotifier? movieChangeNotifier = movieChangeNotifier;
    private readonly TimeSpan notifyThrottle = movieChangeNotifyInterval ?? TimeSpan.FromSeconds(2);

    public string Name => "Library Rescan";

    public string Description =>
        "Syncs the tracked Movies against your local library folders: discovers new movies, reverts " +
        "deleted ones to Missing, and detects video/subtitle/trailer/.nfo/image changes on existing ones.";

    public TimeSpan GetInterval() => DefaultInterval;

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        if (!await localLibraryClient.AnyRootReachableAsync(ct))
        {
            var subtitlesWhileUnreachable = await SyncSubtitleFlagsAsync(progress, ct);
            var trailersWhileUnreachable = await SyncTrailerFlagsAsync(progress, ct);
            var probedWhileUnreachable = await ProbeMediaInfoAsync(progress, ct);
            var nfoWhileUnreachable = await SyncNfoAsync(progress, ct);
            var imagesWhileUnreachable = await SyncImagesAsync(progress, ct);
            movieChangeNotifier?.NotifyChanged();
            return "no configured local library root is currently reachable — skipped new/removed movie detection; "
                + subtitlesWhileUnreachable + "; " + trailersWhileUnreachable + "; " + probedWhileUnreachable + "; " + nfoWhileUnreachable + "; " + imagesWhileUnreachable;
        }

        // Each stage below reports its own Current/Total range starting back at 0 — a bare
        // Current/Total without a Stage label would otherwise look like progress went backwards
        // partway through a run. Reported unconditionally before each stage (not just from inside
        // its per-item loop) so the label still updates even for a stage with zero items to
        // process (e.g. "Importing new movies" when nothing new was found) instead of leaving the
        // previous stage's label stuck on screen.
        progress.Report(new TaskProgress(0, null, "Scanning local library folders"));
        var folderCodes = await localLibraryClient.ListMovieCodesAsync(ct);
        var folderCodeSet = new HashSet<string>(folderCodes, StringComparer.OrdinalIgnoreCase);

        progress.Report(new TaskProgress(0, null, "Importing new movies"));
        var imported = await ImportNewMoviesAsync(folderCodes, progress, ct);

        progress.Report(new TaskProgress(0, null, "Reverting deleted movies"));
        var reverted = await RevertDeletedMoviesAsync(folderCodeSet, ct);

        var subtitlesSynced = await SyncSubtitleFlagsAsync(progress, ct);
        var trailersSynced = await SyncTrailerFlagsAsync(progress, ct);
        var probed = await ProbeMediaInfoAsync(progress, ct);
        var nfoSynced = await SyncNfoAsync(progress, ct);
        var imagesSynced = await SyncImagesAsync(progress, ct);
        var conflictsDetected = await DetectConflictsAsync(progress, ct);

        movieChangeNotifier?.NotifyChanged();

        return $"{folderCodes.Count} folders scanned, {imported} new movie(s) imported, "
            + $"{reverted} movie(s) reverted to Missing (file no longer found), {subtitlesSynced}, {trailersSynced}, {probed}, {nfoSynced}, {imagesSynced}"
            + (conflictsDetected is not null ? $", {conflictsDetected}" : "");
    }

    /// <summary>Mirrors LibraryImport.razor's DiscoverFromLocalLibrary: any folder code with no
    /// existing tracked Movie (regardless of that movie's status) is imported as Got.</summary>
    private async Task<int> ImportNewMoviesAsync(List<string> folderCodes, IProgress<TaskProgress> progress, CancellationToken ct)
    {
        HashSet<string> existingCodes;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var existing = await db.Movies.Where(m => m.Code != null).Select(m => m.Code!).ToListAsync(ct);
            existingCodes = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var newCodes = folderCodes.Where(c => !existingCodes.Contains(c)).ToList();
        if (newCodes.Count == 0) return 0;

        var imported = 0;
        var processed = 0;
        var lastMovieChangeNotifyAt = DateTime.UtcNow;
        var notifyLock = new object();
        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        var tasks = newCodes.Select(async code =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ImportOneAsync(code, ct);
                Interlocked.Increment(ref imported);

                if (movieChangeNotifier is not null)
                {
                    var now = DateTime.UtcNow;
                    var shouldNotify = false;
                    lock (notifyLock)
                    {
                        if (now - lastMovieChangeNotifyAt >= notifyThrottle)
                        {
                            lastMovieChangeNotifyAt = now;
                            shouldNotify = true;
                        }
                    }
                    if (shouldNotify)
                    {
                        movieChangeNotifier.NotifyChanged();
                    }
                }
            }
            // One folder's I/O trouble (permission-denied, a corrupt/inaccessible entry — see
            // ProbeMediaInfoAsync's comment below) must not take out the whole batch via
            // Task.WhenAll; just leave that code untracked for this run and retry it next time.
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                var done = Interlocked.Increment(ref processed);
                progress.Report(new TaskProgress(done, newCodes.Count, "Importing new movies"));
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (imported > 0)
        {
            movieChangeNotifier?.NotifyChanged();
        }

        return imported;
    }

    private async Task ImportOneAsync(string code, CancellationToken ct)
    {
        var movie = new Movie { Code = code, Status = MovieStatus.Got };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Movies.Add(movie);
            await DeletedMovieHistory.ClearAsync(db, code, ct);
            await db.SaveChangesAsync(ct);
        }

        var local = await localLibraryClient.TryGetMetadataAsync(code, ct);
        if (!local.Found || local.Metadata is null) return;

        await using var trackedDb = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await trackedDb.Movies.FindAsync(new object[] { movie.Id }, ct);
        if (tracked is null) return;

        LocalLibraryMetadataMapper.Apply(tracked, local.Metadata);
        await MovieActorAssociation.SynchronizeAsync(trackedDb, tracked, ct);
        await TagNormalization.ApplyToMovieAsync(trackedDb, tracked, local.Metadata.Genres, ct);
        await trackedDb.SaveChangesAsync(ct);
    }

    /// <summary>Reverts any Got movie whose folder isn't in the current listing back to Missing,
    /// clearing the local-file-derived fields that no longer describe anything real, and logs a
    /// debug message per reverted movie so the revert isn't silent.</summary>
    private async Task<int> RevertDeletedMoviesAsync(HashSet<string> folderCodeSet, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var gotMovies = await db.Movies.Where(m => m.Status == MovieStatus.Got && m.Code != null).ToListAsync(ct);

        var reverted = 0;
        foreach (var movie in gotMovies)
        {
            if (folderCodeSet.Contains(movie.Code!)) continue;

            movie.Status = MovieStatus.Missing;
            LocalLibraryMetadataMapper.ClearLocalFileData(movie);
            logger.LogDebug("Local file no longer found for {Code} — reverted to Missing", movie.Code);
            reverted++;
        }

        if (reverted > 0)
        {
            await db.SaveChangesAsync(ct);
            movieChangeNotifier?.NotifyChanged();
        }
        return reverted;
    }

    /// <summary>Recomputes the sidecar-subtitle-file flag for every Got movie, every run — see the
    /// class doc for why this can't share MediaInfo probing's retry gate. Cheap directory listing
    /// per movie (no MediaInfoLib native call), so redoing it unconditionally for the whole library
    /// on every scheduled run is fine.</summary>
    private async Task<string> SyncSubtitleFlagsAsync(IProgress<TaskProgress> progress, CancellationToken ct) =>
        await RunPerMovieAsync(progress, ct, localLibraryClient.RefreshSubtitleFlagOnlyAsync, "Syncing subtitle flags", "synced subtitle flag for");

    /// <summary>Recomputes the trailer-file flag for every Got movie, every run — same reasoning as
    /// SyncSubtitleFlagsAsync (a trailer file can appear/disappear at any time, independent of
    /// MediaInfo's retry gate).</summary>
    private async Task<string> SyncTrailerFlagsAsync(IProgress<TaskProgress> progress, CancellationToken ct) =>
        await RunPerMovieAsync(progress, ct, localLibraryClient.RefreshTrailerFlagOnlyAsync, "Syncing trailer flags", "synced trailer flag for");

    /// <summary>Probes MediaInfo (and, since RefreshMediaInfoOnlyAsync now checks the video file's
    /// size/last-write time first, detects a video file upgrade/replacement too) — see the class
    /// doc. Runs against every Got movie, not a MediaScannedAt/MediaScanError-filtered subset,
    /// since that eligibility check now lives inside RefreshMediaInfoOnlyAsync itself (cheap to call
    /// even when it turns out there's nothing to do).
    ///
    /// A folder that exists but is unreadable (a
    /// permission-denied entry) throws UnauthorizedAccessException out of RefreshMediaInfoOnlyAsync. Left uncaught,
    /// that exception would fail the shared Task.WhenAll below and abort probing for every other
    /// movie in the batch too — one bad folder is counted as failed instead (see RunPerMovieAsync).</summary>
    private async Task<string> ProbeMediaInfoAsync(IProgress<TaskProgress> progress, CancellationToken ct) =>
        await RunPerMovieAsync(progress, ct, localLibraryClient.RefreshMediaInfoOnlyAsync, "Probing MediaInfo", "probed", "MediaInfo for");

    /// <summary>Re-applies descriptive metadata when a movie's .nfo file was edited since the last
    /// time this was checked — see the class doc for the null-baseline guard against a whole-library
    /// false-positive on rollout.</summary>
    private async Task<string> SyncNfoAsync(IProgress<TaskProgress> progress, CancellationToken ct) =>
        await RunPerMovieAsync(progress, ct, localLibraryClient.RefreshLocalMetadataIfNfoChangedAsync, "Syncing .nfo files", "checked", ".nfo files for");

    /// <summary>Detects a poster/fanart/extrafanart change (add/remove/replace) — see the class doc;
    /// actually recaching is left to ImageCacheTask's own daily freshness check.</summary>
    private async Task<string> SyncImagesAsync(IProgress<TaskProgress> progress, CancellationToken ct) =>
        await RunPerMovieAsync(progress, ct, localLibraryClient.SyncImagesSignatureAsync, "Syncing images", "checked images for");

    private async Task<string?> DetectConflictsAsync(IProgress<TaskProgress> progress, CancellationToken ct)
    {
        if (nfoSyncService is null) return null;
        var result = await nfoSyncService.DetectAllMovieConflictsAsync(progress, ct);
        return $"{result.ConflictsFoundCount} .nfo conflict(s) detected";
    }

    /// <summary>Shared shape for every per-movie sync phase above: query every Got movie with a
    /// code, call the given per-movie operation with bounded concurrency, and count successes —
    /// resilient to any one movie's I/O trouble (see ProbeMediaInfoAsync's doc) rather than letting
    /// it abort the whole batch via Task.WhenAll.</summary>
    private async Task<string> RunPerMovieAsync(
        IProgress<TaskProgress> progress,
        CancellationToken ct,
        Func<int, CancellationToken, Task<LocalRefreshResult>> operation,
        string stage,
        string verb,
        string? subject = null)
    {
        progress.Report(new TaskProgress(0, null, stage));

        List<int> movieIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            movieIds = await db.Movies
                .Where(m => m.Status == MovieStatus.Got && m.Code != null)
                .Select(m => m.Id)
                .ToListAsync(ct);
        }

        var processed = 0;
        var succeeded = 0;
        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        var tasks = movieIds.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await operation(id, ct);
                if (result.Success) Interlocked.Increment(ref succeeded);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                var done = Interlocked.Increment(ref processed);
                progress.Report(new TaskProgress(done, movieIds.Count, stage));
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var subjectPart = subject is null ? "" : $" {subject}";
        return $"{verb}{subjectPart} {succeeded}/{processed} Got movies";
    }
}
