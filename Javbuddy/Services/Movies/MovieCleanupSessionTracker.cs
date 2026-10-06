using Javbuddy.Models;

namespace Javbuddy.Services.Movies;

/// <summary>What the user decided for a movie in the Cleanup review flow. <c>Kept</c> is Skip —
/// the movie is left untouched and may reappear in a future session.</summary>
public enum CleanupOutcome
{
    Kept,
    Deleted,
    Snoozed,
    Blacklisted
}

/// <summary>Snapshot of one reviewed movie, taken at decision time. A snapshot rather than a
/// <see cref="Movie"/> reference because a deleted movie's row is gone by the time the end-of-session
/// summary lists it.</summary>
public sealed record CleanupDecision(
    int MovieId,
    string? Code,
    string DisplayName,
    bool HasCover,
    long? FileSizeBytes,
    bool FlaggedBrokenBFrames,
    CleanupOutcome Outcome,
    DateTimeOffset DecidedAt)
{
    public static CleanupDecision From(Movie movie, CleanupOutcome outcome, DateTimeOffset decidedAt) => new(
        movie.Id,
        movie.Code,
        movie.DisplayName,
        !string.IsNullOrWhiteSpace(movie.MetaSourceName) && !string.IsNullOrWhiteSpace(movie.Code),
        movie.LocalFileSizeBytes,
        movie.HasBrokenBFrames,
        outcome,
        decidedAt);
}

/// <summary>What a card edit in the Cleanup review flow changed.</summary>
public enum CleanupEditTarget
{
    Tag,
    Actor
}

/// <summary>One tag or cast change made on a review card during a session.
/// <paramref name="Key"/> identifies the tag/actor (IDs, or the name for an untracked cast entry);
/// <paramref name="Name"/> is snapshotted for the summary, like <see cref="CleanupDecision"/>.</summary>
public sealed record CleanupEdit(int MovieId, CleanupEditTarget Target, string Key, string Name, bool Added)
{
    public static CleanupEdit Tag(int movieId, int tagId, string name, bool added) =>
        new(movieId, CleanupEditTarget.Tag, $"tag:{tagId}", name, added);

    public static CleanupEdit Actor(int movieId, int actorId, string name, bool added) =>
        new(movieId, CleanupEditTarget.Actor, $"actor:{actorId}", name, added);

    /// <summary>A cast entry with no linked Actor row, which can only be removed by name.</summary>
    public static CleanupEdit UntrackedActor(int movieId, string name, bool added) =>
        new(movieId, CleanupEditTarget.Actor, $"name:{name.Trim().ToUpperInvariant()}", name.Trim(), added);
}

/// <summary>Represents an in-progress Movie Cleanup review session.</summary>
public sealed class MovieCleanupSession
{
    public required CleanupOrder Order { get; init; }
    public string? Library { get; init; }

    /// <summary>Set when the session was launched from an Actor Detail page: the queue
    /// then only holds that actor's movies, and resuming prunes to the same scope.</summary>
    public int? ActorId { get; init; }

    /// <summary>Cleanup (prune) or Review (no deletion). Resuming prunes the queue by this
    /// mode's eligibility rules.</summary>
    public CleanupMode Mode { get; init; }

    /// <summary>When true, the session's queue was built with only unreviewed movies
    /// (<c>LastReviewedAt IS NULL</c>). Stored so a resumed session can be pruned with the same rule.</summary>
    public bool UnreviewedOnly { get; init; }

    /// <summary>IDs of the movies still to review, in queue order — IDs rather than <see cref="Movie"/>
    /// entities, since this singleton can hold a library-sized queue for as long as the
    /// session lives. The page loads the current card's movie on demand.</summary>
    public required List<int> Queue { get; init; }
    public int CurrentIndex { get; set; }
    public int ReviewedCount { get; set; }
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Set when the user stops or the queue runs out; the session then only exists to
    /// show its end-of-session summary until the user leaves it.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public List<CleanupDecision> Decisions { get; } = [];

    /// <summary>Net tag/cast changes made on review cards: an edit that undoes an
    /// earlier one on the same movie removes it rather than being recorded.</summary>
    public List<CleanupEdit> Edits { get; } = [];
}

public interface IMovieCleanupSessionTracker
{
    /// <summary>Gets the current active cleanup session, if one exists.</summary>
    MovieCleanupSession? ActiveSession { get; }

    /// <summary>Whether an active session is currently tracked.</summary>
    bool HasActiveSession { get; }

    /// <summary>Starts a new session with the specified queue, order, library filter, optional
    /// actor scope, and mode.</summary>
    MovieCleanupSession StartSession(List<int> queue, CleanupOrder order, string? library, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false);

    /// <summary>Advances the active session by one item, updating index and reviewed count, and
    /// records the decision made for <paramref name="movie"/>.</summary>
    void AdvanceSession(Movie movie, CleanupOutcome outcome);

    /// <summary>Records a successful tag/cast edit on the active, not yet ended session as a net
    /// change: an edit reversing a recorded one for the same movie and tag/actor cancels it out.</summary>
    void RecordEdit(CleanupEdit edit);

    /// <summary>Marks the active session as ended so its summary can be shown, returning it (null
    /// when there is none). Ending an already-ended session keeps its original end time.</summary>
    MovieCleanupSession? EndSession();

    /// <summary>Clears/discards the current active session.</summary>
    void ClearSession();
}

/// <summary>Thread-safe singleton tracking the active Movie Cleanup review session across
/// circuit drops, tab switches, and page navigations.</summary>
public sealed class MovieCleanupSessionTracker(TimeProvider? timeProvider = null) : IMovieCleanupSessionTracker
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private MovieCleanupSession? activeSession;

    public MovieCleanupSession? ActiveSession
    {
        get
        {
            lock (gate)
            {
                return activeSession;
            }
        }
    }

    public bool HasActiveSession
    {
        get
        {
            lock (gate)
            {
                return activeSession is not null;
            }
        }
    }

    public MovieCleanupSession StartSession(List<int> queue, CleanupOrder order, string? library, int? actorId = null, CleanupMode mode = CleanupMode.Cleanup, bool unreviewedOnly = false)
    {
        lock (gate)
        {
            activeSession = new MovieCleanupSession
            {
                Order = order,
                Library = library,
                ActorId = actorId,
                Mode = mode,
                UnreviewedOnly = unreviewedOnly,
                Queue = queue,
                CurrentIndex = 0,
                ReviewedCount = 0,
                StartedAt = this.timeProvider.GetUtcNow()
            };
            return activeSession;
        }
    }

    public void AdvanceSession(Movie movie, CleanupOutcome outcome)
    {
        lock (gate)
        {
            if (activeSession is null) return;
            activeSession.CurrentIndex++;
            activeSession.ReviewedCount++;
            activeSession.Decisions.Add(CleanupDecision.From(movie, outcome, this.timeProvider.GetUtcNow()));
        }
    }

    public void RecordEdit(CleanupEdit edit)
    {
        lock (gate)
        {
            if (activeSession is not { EndedAt: null } session) return;

            var existing = session.Edits.FindIndex(e =>
                e.MovieId == edit.MovieId && e.Target == edit.Target && e.Key == edit.Key);
            if (existing < 0)
            {
                session.Edits.Add(edit);
            }
            else if (session.Edits[existing].Added != edit.Added)
            {
                session.Edits.RemoveAt(existing);
            }
        }
    }

    public MovieCleanupSession? EndSession()
    {
        lock (gate)
        {
            activeSession?.EndedAt ??= this.timeProvider.GetUtcNow();
            return activeSession;
        }
    }

    public void ClearSession()
    {
        lock (gate)
        {
            activeSession = null;
        }
    }
}
