using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class ApexRangesTests
{
    [Theory]
    [InlineData(0d, 100d, true)]
    [InlineData(100d, 100d, true)]
    [InlineData(50d, null, true)]
    [InlineData(-1d, 100d, false)]
    [InlineData(101d, 100d, false)]
    [InlineData(double.NaN, 100d, false)]
    [InlineData(double.PositiveInfinity, null, false)]
    public void ValidateTime_AcceptsOnlyFiniteTimesInsideTheMovie(double seconds, double? duration, bool valid) =>
        Assert.Equal(valid, ApexRanges.ValidateTime(seconds, duration) is null);

    [Theory]
    [InlineData(60, 5, 55)]
    [InlineData(5, 5, 0)]
    [InlineData(2, 5, 0)]
    [InlineData(60, 0, 60)]
    [InlineData(60, 12.5, 47.5)]
    public void StartFor_LeadsInButNotBeforeZero(double apex, double leadIn, double expected) =>
        Assert.Equal(expected, ApexRanges.StartFor(apex, leadIn));

    [Fact]
    public void StartFor_AnApex_UsesItsWindow() =>
        Assert.Equal(88d, ApexRanges.StartFor(new ApexItem(1, 1, 100, []) { Window = new(12, 3) }));

    [Theory]
    [InlineData(100, 5, 5, null, 95, 105)]
    [InlineData(100, 5, 5, 102.0, 95, 102)]
    [InlineData(2, 5, 5, 3600.0, 0, 7)]
    [InlineData(100, 5, 10, null, 95, 110)]
    [InlineData(100, 0, 3, null, 100, 103)]
    public void ClipFor_RunsFromTheLeadInToTheTailPastIt_InsideTheMovie(double apex, double leadIn, double tail, double? duration, double start, double end) =>
        Assert.Equal((start, end), ApexRanges.ClipFor(apex, new ApexWindow(leadIn, tail), duration));

    [Theory]
    [InlineData(100, 89, false, 11d)]
    [InlineData(100, 111.25, true, 11.25)]
    [InlineData(100, 100, false, 0d)]
    [InlineData(100, 100.0004, true, 0d)]
    [InlineData(100, 101, false, null)]
    [InlineData(100, 99, true, null)]
    public void OffsetFromPlayhead_IsThePlayheadsDistanceOnItsSide(double apex, double playhead, bool tail, double? expected) =>
        Assert.Equal(expected, ApexRanges.OffsetFromPlayhead(apex, playhead, tail));

    [Fact]
    public void Resolve_TakesEachOwnSide_ElseTheDefaults()
    {
        var defaults = new ApexWindow(5, 10);
        Assert.Equal(new ApexWindow(5, 10), ApexRanges.Resolve(null, null, defaults));
        Assert.Equal(new ApexWindow(2, 10), ApexRanges.Resolve(2, null, defaults));
        Assert.Equal(new ApexWindow(5, 30), ApexRanges.Resolve(null, 30, defaults));
        Assert.Equal(new ApexWindow(0, 1), ApexRanges.Resolve(0, 1, defaults));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0d, 1d, true)]
    [InlineData(300d, 300d, true)]
    [InlineData(-1d, null, false)]
    [InlineData(301d, null, false)]
    [InlineData(double.NaN, null, false)]
    [InlineData(null, 0d, false)]
    [InlineData(null, 0.5, false)]
    [InlineData(null, 301d, false)]
    public void ValidateWindow_AcceptsLeadInsFromZero_AndTailsFromOneSecond(double? leadIn, double? tail, bool valid) =>
        Assert.Equal(valid, ApexRanges.ValidateWindow(leadIn, tail) is null);

    [Fact]
    public void WithinClip_KeepsApexesInsideTheClip_ShiftedToItsStart()
    {
        IReadOnlyList<ApexItem> apexes =
        [
            new(1, 1, 5, []),
            new(2, 2, 100, []),
            new(3, 3, 130, []),
            new(4, 4, 200, []),
        ];

        var inClip = ApexRanges.WithinClip(apexes, 100, 200);

        // The end is exclusive, like a scene's.
        Assert.Equal([0d, 30d], inClip.Select(a => a.Seconds));
        Assert.Equal([2, 3], inClip.Select(a => a.Id));
    }

    [Theory]
    [InlineData(0d, 10, 10)]        // exactly the length away: the countdown starts
    [InlineData(0.5d, 10, 10)]      // 9.5 s left rounds up
    [InlineData(5.2d, 10, 5)]       // 4.8 s left
    [InlineData(9.999d, 10, 1)]     // just before the apex
    [InlineData(10d, 10, null)]     // at the apex it has been reached
    [InlineData(11d, 10, null)]     // past it
    [InlineData(-5d, 10, null)]     // still farther than the length
    [InlineData(0d, 0, null)]       // off
    public void CountdownFor_ShowsCeilingOfTheSecondsLeftToTheNextApexWithinTheLength(double current, int length, int? expected) =>
        Assert.Equal(expected, ApexRanges.CountdownFor([10d], current, length));

    [Fact]
    public void CountdownFor_CountsToTheNearestApexAhead_RegardlessOfOrder_SkippingPassedOnes()
    {
        double[] apexes = [100, 30, 20];

        Assert.Equal(5, ApexRanges.CountdownFor(apexes, 25, 10));    // 30 is next; 20 has passed
        Assert.Null(ApexRanges.CountdownFor(apexes, 31, 10));        // 100 is too far
        Assert.Equal(3, ApexRanges.CountdownFor(apexes, 97, 10));
    }

    [Fact]
    public void CountdownFor_WithNoApexes_IsNull() =>
        Assert.Null(ApexRanges.CountdownFor([], 5, 10));

    private static readonly IReadOnlyList<ApexItem> Unordered =
    [
        new(3, 1, 10, []),
        new(1, 2, 50, []),
        new(4, 3, 50, []),
        new(2, 4, 90, []),
    ];

    [Theory]
    [InlineData(60d, 4)]  // latest before 60 s; the tie at 50 s goes to the newer apex
    [InlineData(50d, 3)]  // strictly before, so the apexes at 50 s don't count
    [InlineData(1000d, 2)]
    [InlineData(5d, 4)]   // none before: the most recently created
    [InlineData(null, 4)] // no time: the most recently created
    public void PreviousFor_PicksTheLatestApexBefore_ElseTheNewest(double? seconds, int expectedId) =>
        Assert.Equal(expectedId, ApexRanges.PreviousFor(Unordered, seconds)?.Id);

    [Fact]
    public void PreviousFor_NoApexes_IsNull() =>
        Assert.Null(ApexRanges.PreviousFor([], 60));

    [Theory]
    [InlineData(new string[0], new string[0], "Apex")]
    [InlineData(new[] { "Aika" }, new string[0], "Aika")]
    [InlineData(new string[0], new[] { "Kiss" }, "Kiss")]
    [InlineData(new[] { "Aika", "Bea" }, new[] { "Kiss", "Squirt" }, "Aika, Bea — Kiss, Squirt")]
    public void DisplayLabel_ListsActorsThenTags(string[] actors, string[] tags, string expected)
    {
        var apex = new ApexItem(1, 1, 10, tags.Select((t, i) => new SceneTagItem(i, t, null)).ToList())
        {
            Actors = actors.Select((a, i) => new SceneActorItem(i, a)).ToList()
        };
        Assert.Equal(expected, apex.DisplayLabel);
    }

    [Theory]
    [InlineData(0, "Kiss")]
    [InlineData(1, "Kiss")]
    [InlineData(2, "Aika — Kiss")]
    public void DisplayLabel_NamesTheActorsOnlyWhenTheCastHasSeveral(int castCount, string expected)
    {
        var apex = new ApexItem(1, 1, 10, [new SceneTagItem(1, "Kiss", null)])
        {
            Actors = [new SceneActorItem(1, "Aika")],
            CastCount = castCount,
        };
        Assert.Equal(expected, apex.DisplayLabel);
    }
}
