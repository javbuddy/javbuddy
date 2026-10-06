using Javbuddy.Services.Tasks;

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
/// write (it never invents a missing element).</summary>
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

    /// <summary>The metadata editor modal's "Save" check — same shape as
    /// CheckMovieNfoConflictAsync, but flags drift more strictly for every field that modal can
    /// edit (title, original title, plot, director, studio, label, series, release date,
    /// runtime): the modal never writes to the .nfo itself, so a field whose target element is
    /// blank or entirely absent still needs to surface as a conflict rather than silently vanish
    /// — see NfoDriftDetector.AppendFieldConflicts' remarks for the full leniency rationale.</summary>
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

    /// <summary>The "Refresh with Javinizer" field picker's apply step: writes the movie's
    /// current descriptive Meta* fields into its local .nfo file, if one exists, via the same
    /// conservative element-reconciliation ApplyFieldUpdates already uses for the diff preview
    /// (GenerateProposedNfoAsync) — only updates an element that already exists on disk with some
    /// value, never invents one. A deliberate departure from MovieService.UpdateMetadataAsync's
    /// DB-only precedent, since the spec explicitly asks for a direct .nfo write on
    /// confirm. Re-checks remaining .nfo drift afterward, since a selected field with no matching
    /// on-disk element (or an unselected field) can still leave real drift. No-ops (returns false)
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
