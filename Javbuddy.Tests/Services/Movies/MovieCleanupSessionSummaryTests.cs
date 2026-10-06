using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class MovieCleanupSessionSummaryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static CleanupDecision Decision(int id, CleanupOutcome outcome, long? size = null, bool flagged = false) =>
        new(id, $"SUM-{id}", $"Movie {id}", true, size, flagged, outcome, Start);

    private static MovieCleanupSession Session(DateTimeOffset? endedAt, params CleanupDecision[] decisions)
    {
        var session = new MovieCleanupSession { Order = CleanupOrder.Random, Queue = [], StartedAt = Start, EndedAt = endedAt };
        session.Decisions.AddRange(decisions);
        return session;
    }

    [Fact]
    public void From_NullSession_IsEmpty()
    {
        var summary = MovieCleanupSessionSummary.From(null);

        Assert.Equal(0, summary.ReviewedCount);
        Assert.Equal(0, summary.BytesFreed);
        Assert.Equal(TimeSpan.Zero, summary.Duration);
        Assert.Null(summary.AverageDecisionTime);
    }

    [Fact]
    public void From_CategorizesEachDecisionIntoExactlyOneList()
    {
        var summary = MovieCleanupSessionSummary.From(Session(
            Start.AddMinutes(1),
            Decision(1, CleanupOutcome.Deleted),
            Decision(2, CleanupOutcome.Snoozed),
            Decision(3, CleanupOutcome.Blacklisted),
            Decision(4, CleanupOutcome.Kept, flagged: true),
            Decision(5, CleanupOutcome.Kept),
            Decision(6, CleanupOutcome.Snoozed, flagged: true)));

        Assert.Equal([1], summary.Deleted.Select(d => d.MovieId));
        Assert.Equal([2, 6], summary.Snoozed.Select(d => d.MovieId));
        Assert.Equal([3], summary.Blacklisted.Select(d => d.MovieId));
        Assert.Equal([4], summary.Flagged.Select(d => d.MovieId));
        Assert.Equal([5], summary.Kept.Select(d => d.MovieId));
        Assert.Equal(6, summary.ReviewedCount);
    }

    [Fact]
    public void From_BytesFreed_SumsOnlyDeletedMoviesAndTreatsUnknownSizeAsZero()
    {
        var summary = MovieCleanupSessionSummary.From(Session(
            Start.AddMinutes(1),
            Decision(1, CleanupOutcome.Deleted, size: 3_000),
            Decision(2, CleanupOutcome.Deleted, size: null),
            Decision(3, CleanupOutcome.Deleted, size: 500),
            Decision(4, CleanupOutcome.Snoozed, size: 9_999)));

        Assert.Equal(3_500, summary.BytesFreed);
    }

    [Fact]
    public void From_DurationAndAverage_UseSessionStartToEnd()
    {
        var summary = MovieCleanupSessionSummary.From(Session(
            Start.AddSeconds(90),
            Decision(1, CleanupOutcome.Kept),
            Decision(2, CleanupOutcome.Kept),
            Decision(3, CleanupOutcome.Kept)));

        Assert.Equal(TimeSpan.FromSeconds(90), summary.Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), summary.AverageDecisionTime);
    }

    [Fact]
    public void From_SessionNotEnded_EndsAtLastDecision()
    {
        var late = Decision(1, CleanupOutcome.Kept) with { DecidedAt = Start.AddSeconds(45) };

        var summary = MovieCleanupSessionSummary.From(Session(null, late));

        Assert.Equal(TimeSpan.FromSeconds(45), summary.Duration);
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(60, "1m 00s")]
    [InlineData(754, "12m 34s")]
    [InlineData(4_000, "66m 40s")]
    public void FormatDuration_ShowsMinutesAndSeconds(int seconds, string expected) =>
        Assert.Equal(expected, MovieCleanupSessionSummary.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FormatAverage_ShowsSecondsOrDashWhenNothingWasReviewed()
    {
        Assert.Equal("12.5s", MovieCleanupSessionSummary.FormatAverage(TimeSpan.FromSeconds(12.5)));
        Assert.Equal("—", MovieCleanupSessionSummary.FormatAverage(null));
    }

    [Fact]
    public void From_TalliesEditsPerTagAndActor_MostChangedFirst()
    {
        var session = Session(Start.AddMinutes(1));
        session.Edits.AddRange(
        [
            CleanupEdit.Tag(1, 10, "Solo", added: true),
            CleanupEdit.Tag(1, 11, "Drama", added: true),
            CleanupEdit.Tag(2, 11, "Drama", added: true),
            CleanupEdit.Tag(3, 12, "Parent › Child", added: false),
            CleanupEdit.Actor(1, 42, "Mikami Yua", added: true),
            CleanupEdit.Actor(2, 42, "Mikami Yua", added: false),
            CleanupEdit.UntrackedActor(3, "Unknown Actress", added: false),
        ]);

        var summary = MovieCleanupSessionSummary.From(session);

        Assert.Equal([new("Drama", 2), new("Solo", 1)], summary.TagsAdded);
        Assert.Equal([new CleanupEditTally("Parent › Child", 1)], summary.TagsRemoved);
        Assert.Equal([new CleanupEditTally("Mikami Yua", 1)], summary.ActorsAdded);
        Assert.Equal([new("Mikami Yua", 1), new("Unknown Actress", 1)], summary.ActorsRemoved);
        Assert.Equal(3, summary.TagsAddedCount);
        Assert.Equal(1, summary.TagsRemovedCount);
        Assert.Equal(1, summary.ActorsAddedCount);
        Assert.Equal(2, summary.ActorsRemovedCount);
    }

    [Fact]
    public void From_NoEdits_HasEmptyEditTallies()
    {
        var summary = MovieCleanupSessionSummary.From(Session(Start.AddMinutes(1), Decision(1, CleanupOutcome.Kept)));

        Assert.Empty(summary.TagsAdded);
        Assert.Empty(summary.ActorsRemoved);
        Assert.Equal(0, summary.TagsAddedCount);
    }
}
