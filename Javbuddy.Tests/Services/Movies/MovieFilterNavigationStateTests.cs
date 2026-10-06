using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class MovieFilterNavigationStateTests
{
    private sealed class SettableTimeProvider : TimeProvider
    {
        public DateTimeOffset CurrentTime { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => CurrentTime;
    }

    [Fact]
    public void SetPendingFilter_Studio_CanBePeekedAndConsumed()
    {
        var clock = new SettableTimeProvider();
        var state = new MovieFilterNavigationState(clock);

        state.SetPendingFilter(studio: "S1");

        var peeked = state.PeekPendingFilter();
        Assert.NotNull(peeked);
        Assert.Equal("S1", peeked.Studio);
        Assert.Null(peeked.Genre);

        var consumed = state.ConsumePendingFilter();
        Assert.NotNull(consumed);
        Assert.Equal("S1", consumed.Studio);

        Assert.Null(state.PeekPendingFilter());
        Assert.Null(state.ConsumePendingFilter());
    }

    [Fact]
    public void SetPendingFilter_Genre_CanBePeekedAndConsumed()
    {
        var clock = new SettableTimeProvider();
        var state = new MovieFilterNavigationState(clock);

        state.SetPendingFilter(genre: "Drama");

        var peeked = state.PeekPendingFilter();
        Assert.NotNull(peeked);
        Assert.Null(peeked.Studio);
        Assert.Equal("Drama", peeked.Genre);

        var consumed = state.ConsumePendingFilter();
        Assert.NotNull(consumed);
        Assert.Equal("Drama", consumed.Genre);

        Assert.Null(state.PeekPendingFilter());
    }

    [Fact]
    public void PendingFilter_ExpiresAfter30Seconds()
    {
        var clock = new SettableTimeProvider();
        var state = new MovieFilterNavigationState(clock);

        state.SetPendingFilter(studio: "Attackers");

        clock.CurrentTime = clock.CurrentTime.AddSeconds(29);
        Assert.NotNull(state.PeekPendingFilter());

        clock.CurrentTime = clock.CurrentTime.AddSeconds(2);
        Assert.Null(state.PeekPendingFilter());
        Assert.Null(state.ConsumePendingFilter());
    }

    [Fact]
    public void Clear_RemovesPendingFilterImmediately()
    {
        var clock = new SettableTimeProvider();
        var state = new MovieFilterNavigationState(clock);

        state.SetPendingFilter(genre: "Creampie");
        Assert.NotNull(state.PeekPendingFilter());

        state.Clear();
        Assert.Null(state.PeekPendingFilter());
    }
}
