using System.Xml;
using System.Xml.Linq;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Tasks;
using Microsoft.EntityFrameworkCore;
using static Javbuddy.Services.Nfo.NfoActorMatching;
using static Javbuddy.Services.Nfo.NfoFieldSync;
using static Javbuddy.Services.Nfo.NfoXmlFormatter;

namespace Javbuddy.Services.Nfo;

public enum ActorNfoMovieStatus
{
    UpToDate,
    WillUpdate,
    NoNfoFound,
    ReadOnly,
    InvalidXml,
    NoMatchingActorInNfo,
    Error
}

public sealed record ActorNfoMoviePreview(
    int MovieId,
    string MovieCode,
    string? MovieTitle,
    string? NfoPath,
    ActorNfoMovieStatus Status,
    IReadOnlyList<string> CurrentNames,
    string ProposedName,
    string? ErrorMessage = null);

public sealed record ActorNfoSyncPreviewResult(
    int ActorId,
    string CanonicalName,
    IReadOnlyList<ActorNfoMoviePreview> Movies)
{
    public int TotalMovies => Movies.Count;
    public int WillUpdateCount => Movies.Count(m => m.Status == ActorNfoMovieStatus.WillUpdate);
    public int UpToDateCount => Movies.Count(m => m.Status == ActorNfoMovieStatus.UpToDate);
    public int NoNfoCount => Movies.Count(m => m.Status == ActorNfoMovieStatus.NoNfoFound);
    public int ProblemCount => Movies.Count(m => m.Status is ActorNfoMovieStatus.ReadOnly or ActorNfoMovieStatus.InvalidXml or ActorNfoMovieStatus.Error);
}

public sealed record ActorNfoMovieResult(
    int MovieId,
    string MovieCode,
    string? NfoPath,
    ActorNfoMovieStatus Status,
    string? Message = null);

public sealed record ActorNfoSyncResult(
    int ActorId,
    string CanonicalName,
    int TotalMovies,
    int UpdatedCount,
    int UpToDateCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<ActorNfoMovieResult> Details);

public sealed record ActorNfoBatchSyncResult(
    int TotalMoviesChecked,
    int TotalNfosUpdated,
    int TotalActorsUpdated,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record ActorNfoConflictCheckResult(
    int MovieId,
    string MovieCode,
    bool HasConflict,
    string? ConflictDetails);

public sealed record ActorNfoConflictBatchResult(
    int TotalMoviesChecked,
    int ConflictsFoundCount,
    IReadOnlyList<ActorNfoConflictCheckResult> Conflicts);

public sealed record MovieNfoConflictResolutionResult(bool Success, bool FileChanged);

/// <summary>Outcome of a bulk .nfo push. StillDrifting counts movies that are still
/// drifting afterwards — written but with drift left over, or with nothing the proposal could
/// write (the sync creates the elements an .nfo lacks, so this is rare).</summary>
public sealed record NfoDriftPushResult(int Written, int SkippedExternal, int Failed, int StillDrifting);

public interface INfoSyncService
{
    Task<ActorNfoSyncPreviewResult> PreviewSyncActorAsync(
        int actorId,
        IReadOnlyList<string>? extraNamesToMatch = null,
        CancellationToken ct = default);

    Task<ActorNfoSyncResult> SyncActorAsync(
        int actorId,
        IReadOnlyList<string>? extraNamesToMatch = null,
        CancellationToken ct = default);

    Task<ActorNfoBatchSyncResult> SyncAllActorsAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<ActorNfoConflictCheckResult> CheckMovieNfoConflictAsync(
        int movieId,
        CancellationToken ct = default);

    /// <summary>The metadata editor modal's "Save" check: the same drift check as
    /// CheckMovieNfoConflictAsync. The modal never writes to the .nfo itself, so a field edited
    /// there whose .nfo element is blank or absent surfaces as a conflict the user can resolve.</summary>
    Task<ActorNfoConflictCheckResult> CheckMovieMetadataConflictAsync(
        int movieId,
        CancellationToken ct = default);

    Task<ActorNfoConflictBatchResult> DetectAllMovieConflictsAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<ActorNfoSyncResult> SyncMovieNfoAsync(
        int movieId,
        CancellationToken ct = default);

    Task<string?> GenerateProposedNfoAsync(
        int movieId,
        CancellationToken ct = default);

    Task<MovieNfoConflictResolutionResult> ResolveMovieNfoConflictAsync(
        int movieId,
        string expectedOriginalNfoContent,
        string updatedNfoContent,
        CancellationToken ct = default);

    /// <summary>The conflict review's "Import from .nfo": the opposite resolution to
    /// ResolveMovieNfoConflictAsync — adopts the on-disk .nfo as the movie's metadata (descriptive
    /// fields, cast, tags) and leaves the file untouched, then re-checks drift. Only proceeds while the
    /// file still holds <paramref name="expectedOnDiskNfoContent"/> (what the user reviewed);
    /// otherwise returns FileChanged. Success is false for a missing or malformed .nfo.</summary>
    Task<MovieNfoConflictResolutionResult> ImportMovieNfoAsync(
        int movieId,
        string expectedOnDiskNfoContent,
        CancellationToken ct = default);

    /// <summary>The "Refresh with Javinizer" field picker's apply step: writes the movie's
    /// current descriptive Meta* fields into its local .nfo file, if one exists, via the same
    /// conservative element-reconciliation ApplyFieldUpdates already uses for the diff preview
    /// (GenerateProposedNfoAsync) — updates the element a field already has on disk, and creates it
    /// when the .nfo has none (or an empty one). A deliberate departure from MovieService.UpdateMetadataAsync's
    /// DB-only precedent, since the spec explicitly asks for a direct .nfo write on
    /// confirm. Re-checks remaining .nfo drift afterward, since an unselected field can still leave
    /// real drift. No-ops (returns false)
    /// when the movie has no local .nfo file or it's read-only. Returns whether anything was
    /// actually written.</summary>
    Task<bool> SyncMovieMetadataToNfoAsync(int movieId, CancellationToken ct = default);

    /// <summary>The Movies page's "Write Javbuddy metadata to .nfo" bulk action: for each
    /// movie whose stored drift is JavbuddyChanged — or ExternalEdit/BothChanged too, when
    /// <paramref name="includeExternal"/> — applies the same changes as GenerateProposedNfoAsync,
    /// but to the file as loaded (its own formatting kept, no re-indent), writes it through
    /// NfoFileWriter (keeping the replaced content as an NfoGeneration) and re-checks drift against what was written. Movies with
    /// no drift, an Unreadable .nfo, or an ineligible direction are left untouched.</summary>
    Task<NfoDriftPushResult> PushNfoDriftAsync(
        IReadOnlyList<int> movieIds,
        bool includeExternal,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);
}

public class NfoSyncService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    ILogger<NfoSyncService> logger) : INfoSyncService
{
    public async Task<ActorNfoSyncPreviewResult> PreviewSyncActorAsync(
        int actorId,
        IReadOnlyList<string>? extraNamesToMatch = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors
            .AsNoTracking()
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == actorId, ct);

        if (actor is null || !actor.HasName)
        {
            return new ActorNfoSyncPreviewResult(actorId, string.Empty, []);
        }

        var canonicalName = actor.DisplayName;
        var nameMatcher = CreateNameMatcher(actor, extraNamesToMatch);

        var movieIds = await db.MovieActors
            .AsNoTracking()
            .Where(ma => ma.ActorId == actorId)
            .Select(ma => ma.MovieId)
            .Distinct()
            .ToListAsync(ct);

        var movies = await db.Movies
            .AsNoTracking()
            .Where(m => movieIds.Contains(m.Id))
            .OrderBy(m => m.Code)
            .ToListAsync(ct);

        var previews = new List<ActorNfoMoviePreview>();

        foreach (var movie in movies)
        {
            if (string.IsNullOrWhiteSpace(movie.Code)) continue;

            var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
            if (nfoPath is null || !File.Exists(nfoPath))
            {
                previews.Add(new ActorNfoMoviePreview(
                    movie.Id,
                    movie.Code,
                    movie.Title ?? movie.MetaTitle,
                    null,
                    ActorNfoMovieStatus.NoNfoFound,
                    [],
                    canonicalName));
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(nfoPath);
                if (fileInfo.IsReadOnly)
                {
                    previews.Add(new ActorNfoMoviePreview(
                        movie.Id,
                        movie.Code,
                        movie.Title ?? movie.MetaTitle,
                        nfoPath,
                        ActorNfoMovieStatus.ReadOnly,
                        [],
                        canonicalName,
                        "File is marked read-only."));
                    continue;
                }

                XDocument doc;
                await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
                }

                var root = doc.Root;
                if (root is null)
                {
                    previews.Add(new ActorNfoMoviePreview(
                        movie.Id,
                        movie.Code,
                        movie.Title ?? movie.MetaTitle,
                        nfoPath,
                        ActorNfoMovieStatus.InvalidXml,
                        [],
                        canonicalName,
                        "Missing root element."));
                    continue;
                }

                var matchingActors = FindMatchingActorElements(root, nameMatcher);
                if (matchingActors.Count == 0)
                {
                    previews.Add(new ActorNfoMoviePreview(
                        movie.Id,
                        movie.Code,
                        movie.Title ?? movie.MetaTitle,
                        nfoPath,
                        ActorNfoMovieStatus.NoMatchingActorInNfo,
                        [],
                        canonicalName));
                    continue;
                }

                var currentNames = matchingActors
                    .Select(a => a.Element("name")?.Value?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToList();

                var allMatchCanonical = matchingActors.Count == 1
                    && string.Equals(currentNames.FirstOrDefault(), canonicalName, StringComparison.Ordinal);

                previews.Add(new ActorNfoMoviePreview(
                    movie.Id,
                    movie.Code,
                    movie.Title ?? movie.MetaTitle,
                    nfoPath,
                    allMatchCanonical ? ActorNfoMovieStatus.UpToDate : ActorNfoMovieStatus.WillUpdate,
                    currentNames,
                    canonicalName));
            }
            catch (XmlException ex)
            {
                previews.Add(new ActorNfoMoviePreview(
                    movie.Id,
                    movie.Code,
                    movie.Title ?? movie.MetaTitle,
                    nfoPath,
                    ActorNfoMovieStatus.InvalidXml,
                    [],
                    canonicalName,
                    ex.Message));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                previews.Add(new ActorNfoMoviePreview(
                    movie.Id,
                    movie.Code,
                    movie.Title ?? movie.MetaTitle,
                    nfoPath,
                    ActorNfoMovieStatus.Error,
                    [],
                    canonicalName,
                    ex.Message));
            }
        }

        return new ActorNfoSyncPreviewResult(actorId, canonicalName, previews);
    }

    public async Task<ActorNfoSyncResult> SyncActorAsync(
        int actorId,
        IReadOnlyList<string>? extraNamesToMatch = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors
            .AsNoTracking()
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == actorId, ct);

        if (actor is null || !actor.HasName)
        {
            return new ActorNfoSyncResult(actorId, string.Empty, 0, 0, 0, 0, 0, []);
        }

        var canonicalName = actor.DisplayName;
        var nameMatcher = CreateNameMatcher(actor, extraNamesToMatch);

        var movieIds = await db.MovieActors
            .AsNoTracking()
            .Where(ma => ma.ActorId == actorId)
            .Select(ma => ma.MovieId)
            .Distinct()
            .ToListAsync(ct);

        // Actors and tags loaded for the post-write drift re-check (RecordWrite).
        var movies = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .Where(m => movieIds.Contains(m.Id))
            .OrderBy(m => m.Code)
            .ToListAsync(ct);

        var results = new List<ActorNfoMovieResult>();
        int updatedCount = 0;
        int upToDateCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        foreach (var movie in movies)
        {
            if (string.IsNullOrWhiteSpace(movie.Code))
            {
                skippedCount++;
                continue;
            }

            var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
            if (nfoPath is null || !File.Exists(nfoPath))
            {
                skippedCount++;
                results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, null, ActorNfoMovieStatus.NoNfoFound));
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(nfoPath);
                if (fileInfo.IsReadOnly)
                {
                    failedCount++;
                    results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.ReadOnly, "File is marked read-only."));
                    continue;
                }

                XDocument doc;
                await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
                }

                var root = doc.Root;
                if (root is null)
                {
                    failedCount++;
                    results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.InvalidXml, "Missing root element."));
                    continue;
                }

                var matchingActors = FindMatchingActorElements(root, nameMatcher);
                if (matchingActors.Count == 0)
                {
                    skippedCount++;
                    results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.NoMatchingActorInNfo));
                    continue;
                }

                var currentNames = matchingActors
                    .Select(a => a.Element("name")?.Value?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToList();

                var allMatchCanonical = matchingActors.Count == 1
                    && string.Equals(currentNames.FirstOrDefault(), canonicalName, StringComparison.Ordinal);

                if (allMatchCanonical)
                {
                    upToDateCount++;
                    results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.UpToDate));
                    continue;
                }

                // Apply changes to XML
                var preWrite = NfoDriftDetector.ReadNfo(root);
                ConsolidateAndUpdateActorElements(matchingActors, canonicalName);

                // Save back to disk preserving formatting and XML declaration
                var written = await NfoFileWriter.WriteXmlAsync(nfoPath, doc, ct);
                if (written.Status != NfoWriteStatus.Saved)
                {
                    failedCount++;
                    results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ToActorNfoStatus(written.Status), "File could not be written."));
                    continue;
                }

                // Update timestamps and MetaActresses in database
                movie.MediaNfoLastWriteUtc = written.LastWriteUtc;
                UpdateMovieMetaActresses(movie, nameMatcher, canonicalName);
                RecordWrite(movie, preWrite, root, written);
                await NfoHistoryService.AddGenerationAsync(db, movie.Id, written.PreviousContent, newContent: null, NfoWriteTrigger.ActorSync, ct);
                await db.SaveChangesAsync(ct);

                updatedCount++;
                results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.UpToDate, "Updated successfully."));
                logger.LogInformation("Synchronized actor name to '{CanonicalName}' in {NfoPath}", canonicalName, nfoPath);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failedCount++;
                results.Add(new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.Error, ex.Message));
                logger.LogWarning(ex, "Failed to synchronize actor name in {Code} ({NfoPath})", movie.Code, nfoPath);
            }
        }

        return new ActorNfoSyncResult(
            actorId,
            canonicalName,
            movies.Count,
            updatedCount,
            upToDateCount,
            skippedCount,
            failedCount,
            results);
    }

    public async Task<ActorNfoBatchSyncResult> SyncAllActorsAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var actors = await db.Actors
            .AsNoTracking()
            .Include(a => a.Aliases)
            .Where(a => a.FirstName != null)
            .ToListAsync(ct);

        var actorMatchers = actors
            .Select(a => (Actor: a, Matcher: CreateNameMatcher(a, null)))
            .ToList();

        // Actors and tags loaded for the post-write drift re-check (RecordWrite).
        var movies = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        int totalMoviesChecked = 0;
        int totalNfosUpdated = 0;
        int totalActorsUpdated = 0;
        int failedCount = 0;
        var errors = new List<string>();

        int totalCount = movies.Count;

        for (int i = 0; i < movies.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var movie = movies[i];
            progress?.Report(new TaskProgress(i + 1, totalCount, "Aligning .nfo actor names"));

            if (string.IsNullOrWhiteSpace(movie.Code)) continue;

            var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
            if (nfoPath is null || !File.Exists(nfoPath)) continue;

            totalMoviesChecked++;

            try
            {
                var fileInfo = new FileInfo(nfoPath);
                if (fileInfo.IsReadOnly)
                {
                    failedCount++;
                    errors.Add($"{movie.Code}: file is read-only");
                    continue;
                }

                XDocument doc;
                await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
                }

                var root = doc.Root;
                if (root is null) continue;

                var preWrite = NfoDriftDetector.ReadNfo(root);
                bool nfoModified = false;

                foreach (var (actor, matcher) in actorMatchers)
                {
                    var canonicalName = actor.DisplayName;
                    var matchingActors = FindMatchingActorElements(root, matcher);
                    if (matchingActors.Count == 0) continue;

                    var currentNames = matchingActors
                        .Select(a => a.Element("name")?.Value?.Trim())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .Distinct()
                        .ToList();

                    var alreadyUpToDate = matchingActors.Count == 1
                        && string.Equals(currentNames.FirstOrDefault(), canonicalName, StringComparison.Ordinal);

                    if (alreadyUpToDate) continue;

                    ConsolidateAndUpdateActorElements(matchingActors, canonicalName);
                    UpdateMovieMetaActresses(movie, matcher, canonicalName);
                    totalActorsUpdated++;
                    nfoModified = true;
                }

                if (nfoModified)
                {
                    var written = await NfoFileWriter.WriteXmlAsync(nfoPath, doc, ct);
                    if (written.Status != NfoWriteStatus.Saved)
                    {
                        failedCount++;
                        errors.Add($"{movie.Code}: .nfo is {written.Status}");
                        continue;
                    }
                    movie.MediaNfoLastWriteUtc = written.LastWriteUtc;
                    RecordWrite(movie, preWrite, root, written);
                    await NfoHistoryService.AddGenerationAsync(db, movie.Id, written.PreviousContent, newContent: null, NfoWriteTrigger.ActorSync, ct);
                    await db.SaveChangesAsync(ct);
                    totalNfosUpdated++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failedCount++;
                errors.Add($"{movie.Code}: {ex.Message}");
                logger.LogWarning(ex, "Failed to align actor names in {Code} .nfo", movie.Code);
            }
        }

        return new ActorNfoBatchSyncResult(
            totalMoviesChecked,
            totalNfosUpdated,
            totalActorsUpdated,
            failedCount,
            errors);
    }

    public async Task<ActorNfoConflictCheckResult> CheckMovieNfoConflictAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code) || movie.Status != MovieStatus.Got)
        {
            return new ActorNfoConflictCheckResult(movieId, movie?.Code ?? "", false, null);
        }

        var (result, changed) = await CheckMovieNfoConflictInMemoryAsync(movie, ct);
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }

        return result;
    }

    /// <summary>The metadata editor modal's "Save" check; the same drift check as
    /// <see cref="CheckMovieNfoConflictAsync"/>, under the name its callers use.</summary>
    public Task<ActorNfoConflictCheckResult> CheckMovieMetadataConflictAsync(int movieId, CancellationToken ct = default) =>
        CheckMovieNfoConflictAsync(movieId, ct);

    public async Task<ActorNfoConflictBatchResult> DetectAllMovieConflictsAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Batch-load all Got movies with their linked actors/aliases and tags in one query,
        // avoiding the N+1 pattern of creating a DbContext per movie.
        var movies = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .Where(m => m.Status == MovieStatus.Got && m.Code != null)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var conflicts = new List<ActorNfoConflictCheckResult>();
        int total = movies.Count;
        bool anyDirty = false;

        for (int i = 0; i < movies.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new TaskProgress(i + 1, total, "Detecting .nfo metadata conflicts"));

            var movie = movies[i];
            var result = await CheckMovieNfoConflictInMemoryAsync(movie, ct);
            if (result.changed) anyDirty = true;
            if (result.result.HasConflict)
            {
                conflicts.Add(result.result);
            }
        }

        if (anyDirty)
        {
            await db.SaveChangesAsync(ct);
        }

        return new ActorNfoConflictBatchResult(total, conflicts.Count, conflicts);
    }

    /// <summary>Checks a single already-loaded movie for .nfo drift without creating a new
    /// DbContext. Mutates <paramref name="movie"/>'s drift fields in-place; the caller is
    /// responsible for calling SaveChangesAsync on the owning context. The .nfo is only re-read
    /// when its last-write time or size differs from the fingerprint of the values cached in
    /// NfoBaselineJson — an unchanged file costs one stat, so a library-wide rescan only parses
    /// the files that actually changed.</summary>
    private async Task<(ActorNfoConflictCheckResult result, bool changed)> CheckMovieNfoConflictInMemoryAsync(
        Movie movie, CancellationToken ct)
    {
        var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code!, ct);
        var info = nfoPath is null ? null : new FileInfo(nfoPath);
        if (info is null || !info.Exists)
        {
            // The baseline is kept: if the .nfo comes back, drift is still judged against it.
            bool cleared = movie.NfoDriftKind != NfoDriftKind.None || movie.NfoConflictDetails is not null;
            movie.NfoDriftKind = NfoDriftKind.None;
            movie.NfoConflictDetails = null;
            return (new ActorNfoConflictCheckResult(movie.Id, movie.Code!, false, null), cleared);
        }

        var baseline = NfoBaselineState.FromJson(movie.NfoBaselineJson);
        NfoFieldValues disk;
        if (baseline?.LastDisk is { } cached
            && movie.NfoBaselineLastWriteUtc == info.LastWriteTimeUtc
            && movie.NfoBaselineSize == info.Length)
        {
            disk = cached;
        }
        else
        {
            try
            {
                XDocument doc;
                await using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
                }

                if (doc.Root is null)
                {
                    return SetUnreadable(movie, "Invalid .nfo XML: missing root element.");
                }
                disk = NfoDriftDetector.ReadNfo(doc.Root);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return SetUnreadable(movie, $"Error reading .nfo: {ex.Message}");
            }
        }

        var outcome = NfoDriftDetector.Evaluate(movie, disk, baseline);
        return ApplyOutcome(movie, outcome, info.LastWriteTimeUtc, info.Length);

        static (ActorNfoConflictCheckResult, bool) SetUnreadable(Movie m, string detail)
        {
            // Baseline and fingerprint untouched, so the next check re-reads the file.
            bool changed = m.NfoDriftKind != NfoDriftKind.Unreadable || m.NfoConflictDetails != detail;
            m.NfoDriftKind = NfoDriftKind.Unreadable;
            m.NfoConflictDetails = detail;
            return (new ActorNfoConflictCheckResult(m.Id, m.Code!, true, detail), changed);
        }
    }

    /// <summary>Stores a drift outcome and the fingerprint of the .nfo its disk values came from.</summary>
    private static (ActorNfoConflictCheckResult result, bool changed) ApplyOutcome(
        Movie movie, NfoDriftOutcome outcome, DateTime nfoLastWriteUtc, long nfoSize)
    {
        var baselineJson = outcome.Baseline.ToJson();
        bool changed = movie.NfoDriftKind != outcome.Kind
            || movie.NfoConflictDetails != outcome.Details
            || movie.NfoBaselineJson != baselineJson
            || movie.NfoBaselineLastWriteUtc != nfoLastWriteUtc
            || movie.NfoBaselineSize != nfoSize;

        movie.NfoDriftKind = outcome.Kind;
        movie.NfoConflictDetails = outcome.Details;
        movie.NfoBaselineJson = baselineJson;
        movie.NfoBaselineLastWriteUtc = nfoLastWriteUtc;
        movie.NfoBaselineSize = nfoSize;

        return (new ActorNfoConflictCheckResult(movie.Id, movie.Code!, outcome.Kind != NfoDriftKind.None, outcome.Details), changed);
    }

    /// <summary>Re-checks drift right after a Javbuddy .nfo write, against the document it just
    /// wrote (no re-read) — so whatever drift the write left behind is reported as-is, and fields
    /// the write changed on disk aren't later mistaken for an outside edit (see
    /// NfoDriftDetector.Evaluate's preWriteDisk). Requires the movie's actors and tags loaded.</summary>
    private static void RecordWrite(Movie movie, NfoFieldValues preWrite, XElement writtenRoot, NfoWriteResult written)
    {
        var outcome = NfoDriftDetector.Evaluate(
            movie,
            NfoDriftDetector.ReadNfo(writtenRoot),
            NfoBaselineState.FromJson(movie.NfoBaselineJson),
            preWriteDisk: preWrite);
        ApplyOutcome(movie, outcome, written.LastWriteUtc!.Value, written.Size!.Value);
    }

    public async Task<ActorNfoSyncResult> SyncMovieNfoAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new ActorNfoSyncResult(0, string.Empty, 0, 0, 0, 0, 0, []);
        }

        var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
        if (nfoPath is null || !File.Exists(nfoPath))
        {
            return new ActorNfoSyncResult(0, string.Empty, 1, 0, 0, 1, 0, [new ActorNfoMovieResult(movie.Id, movie.Code, null, ActorNfoMovieStatus.NoNfoFound)]);
        }

        var fileInfo = new FileInfo(nfoPath);
        if (fileInfo.IsReadOnly)
        {
            return new ActorNfoSyncResult(0, string.Empty, 1, 0, 0, 0, 1, [new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.ReadOnly, "File is marked read-only.")]);
        }

        XDocument doc;
        await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
        {
            doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
        }

        var root = doc.Root;
        if (root is null)
        {
            return new ActorNfoSyncResult(0, string.Empty, 1, 0, 0, 0, 1, [new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.InvalidXml, "Missing root element.")]);
        }

        var linkedActors = movie.MovieActors
            .Select(ma => ma.Actor)
            .Where(a => a != null && a.HasName)
            .ToList();

        var preWrite = NfoDriftDetector.ReadNfo(root);
        bool modified = false;
        foreach (var actor in linkedActors)
        {
            var canonicalName = actor.DisplayName;
            var matcher = CreateNameMatcher(actor, null);
            var matchingActors = FindMatchingActorElements(root, matcher, linkedActors.Count);
            if (matchingActors.Count == 0)
            {
                var newActorEl = new XElement("actor", new XElement("name", canonicalName));
                root.Add(newActorEl);
                UpdateMovieMetaActresses(movie, matcher, canonicalName);
                modified = true;
                continue;
            }

            var currentNames = matchingActors
                .Select(a => a.Element("name")?.Value?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            var alreadyUpToDate = matchingActors.Count == 1
                && string.Equals(currentNames.FirstOrDefault(), canonicalName, StringComparison.Ordinal);

            if (alreadyUpToDate) continue;

            ConsolidateAndUpdateActorElements(matchingActors, canonicalName);
            UpdateMovieMetaActresses(movie, matcher, canonicalName);
            modified = true;
        }

        if (modified)
        {
            var written = await NfoFileWriter.WriteXmlAsync(nfoPath, doc, ct);
            if (written.Status != NfoWriteStatus.Saved)
            {
                return new ActorNfoSyncResult(0, string.Empty, 1, 0, 0, 0, 1, [new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ToActorNfoStatus(written.Status), "File could not be written.")]);
            }
            movie.MediaNfoLastWriteUtc = written.LastWriteUtc;
            RecordWrite(movie, preWrite, root, written);
            await NfoHistoryService.AddGenerationAsync(db, movie.Id, written.PreviousContent, newContent: null, NfoWriteTrigger.ActorSync, ct);
        }
        else
        {
            // Nothing to write, but the actors being in sync doesn't mean every field is — report
            // whatever drift the file (already parsed above) really has.
            var outcome = NfoDriftDetector.Evaluate(movie, preWrite, NfoBaselineState.FromJson(movie.NfoBaselineJson));
            ApplyOutcome(movie, outcome, fileInfo.LastWriteTimeUtc, fileInfo.Length);
        }

        await db.SaveChangesAsync(ct);

        return new ActorNfoSyncResult(
            0,
            string.Empty,
            1,
            modified ? 1 : 0,
            modified ? 0 : 1,
            0,
            0,
            [new ActorNfoMovieResult(movie.Id, movie.Code, nfoPath, ActorNfoMovieStatus.UpToDate, "Updated successfully.")]);
    }

    public async Task<bool> SyncMovieMetadataToNfoAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return false;
        }

        var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
        if (nfoPath is null || !File.Exists(nfoPath))
        {
            return false;
        }

        if (new FileInfo(nfoPath).IsReadOnly)
        {
            return false;
        }

        XDocument doc;
        await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
        {
            doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
        }

        var root = doc.Root;
        if (root is null)
        {
            return false;
        }

        // Re-check remaining drift either way: rating and actor differences are not written by ApplyFieldUpdates and
        // are real drift the user should still see.
        var preWrite = NfoDriftDetector.ReadNfo(root);
        var changed = ApplyFieldUpdates(movie, root);
        if (changed)
        {
            var written = await NfoFileWriter.WriteXmlAsync(nfoPath, doc, ct);
            changed = written.Status == NfoWriteStatus.Saved;
            if (changed)
            {
                movie.MediaNfoLastWriteUtc = written.LastWriteUtc;
                RecordWrite(movie, preWrite, root, written);
                await NfoHistoryService.AddGenerationAsync(db, movie.Id, written.PreviousContent, newContent: null, NfoWriteTrigger.MetadataSync, ct);
            }
        }

        if (!changed)
        {
            await CheckMovieNfoConflictInMemoryAsync(movie, ct);
        }
        await db.SaveChangesAsync(ct);

        return changed;
    }

    private static NfoFieldValues? TryReadNfo(string content)
    {
        try
        {
            return XDocument.Parse(content).Root is { } root ? NfoDriftDetector.ReadNfo(root) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Maps a refused NfoFileWriter write (the file vanished or turned read-only between
    /// this service's own checks and the write) onto the per-movie status the actor sync UI shows.</summary>
    private static ActorNfoMovieStatus ToActorNfoStatus(NfoWriteStatus status) => status switch
    {
        NfoWriteStatus.NotFound => ActorNfoMovieStatus.NoNfoFound,
        NfoWriteStatus.ReadOnly => ActorNfoMovieStatus.ReadOnly,
        _ => ActorNfoMovieStatus.Error,
    };

    public async Task<string?> GenerateProposedNfoAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code)) return null;

        var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
        if (nfoPath is null || !File.Exists(nfoPath)) return null;

        XDocument doc;
        try
        {
            await using var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        }
        catch (XmlException)
        {
            // The editor can offer the raw content for manual repair; malformed XML must not
            // terminate the Blazor circuit when the conflict review is opened.
            return null;
        }

        var root = doc.Root;
        if (root is null) return null;

        ApplyProposedChanges(movie, root);

        return SerializeXmlFormatted(doc);
    }

    /// <summary>The changes the conflict review proposes (and the bulk push writes): every
    /// descriptive field via ApplyFieldUpdates, then each linked actor renamed to its canonical
    /// name — consolidating duplicate entries — or appended when the .nfo has no entry for it
    /// (FindMatchingActorElements' single-actor fallback treats a lone entry as the target).
    /// Returns whether anything changed.</summary>
    private static bool ApplyProposedChanges(Movie movie, XElement root)
    {
        var changed = ApplyFieldUpdates(movie, root);

        var linkedActors = movie.MovieActors
            .Select(ma => ma.Actor)
            .Where(a => a != null && a.HasName)
            .ToList();

        foreach (var actor in linkedActors)
        {
            var matcher = CreateNameMatcher(actor, null);
            var matchingActors = FindMatchingActorElements(root, matcher, linkedActors.Count);

            if (matchingActors.Count == 0)
            {
                root.Add(new XElement("actor", new XElement("name", actor.DisplayName)));
                changed = true;
                continue;
            }

            var alreadyUpToDate = matchingActors.Count == 1
                && string.Equals(matchingActors[0].Element("name")?.Value?.Trim(), actor.DisplayName, StringComparison.Ordinal);
            if (alreadyUpToDate) continue;

            ConsolidateAndUpdateActorElements(matchingActors, actor.DisplayName);
            changed = true;
        }

        return changed;
    }

    public async Task<NfoDriftPushResult> PushNfoDriftAsync(
        IReadOnlyList<int> movieIds,
        bool includeExternal,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        int written = 0, skippedExternal = 0, failed = 0, stillDrifting = 0;

        for (int i = 0; i < movieIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new TaskProgress(i + 1, movieIds.Count, "Writing .nfo files"));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var movie = await db.Movies
                .AsSplitQuery()
                .Include(m => m.MovieActors)
                .ThenInclude(ma => ma.Actor)
                .ThenInclude(a => a.Aliases)
                .Include(m => m.MovieTags)
                .ThenInclude(mt => mt.Tag)
                .ThenInclude(t => t.ParentTag)
                .FirstOrDefaultAsync(m => m.Id == movieIds[i], ct);

            if (movie is null || string.IsNullOrWhiteSpace(movie.Code) || movie.Status != MovieStatus.Got) continue;
            if (movie.NfoDriftKind is (NfoDriftKind.ExternalEdit or NfoDriftKind.BothChanged) && !includeExternal)
            {
                skippedExternal++;
                continue;
            }
            if (movie.NfoDriftKind is not (NfoDriftKind.JavbuddyChanged or NfoDriftKind.ExternalEdit or NfoDriftKind.BothChanged)) continue;

            try
            {
                var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
                if (nfoPath is null || !File.Exists(nfoPath))
                {
                    failed++;
                    continue;
                }

                XDocument doc;
                await using (var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
                }
                if (doc.Root is not { } root)
                {
                    failed++;
                    continue;
                }

                var preWrite = NfoDriftDetector.ReadNfo(root);
                if (!ApplyProposedChanges(movie, root))
                {
                    stillDrifting++;
                    continue;
                }

                var result = await NfoFileWriter.WriteXmlAsync(nfoPath, doc, ct);
                if (result.Status != NfoWriteStatus.Saved)
                {
                    failed++;
                    continue;
                }

                movie.MediaNfoLastWriteUtc = result.LastWriteUtc;
                foreach (var actor in movie.MovieActors.Select(ma => ma.Actor).Where(a => a != null && a.HasName))
                {
                    UpdateMovieMetaActresses(movie, CreateNameMatcher(actor, null), actor.DisplayName);
                }
                RecordWrite(movie, preWrite, root, result);
                await NfoHistoryService.AddGenerationAsync(db, movie.Id, result.PreviousContent, newContent: null, NfoWriteTrigger.DriftPush, ct);
                await db.SaveChangesAsync(ct);

                written++;
                if (movie.NfoDriftKind != NfoDriftKind.None) stillDrifting++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex, "Failed to write Javbuddy metadata to the .nfo of {Code}", movie.Code);
            }
        }

        return new NfoDriftPushResult(written, skippedExternal, failed, stillDrifting);
    }

    public async Task<MovieNfoConflictResolutionResult> ResolveMovieNfoConflictAsync(
        int movieId,
        string expectedOriginalNfoContent,
        string updatedNfoContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(updatedNfoContent)) return new(false, false);

        XDocument updatedDoc;
        try
        {
            updatedDoc = XDocument.Parse(updatedNfoContent);
        }
        catch (XmlException)
        {
            return new(false, false);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);

        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            logger.LogWarning("Cannot resolve .nfo conflict for movie {MovieId}: movie not found or has no code", movieId);
            return new(false, false);
        }

        var saveResult = await localLibraryClient.SaveRawNfoIfUnchangedAsync(movie.Code, expectedOriginalNfoContent, updatedNfoContent, ct);
        if (saveResult is not ConditionalNfoSaveResult.Saved)
        {
            return new(false, saveResult is ConditionalNfoSaveResult.FileChanged);
        }

        // The write only happens when the file still matched expectedOriginalNfoContent.
        await NfoHistoryService.AddGenerationAsync(db, movie.Id, expectedOriginalNfoContent, updatedNfoContent, NfoWriteTrigger.ConflictResolution, ct);

        var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
        var savedFile = nfoPath is null ? null : new FileInfo(nfoPath);
        if (savedFile is { Exists: true })
        {
            movie.MediaNfoLastWriteUtc = savedFile.LastWriteTimeUtc;
        }

        var linkedActors = movie.MovieActors
            .Select(ma => ma.Actor)
            .Where(a => a != null && a.HasName)
            .ToList();

        foreach (var actor in linkedActors)
        {
            var matcher = CreateNameMatcher(actor, null);
            UpdateMovieMetaActresses(movie, matcher, actor.DisplayName);
        }

        // Report whatever drift the accepted content still has (usually none) rather than
        // assuming the review resolved everything.
        if (savedFile is { Exists: true })
        {
            var writtenValues = NfoDriftDetector.ReadNfo(updatedDoc.Root!);
            RecordWrite(
                movie,
                TryReadNfo(expectedOriginalNfoContent) ?? writtenValues,
                updatedDoc.Root!,
                new NfoWriteResult(NfoWriteStatus.Saved, savedFile.LastWriteTimeUtc, savedFile.Length));
        }
        await db.SaveChangesAsync(ct);
        return new(true, false);
    }

    public async Task<MovieNfoConflictResolutionResult> ImportMovieNfoAsync(
        int movieId,
        string expectedOnDiskNfoContent,
        CancellationToken ct = default)
    {
        DateTime importedWriteUtc;
        long importedSize;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == movieId, ct);
            if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
            {
                logger.LogWarning("Cannot import .nfo for movie {MovieId}: movie not found or has no code", movieId);
                return new(false, false);
            }

            var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(movie.Code, ct);
            if (nfoPath is null) return new(false, false);

            // The file is stat'ed before and after it is read: if it moved in between, what was parsed
            // isn't what was reviewed (or what gets recorded as the file's write time).
            FileInfo before;
            XDocument doc;
            string? current;
            try
            {
                before = new FileInfo(nfoPath);
                if (!before.Exists) return new(false, false);

                current = await localLibraryClient.GetRawNfoAsync(movie.Code, ct);
                if (current is null) return new(false, false);
                if (!string.Equals(current, expectedOnDiskNfoContent, StringComparison.Ordinal)) return new(false, true);

                // Loaded from the file (not the string) so a declared non-UTF-8 encoding is honoured,
                // as in LocalNfoParser.ParseAsync and the drift check.
                await using var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
                doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                return new(false, false);
            }
            if (doc.Root is null) return new(false, false);

            var after = new FileInfo(nfoPath);
            if (!after.Exists || after.LastWriteTimeUtc != before.LastWriteTimeUtc || after.Length != before.Length)
            {
                return new(false, true);
            }

            var metadata = LocalNfoParser.Parse(doc.Root);

            // Fields an .nfo Javbuddy writes never holds (rating, remote image URLs) are kept unless the file
            // itself provides them. The metadata source becomes "Local" like any other adoption of .nfo-backed
            // metadata, so a rescan doesn't re-scrape over what was just imported.
            var (score, votes, cover, backdrop) =
                (movie.MetaRatingScore, movie.MetaRatingVotes, movie.MetaCoverUrl, movie.MetaBackdropUrl);
            LocalLibraryMetadataMapper.ApplyDescriptiveMetadata(movie, metadata);
            movie.MetaRatingScore ??= score;
            movie.MetaRatingVotes ??= votes;
            movie.MetaCoverUrl ??= cover;
            movie.MetaBackdropUrl ??= backdrop;
            LocalLibraryMetadataMapper.FillMissingRuntimeFromMedia(movie, metadata);

            await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
            // Saves the changes made above along with the tag links.
            await TagNormalization.ApplyToMovieAsync(db, movie, metadata.Genres, ct);
            movie.MediaNfoLastWriteUtc = after.LastWriteTimeUtc;
            await db.SaveChangesAsync(ct);
            (importedWriteUtc, importedSize) = (after.LastWriteTimeUtc, after.Length);
        }

        await RecordDriftAfterImportAsync(movieId, importedWriteUtc, importedSize, ct);
        return new(true, false);
    }

    /// <summary>Re-checks drift after an import. Importing runs the .nfo through Javbuddy's normalization (actor
    /// aliases to the main name, tag rules and the ignore list), so the cast or genres can still differ from the
    /// file even though it was just adopted — and with the old baseline that would read as an outside edit again.
    /// Those two fields get a baseline saying the file's value was once Javbuddy's too, so they read as Javbuddy's
    /// own normalized form (JavbuddyChanged, writable to the file) rather than as a conflict to resolve. Skipped
    /// when the file no longer is the one that was imported: a concurrent outside edit stays an outside edit.</summary>
    private async Task RecordDriftAfterImportAsync(int movieId, DateTime importedWriteUtc, long importedSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .AsSplitQuery()
            .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
            .ThenInclude(a => a.Aliases)
            .Include(m => m.MovieTags)
            .ThenInclude(mt => mt.Tag)
            .ThenInclude(t => t.ParentTag)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code)) return;

        var (result, _) = await CheckMovieNfoConflictInMemoryAsync(movie, ct);
        if (result.HasConflict
            && movie.NfoDriftKind != NfoDriftKind.Unreadable
            && movie.NfoBaselineLastWriteUtc == importedWriteUtc
            && movie.NfoBaselineSize == importedSize
            && NfoBaselineState.FromJson(movie.NfoBaselineJson) is { LastDisk: { } disk } baseline)
        {
            var dbValues = NfoDriftDetector.ReadMovie(movie);
            var fields = baseline.Fields.ToDictionary();
            foreach (var field in new[] { NfoField.Actors, NfoField.Genres })
            {
                var diskKey = NfoDriftDetector.ValueKey(disk, field);
                if (NfoDriftDetector.ValueKey(dbValues, field) != diskKey)
                {
                    fields[field] = new NfoFieldBaseline(diskKey, diskKey);
                }
            }

            var outcome = NfoDriftDetector.Evaluate(movie, disk, new NfoBaselineState(fields, disk));
            ApplyOutcome(movie, outcome, importedWriteUtc, importedSize);
        }

        await db.SaveChangesAsync(ct);
    }
}
