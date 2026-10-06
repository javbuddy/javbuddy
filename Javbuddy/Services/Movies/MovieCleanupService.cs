using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>The four queue-ordering options's Cleanup review flow offers.</summary>
public enum CleanupOrder
{
    Random,
    ByActress,
    OldestAdded,
    LargestFirst
}

/// <summary>Which flavor of the review flow a session runs in. <c>Cleanup</c> is the
/// keep/snooze/remove pruning flow; <c>Review</c> is the same card without deletion, for correcting
/// metadata and checking video quality — so it also covers favorited actors' movies, which
/// pruning deliberately leaves out.</summary>
public enum CleanupMode
{
    Cleanup,
    Review
}

/// <summary>One actor entry for a movie shown in the Cleanup review flow.</summary>
public sealed record CleanupActorItem(string Name, int? ActorId, bool HasImage);

/// <summary>One tag on the review card. ParentName is set for a subtag; NeedsReview marks a tag not
/// yet approved in the tag library.</summary>
/// <summary>IsImplicit: the movie only has the tag because one of its scenes, highlights or apexes carries it
///, so it can't be removed here.</summary>
public sealed record CleanupTagItem(int Id, string Name, string? ParentName, bool NeedsReview, bool IsImplicit = false);

public interface IMovieCleanupService
{
    /// <summary>Loads the IDs of every eligible movie (Status == Got, not blacklisted, not currently
    /// snoozed) for the Cleanup review flow, in the requested order, optionally filtered by
    /// Jellyfin library name. Materializes the whole queue up front — the page works through
    /// it in memory, not via further paged queries — but as IDs only; the page loads
    /// each card's movie with <see cref="GetQueueMovieAsync"/>. With <paramref name="actorId"/> the queue is
    /// scoped to movies linked to that actor and the favorite-actor exclusion is
    /// skipped — the user picked that actor on purpose, so a favorited actor (or a collab with
    /// one) must still be reviewable. <see cref="CleanupMode.Review"/> skips it too.
    /// With <paramref name="unreviewedOnly"/> true (the default for Review mode —)
    /// the queue contains only movies whose <c>LastReviewedAt</c> is null.</summary>
    Task<List<int>> BuildQueueAsync(CleanupOrder order, string? jellyfinLibrary = null, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false, CancellationToken ct = default);

    /// <summary>Loads one queued movie for its review card, or null if it no longer exists
    ///.</summary>
    Task<Movie?> GetQueueMovieAsync(int movieId, CancellationToken ct = default);

    /// <summary>Gets all distinct Jellyfin library names across movies currently eligible for cleanup,
    /// optionally only those of the given actor's movies (same scope rules as <see cref="BuildQueueAsync"/>).</summary>
    Task<List<string>> GetEligibleLibrariesAsync(int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, CancellationToken ct = default);

    /// <summary>Resolves the actresses/actors for a movie (from linked MovieActors and MetaActresses),
    /// including their Actor ID and whether a thumbnail image is available.</summary>
    Task<List<CleanupActorItem>> GetMovieActorsAsync(int movieId, string? metaActresses, CancellationToken ct = default);

    /// <summary>Reads the movie's current <c>MetaActresses</c> cast text (null if it has none or the
    /// movie no longer exists). The page loads the card's <see cref="Movie"/> once when the card is shown,
    /// so it re-reads this after an in-card cast edit instead of trusting that
    /// instance's stale value.</summary>
    Task<string?> GetMetaActressesAsync(int movieId, CancellationToken ct = default);

    /// <summary>Reads the movie's tags from its real MovieTag/Tag links (not the MetaGenres string
    /// cache, so each has an ID to add/remove by), ordered like Movie Detail's Genres section
    /// (parent name or own name, then name) —.</summary>
    Task<List<CleanupTagItem>> GetMovieTagsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Deletes the movie's entire on-disk folder (not just its video file — poster,
    /// .nfo, trailer, subtitles, .actors all live alongside it) if one resolves, then removes
    /// its DB record. Succeeds even if no folder resolves (already gone from disk). A "Directory not
    /// empty" failure is retried for a few seconds first (see
    /// <see cref="Infrastructure.RetryingDirectoryDelete"/>). Like Movie Detail's Delete, the folder
    /// is only deleted when it sits directly inside a configured library root (see
    /// <see cref="MovieFolderDeletion"/>); otherwise nothing is deleted and the error returned.</summary>
    Task<OperationResult> DeleteAsync(int movieId, CancellationToken ct = default);

    /// <summary>Excludes the movie from the Cleanup queue for 90 days.</summary>
    Task<OperationResult> SnoozeAsync(int movieId, CancellationToken ct = default);

    /// <summary>Permanently excludes the movie from the Cleanup queue.</summary>
    Task<OperationResult> BlacklistAsync(int movieId, CancellationToken ct = default);

    /// <summary>Reverses BlacklistAsync — surfaced as Movie Detail's "Remove from blacklist" action.</summary>
    Task<OperationResult> UnblacklistAsync(int movieId, CancellationToken ct = default);

    /// <summary>Stamps <c>LastReviewedAt</c> on the movie to UtcNow — called when the user clicks
    /// Next in Review mode. Idempotent: re-stamps with a fresh timestamp on repeat.</summary>
    Task<OperationResult> StampReviewedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Marks a movie as having broken B-frames.</summary>
    Task<OperationResult> MarkBrokenBFrameAsync(int movieId, CancellationToken ct = default);

    /// <summary>Unmarks a movie as having broken B-frames.</summary>
    Task<OperationResult> UnmarkBrokenBFrameAsync(int movieId, CancellationToken ct = default);

    /// <summary>Gets the count of movies flagged with broken B-frames.</summary>
    Task<int> GetBrokenBFrameCountAsync(CancellationToken ct = default);

    /// <summary>Gets all movie IDs currently flagged with broken B-frames.</summary>
    Task<List<int>> GetBrokenBFrameMovieIdsAsync(CancellationToken ct = default);

    /// <summary>Gets the current active cleanup session, if one exists.</summary>
    MovieCleanupSession? ActiveSession { get; }

    /// <summary>Resumes the active cleanup session if one exists, pruning any movies that are no
    /// longer eligible (e.g. deleted, blacklisted, or snoozed from another tab while away).</summary>
    Task<MovieCleanupSession?> ResumeSessionAsync(CancellationToken ct = default);

    /// <summary>Starts and tracks a new cleanup session with the given queue, order, library,
    /// optional actor scope, mode, and whether the queue was built with the unreviewed-only filter.</summary>
    void StartSession(List<int> queue, CleanupOrder order, string? library, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false);

    /// <summary>Advances the active session by one item, recording the decision made for the movie.</summary>
    void AdvanceSession(Movie movie, CleanupOutcome outcome);

    /// <summary>Ends the active session (user stopped, or the queue ran out) so the page can show its
    /// summary. The session stays tracked — and resumable as a summary — until it is discarded.</summary>
    MovieCleanupSession? EndSession();

    /// <summary>Records a successful tag add/remove on a review card for the session summary
    ///, snapshotting the tag's name ("Parent › Child" for a subtag).</summary>
    Task RecordTagEditAsync(int movieId, int tagId, bool added, CancellationToken ct = default);

    /// <summary>Records a successful cast add/remove of a linked actor on a review card for the
    /// session summary, snapshotting the actor's display name.</summary>
    Task RecordActorEditAsync(int movieId, int actorId, bool added, CancellationToken ct = default);

    /// <summary>Records the removal of an untracked (name-only) cast entry for the session summary.</summary>
    void RecordUntrackedActorRemoval(int movieId, string name);

    /// <summary>Discards the active session.</summary>
    void DiscardSession();
}

public class MovieCleanupService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    MovieChangeNotifier? movieChangeNotifier = null,
    IMovieCleanupSessionTracker? sessionTracker = null) : IMovieCleanupService
{
    private readonly IMovieCleanupSessionTracker sessionTracker = sessionTracker ?? new MovieCleanupSessionTracker();

    public MovieCleanupSession? ActiveSession => sessionTracker.ActiveSession;

    public void StartSession(List<int> queue, CleanupOrder order, string? library, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false) =>
        sessionTracker.StartSession(queue, order, library, actorId, mode, unreviewedOnly);

    public void AdvanceSession(Movie movie, CleanupOutcome outcome) => sessionTracker.AdvanceSession(movie, outcome);

    public MovieCleanupSession? EndSession() => sessionTracker.EndSession();

    public void DiscardSession() => sessionTracker.ClearSession();

    public async Task RecordTagEditAsync(int movieId, int tagId, bool added, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags
            .AsNoTracking()
            .Where(t => t.Id == tagId)
            .Select(t => new { t.Name, ParentName = t.ParentTag != null ? t.ParentTag.Name : null })
            .FirstOrDefaultAsync(ct);
        if (tag is null) return;

        var name = tag.ParentName is not null ? $"{tag.ParentName} › {tag.Name}" : tag.Name;
        sessionTracker.RecordEdit(CleanupEdit.Tag(movieId, tagId, name, added));
    }

    public async Task RecordActorEditAsync(int movieId, int actorId, bool added, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors
            .AsNoTracking()
            .Where(a => a.Id == actorId)
            .Select(a => new { a.FirstName, a.LastName })
            .FirstOrDefaultAsync(ct);
        if (actor is null) return;

        var name = ActorDisplayName.Format(actor.FirstName ?? string.Empty, actor.LastName);
        sessionTracker.RecordEdit(CleanupEdit.Actor(movieId, actorId, name, added));
    }

    public void RecordUntrackedActorRemoval(int movieId, string name) =>
        sessionTracker.RecordEdit(CleanupEdit.UntrackedActor(movieId, name, added: false));

    public async Task<MovieCleanupSession?> ResumeSessionAsync(CancellationToken ct = default)
    {
        var session = sessionTracker.ActiveSession;
        if (session is null) return null;

        // An ended session is only a summary to show — nothing to prune, and its (possibly empty)
        // queue must not trigger the exhausted-session cleanup below.
        if (session.EndedAt is not null) return session;

        if (session.Queue.Count > 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var movieIds = session.Queue.ToList();

            var eligible = ToCandidates(EligibleMovies(db, session.Library, session.ActorId, session.Mode, session.UnreviewedOnly).Where(m => movieIds.Contains(m.Id)));
            var eligibleMovies = await ApplyFavoriteExclusionAsync(db, await eligible.ToListAsync(ct), session.ActorId, session.Mode, ct);
            var eligibleIds = eligibleMovies.Select(m => m.Id).ToHashSet();
            session.Queue.RemoveAll(id => !eligibleIds.Contains(id));
        }

        // An exhausted session has nothing left to restore. Keeping it would strand the page on
        // "No movies eligible for cleanup right now" until the user hits Restart, even though the
        // library may well have gained eligible movies (new imports, expired snoozes) meanwhile —
        // so drop it and let the caller build a fresh queue, as it did before sessions persisted.
        if (session.Queue.Count == 0)
        {
            sessionTracker.ClearSession();
            return null;
        }

        return session;
    }

    public async Task<List<int>> BuildQueueAsync(CleanupOrder order, string? jellyfinLibrary = null, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var eligible = EligibleMovies(db, jellyfinLibrary, actorId, mode, unreviewedOnly);

        var candidates = order switch
        {
            CleanupOrder.OldestAdded => await ApplyFavoriteExclusionAsync(
                db, await ToCandidates(eligible.OrderBy(m => m.FileAddedAt ?? m.CreatedAt)).ToListAsync(ct), actorId, mode, ct),
            CleanupOrder.LargestFirst => await ApplyFavoriteExclusionAsync(
                db, await ToCandidates(eligible.OrderByDescending(m => m.LocalFileSizeBytes ?? 0)).ToListAsync(ct), actorId, mode, ct),
            // ThenBy(Code) breaks ties deterministically — without it, two movies with the same
            // first actress would order however SQLite happened to return the unordered base
            // query, which isn't specified.
            CleanupOrder.ByActress => await ApplyFavoriteExclusionAsync(
                db,
                (await ToCandidates(eligible).ToListAsync(ct))
                    .OrderBy(m => ActorMatching.SplitNames(m.MetaActresses).FirstOrDefault() ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                actorId, mode, ct),
            // The whole queue is materialized once (unlike Movies.razor's virtualized grid, which
            // needs a stable order across repeated Skip/Take calls and so shuffles via a seeded
            // modular bijection instead) — a plain in-memory Fisher-Yates shuffle is simpler and
            // sufficient here.
            _ => Shuffle(await ApplyFavoriteExclusionAsync(db, await ToCandidates(eligible).ToListAsync(ct), actorId, mode, ct)),
        };

        return candidates.Select(m => m.Id).ToList();
    }

    public async Task<Movie?> GetQueueMovieAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);
    }

    public async Task<List<string>> GetEligibleLibrariesAsync(int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await EligibleMovies(db, null, actorId, mode)
            .Where(m => m.JellyfinLibraryName != null && m.JellyfinLibraryName != "")
            .Select(m => m.JellyfinLibraryName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
    }

    public async Task<string?> GetMetaActressesAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies
            .AsNoTracking()
            .Where(m => m.Id == movieId)
            .Select(m => m.MetaActresses)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<CleanupTagItem>> GetMovieTagsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tags = await db.MovieTags
            .AsNoTracking()
            .Where(mt => mt.MovieId == movieId)
            .Select(mt => new CleanupTagItem(
                mt.TagId,
                mt.Tag.Name,
                mt.Tag.ParentTag != null ? mt.Tag.ParentTag.Name : null,
                mt.Tag.NeedsReview,
                !mt.IsExplicit))
            .ToListAsync(ct);

        return tags
            .OrderBy(t => t.ParentName ?? t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<CleanupActorItem>> GetMovieActorsAsync(int movieId, string? metaActresses, CancellationToken ct = default)
    {
        var castNames = ActorMatching.SplitNames(metaActresses).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var linkedActors = await db.MovieActors
            .AsNoTracking()
            .Where(ma => ma.MovieId == movieId)
            .Select(ma => new
            {
                ma.Actor.Id,
                ma.Actor.FirstName,
                ma.Actor.LastName,
                ma.Actor.JapaneseNameKanji,
                ma.Actor.JapaneseNameKana,
                ma.Actor.R18DevName,
                Aliases = ma.Actor.Aliases.Select(a => a.Name).ToList()
            })
            .ToListAsync(ct);

        var linkedLookups = linkedActors.Select(a => new
        {
            a.Id,
            DisplayName = ActorDisplayName.Format(a.FirstName ?? string.Empty, a.LastName),
            ReversedName = !string.IsNullOrWhiteSpace(a.LastName) ? $"{a.FirstName} {a.LastName}" : null,
            a.JapaneseNameKanji,
            a.JapaneseNameKana,
            a.R18DevName,
            a.Aliases
        }).ToList();

        var actorIds = linkedLookups.Select(a => a.Id).ToList();
        var actorImages = await db.ActorImages
            .AsNoTracking()
            .Where(ai => ai.Variant == "thumb" && actorIds.Contains(ai.ActorId))
            .Select(ai => ai.ActorId)
            .ToHashSetAsync(ct);

        if (castNames.Count == 0)
        {
            return linkedLookups.Select(a =>
                new CleanupActorItem(a.DisplayName, a.Id, actorImages.Contains(a.Id))).ToList();
        }

        var result = new List<CleanupActorItem>();
        var includedActorIds = new HashSet<int>();

        foreach (var name in castNames)
        {
            var compactName = name.Replace(" ", "");
            var match = linkedLookups.FirstOrDefault(actor =>
                string.Equals(actor.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || (actor.ReversedName is not null && string.Equals(actor.ReversedName, name, StringComparison.OrdinalIgnoreCase))
                || (actor.JapaneseNameKanji is not null && (string.Equals(actor.JapaneseNameKanji, name, StringComparison.OrdinalIgnoreCase) || actor.JapaneseNameKanji.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)))
                || (actor.JapaneseNameKana is not null && (string.Equals(actor.JapaneseNameKana, name, StringComparison.OrdinalIgnoreCase) || actor.JapaneseNameKana.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)))
                || (actor.R18DevName is not null && string.Equals(actor.R18DevName, name, StringComparison.OrdinalIgnoreCase))
                || actor.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase) || a.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)));

            if (match is not null)
            {
                includedActorIds.Add(match.Id);
                result.Add(new CleanupActorItem(match.DisplayName, match.Id, actorImages.Contains(match.Id)));
            }
            else
            {
                result.Add(new CleanupActorItem(name, null, false));
            }
        }

        foreach (var actor in linkedLookups)
        {
            if (!includedActorIds.Contains(actor.Id))
            {
                result.Add(new CleanupActorItem(actor.DisplayName, actor.Id, actorImages.Contains(actor.Id)));
            }
        }

        return result;
    }

    public async Task<OperationResult> DeleteAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        if (await MovieFolderDeletion.DeleteAsync(localLibraryClient, movie.Code, ct) is { } error)
        {
            return OperationResult.Fail(error);
        }

        await DeletedMovieHistory.RecordAsync(db, movie, ct);
        db.Movies.Remove(movie);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();
        return OperationResult.Ok();
    }

    public Task<OperationResult> SnoozeAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.CleanupSnoozedUntil = DateTime.UtcNow.AddDays(90), ct);

    public Task<OperationResult> BlacklistAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.CleanupBlacklisted = true, ct);

    public Task<OperationResult> UnblacklistAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.CleanupBlacklisted = false, ct);

    public Task<OperationResult> StampReviewedAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.LastReviewedAt = DateTime.UtcNow, ct);

    public Task<OperationResult> MarkBrokenBFrameAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.HasBrokenBFrames = true, ct);

    public Task<OperationResult> UnmarkBrokenBFrameAsync(int movieId, CancellationToken ct = default) =>
        SetFlagAsync(movieId, movie => movie.HasBrokenBFrames = false, ct);

    public async Task<int> GetBrokenBFrameCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies.CountAsync(m => m.HasBrokenBFrames && m.Status == MovieStatus.Got, ct);
    }

    public async Task<List<int>> GetBrokenBFrameMovieIdsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies
            .AsNoTracking()
            .Where(m => m.HasBrokenBFrames && m.Status == MovieStatus.Got)
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    private async Task<OperationResult> SetFlagAsync(int movieId, Action<Movie> apply, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        apply(movie);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();
        return OperationResult.Ok();
    }

    private static List<QueueCandidate> Shuffle(List<QueueCandidate> movies)
    {
        var rng = Random.Shared;
        for (var i = movies.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (movies[i], movies[j]) = (movies[j], movies[i]);
        }
        return movies;
    }

    /// <summary>The queue's eligibility rules (Status == Got, not blacklisted, not currently snoozed),
    /// scoped to <paramref name="actorId"/> when given. Only the unscoped Cleanup queue also drops movies
    /// with a favorited linked actor here (its MetaActresses counterpart is
    /// <see cref="ApplyFavoriteExclusionAsync"/>).</summary>
    /// <summary>The queue's eligibility rules (Status == Got, not blacklisted, not currently snoozed),
    /// scoped to <paramref name="actorId"/> when given. Only the unscoped Cleanup queue also drops movies
    /// with a favorited linked actor here (its MetaActresses counterpart is
    /// <see cref="ApplyFavoriteExclusionAsync"/>). With <paramref name="unreviewedOnly"/> true
    /// the query adds <c>LastReviewedAt IS NULL</c>.</summary>
    private static IQueryable<Movie> EligibleMovies(AppDbContext db, string? jellyfinLibrary, int? actorId, CleanupMode mode, bool unreviewedOnly = false)
    {
        var now = DateTime.UtcNow;
        var eligible = db.Movies.AsNoTracking().Where(m =>
            m.Status == MovieStatus.Got &&
            !m.CleanupBlacklisted &&
            (m.CleanupSnoozedUntil == null || m.CleanupSnoozedUntil <= now));

        if (unreviewedOnly)
        {
            eligible = eligible.Where(m => m.LastReviewedAt == null);
        }

        if (actorId is { } scopedActorId)
        {
            eligible = eligible.Where(m => m.MovieActors.Any(ma => ma.ActorId == scopedActorId));
        }
        else if (ExcludesFavorites(actorId, mode))
        {
            eligible = eligible.Where(m => !m.MovieActors.Any(ma => ma.Actor.IsFavorite));
        }

        if (!string.IsNullOrWhiteSpace(jellyfinLibrary))
        {
            eligible = eligible.Where(m => m.JellyfinLibraryName == jellyfinLibrary);
        }

        return eligible;
    }

    /// <summary>Only the unscoped Cleanup queue protects favorited actors' movies from pruning; a scoped
    /// queue and Review mode reach them on purpose.</summary>
    private static bool ExcludesFavorites(int? actorId, CleanupMode mode) => actorId is null && mode == CleanupMode.Cleanup;

    /// <summary>The only columns queue ordering and favorite exclusion read, so building or pruning a
    /// library-sized queue doesn't materialize every movie's full row.</summary>
    private sealed record QueueCandidate(int Id, string? Code, string? MetaActresses);

    private static IQueryable<QueueCandidate> ToCandidates(IQueryable<Movie> movies) =>
        movies.Select(m => new QueueCandidate(m.Id, m.Code, m.MetaActresses));

    private static async Task<List<QueueCandidate>> ApplyFavoriteExclusionAsync(AppDbContext db, List<QueueCandidate> movies, int? actorId, CleanupMode mode, CancellationToken ct) =>
        ExcludesFavorites(actorId, mode) ? ExcludeFavoriteMetaActresses(movies, await GetFavoriteActorsAsync(db, ct)) : movies;

    private static async Task<List<ActorMatching.ActorLookup>> GetFavoriteActorsAsync(AppDbContext db, CancellationToken ct)
    {
        var raw = await db.Actors
            .AsNoTracking()
            .Where(a => a.FirstName != null && a.IsFavorite)
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.JapaneseNameKanji,
                a.JapaneseNameKana,
                a.R18DevName,
                Aliases = a.Aliases.Select(al => al.Name).ToList()
            })
            .ToListAsync(ct);

        return raw.Select(a => new ActorMatching.ActorLookup(
            a.Id,
            ActorDisplayName.Format(a.FirstName!, a.LastName),
            !string.IsNullOrWhiteSpace(a.LastName) ? $"{a.FirstName} {a.LastName}" : null,
            a.JapaneseNameKanji,
            a.JapaneseNameKana,
            a.R18DevName,
            a.Aliases)).ToList();
    }

    private static List<QueueCandidate> ExcludeFavoriteMetaActresses(List<QueueCandidate> movies, List<ActorMatching.ActorLookup> favoriteActors)
    {
        if (favoriteActors.Count == 0) return movies;
        return movies.Where(m =>
        {
            if (string.IsNullOrWhiteSpace(m.MetaActresses)) return true;
            var names = ActorMatching.SplitNames(m.MetaActresses).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return names.Count == 0 || !favoriteActors.Any(fa => fa.Matches(names));
        }).ToList();
    }
}
