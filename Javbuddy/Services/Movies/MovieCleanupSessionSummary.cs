namespace Javbuddy.Services.Movies;

/// <summary>One tag or actor in the session summary's added/removed lists, with the number of
/// movies it was added to or removed from.</summary>
public sealed record CleanupEditTally(string Name, int MovieCount);

/// <summary>End-of-session statistics and categorized movie lists for the Cleanup review flow
///. Derived purely from a session's recorded decisions and card edits.</summary>
public sealed record MovieCleanupSessionSummary(
    IReadOnlyList<CleanupDecision> Deleted,
    IReadOnlyList<CleanupDecision> Snoozed,
    IReadOnlyList<CleanupDecision> Blacklisted,
    IReadOnlyList<CleanupDecision> Flagged,
    IReadOnlyList<CleanupDecision> Kept,
    long BytesFreed,
    TimeSpan Duration,
    IReadOnlyList<CleanupEditTally> TagsAdded,
    IReadOnlyList<CleanupEditTally> TagsRemoved,
    IReadOnlyList<CleanupEditTally> ActorsAdded,
    IReadOnlyList<CleanupEditTally> ActorsRemoved)
{
    public static MovieCleanupSessionSummary Empty { get; } = From(null);

    public int ReviewedCount => Deleted.Count + Snoozed.Count + Blacklisted.Count + Flagged.Count + Kept.Count;

    /// <summary>Null when nothing was reviewed — there is no meaningful average then.</summary>
    public TimeSpan? AverageDecisionTime => ReviewedCount == 0 ? null : Duration / ReviewedCount;

    /// <summary>Movie-level changes, e.g. one tag added to three movies counts as three.</summary>
    public int TagsAddedCount => TagsAdded.Sum(t => t.MovieCount);
    public int TagsRemovedCount => TagsRemoved.Sum(t => t.MovieCount);
    public int ActorsAddedCount => ActorsAdded.Sum(t => t.MovieCount);
    public int ActorsRemovedCount => ActorsRemoved.Sum(t => t.MovieCount);

    /// <summary>Each reviewed movie lands in exactly one category, so the counts add up to
    /// <see cref="ReviewedCount"/>. Category follows the outcome, except that a skipped movie
    /// flagged for broken B-frames is listed as Flagged rather than Kept.</summary>
    public static MovieCleanupSessionSummary From(MovieCleanupSession? session)
    {
        if (session is null)
        {
            return new MovieCleanupSessionSummary([], [], [], [], [], 0, TimeSpan.Zero, [], [], [], []);
        }

        var decisions = session.Decisions;
        var deleted = decisions.Where(d => d.Outcome == CleanupOutcome.Deleted).ToList();
        var end = session.EndedAt ?? decisions.LastOrDefault()?.DecidedAt ?? session.StartedAt;

        return new MovieCleanupSessionSummary(
            deleted,
            decisions.Where(d => d.Outcome == CleanupOutcome.Snoozed).ToList(),
            decisions.Where(d => d.Outcome == CleanupOutcome.Blacklisted).ToList(),
            decisions.Where(d => d.Outcome == CleanupOutcome.Kept && d.FlaggedBrokenBFrames).ToList(),
            decisions.Where(d => d.Outcome == CleanupOutcome.Kept && !d.FlaggedBrokenBFrames).ToList(),
            deleted.Sum(d => d.FileSizeBytes ?? 0),
            end - session.StartedAt,
            Tally(session.Edits, CleanupEditTarget.Tag, added: true),
            Tally(session.Edits, CleanupEditTarget.Tag, added: false),
            Tally(session.Edits, CleanupEditTarget.Actor, added: true),
            Tally(session.Edits, CleanupEditTarget.Actor, added: false));
    }

    /// <summary>Groups the session's net edits by tag/actor, most-changed first.</summary>
    private static List<CleanupEditTally> Tally(IEnumerable<CleanupEdit> edits, CleanupEditTarget target, bool added) =>
        edits
            .Where(e => e.Target == target && e.Added == added)
            .GroupBy(e => e.Key)
            .Select(g => new CleanupEditTally(g.Last().Name, g.Count()))
            .OrderByDescending(t => t.MovieCount)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = (long)Math.Round(duration.TotalSeconds);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes == 0 ? $"{seconds}s" : $"{minutes}m {seconds:00}s";
    }

    public static string FormatAverage(TimeSpan? average) =>
        average is { } value ? $"{value.TotalSeconds:0.#}s" : "—";
}
