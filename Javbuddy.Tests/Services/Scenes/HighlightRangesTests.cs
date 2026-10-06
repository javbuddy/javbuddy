using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class HighlightRangesTests
{
    [Theory]
    [InlineData(0, 10, null, null)]
    [InlineData(890, 900, 900d, null)]
    [InlineData(-1, 10, null, "Start can't be negative.")]
    [InlineData(10, 10, null, "End must be after start.")]
    [InlineData(20, 10, null, "End must be after start.")]
    [InlineData(890, 900.5, 900d, "End can't be past the end of the movie.")]
    [InlineData(double.NaN, 10, null, "Start and end must be valid times.")]
    [InlineData(0, double.PositiveInfinity, null, "Start and end must be valid times.")]
    public void Validate_EnforcesRangeAndDuration(double start, double end, double? duration, string? expected) =>
        Assert.Equal(expected, HighlightRanges.Validate(start, end, duration));

    [Fact]
    public void DisplayTitle_FallsBackToPosition()
    {
        Assert.Equal("Highlight 3", HighlightRanges.DisplayTitle("  ", 3));
        Assert.Equal("Climax", HighlightRanges.DisplayTitle("Climax", 3));
    }

    [Fact]
    public void DisplayTitle_WithoutATitle_NamesTheActorsThenTheTags()
    {
        Assert.Equal("Aika, Bea — Creampie", HighlightRanges.DisplayTitle(null, 3, ["Aika", "Bea"], ["Creampie"]));
        Assert.Equal("Creampie, Squirt", HighlightRanges.DisplayTitle("", 3, [], ["Creampie", "Squirt"]));
        Assert.Equal("Aika", HighlightRanges.DisplayTitle(null, 3, ["Aika"], []));
        Assert.Equal("Highlight 3", HighlightRanges.DisplayTitle(null, 3, [], []));
        Assert.Equal("Climax", HighlightRanges.DisplayTitle("Climax", 3, ["Aika"], ["Creampie"]));
    }

    [Fact]
    public void ActorsToName_LeavesThemOutInASoloMovie()
    {
        Assert.Empty(ActorTagLabel.ActorsToName(["Mei"], 1));
        Assert.Empty(ActorTagLabel.ActorsToName(["Mei"], 0));
        Assert.Equal(["Aika"], ActorTagLabel.ActorsToName(["Aika"], 2));
    }

    [Fact]
    public void AssignLanes_StacksOverlapsAndReusesFreedLanes()
    {
        var lanes = HighlightRanges.AssignLanes([(0, 100), (50, 150), (60, 70), (100, 120), (160, 200)]);

        // (50,150) overlaps lane 0, (60,70) overlaps both, (100,120) fits lane 0 again since it only
        // touches (0,100), and (160,200) is clear of everything.
        Assert.Equal([0, 1, 2, 0, 0], lanes);
    }

    private static HighlightItem Highlight(int id, double start) =>
        new(id, id, $"Highlight {id}", null, start, start + 5, false, 0);

    [Fact]
    public void CountStartsByScene_CountsEachHighlightOnceByItsStart()
    {
        IReadOnlyList<SceneItem> scenes =
        [
            new(1, 1, "Scene 1", null, 10, 100, 100),
            new(2, 2, "Scene 2", null, 100, 200, 200),
            // Ends at 300, then a gap until the open-ended last scene.
            new(3, 3, "Scene 3", null, 250, 300, 300),
            new(4, 4, "Scene 4", null, 400, null, null),
        ];

        var counts = HighlightRanges.CountStartsByScene(scenes,
        [
            Highlight(1, 5),    // before the first scene
            Highlight(2, 10),   // on scene 1's start
            Highlight(3, 95),   // runs past scene 1's end, still counts only there
            Highlight(4, 100),  // an end is exclusive: scene 2
            Highlight(5, 320),  // in the gap after scene 3
            Highlight(6, 5000), // open-ended last scene
        ]);

        Assert.Equal(new Dictionary<int, int> { [1] = 2, [2] = 1, [4] = 1 }, counts);
    }

    [Fact]
    public void CountStartsByScene_PlainStartTimes_MapToScenesTheSameWay()
    {
        IReadOnlyList<SceneItem> scenes =
        [
            new(1, 1, "Scene 1", null, 10, 100, 100),
            new(2, 2, "Scene 2", null, 100, 200, 200),
            new(3, 3, "Scene 3", null, 250, 300, 300),
        ];

        var counts = HighlightRanges.CountStartsByScene(scenes, new double[] { 5, 10, 99.9, 100, 220, 320 });

        Assert.Equal(new Dictionary<int, int> { [1] = 2, [2] = 1 }, counts);
    }

    [Fact]
    public void CountStartsByScene_NoScenes_CountsNothing() =>
        Assert.Empty(HighlightRanges.CountStartsByScene([], [Highlight(1, 5)]));

    [Fact]
    public void AssignLanes_EmptyInput_ReturnsEmpty() =>
        Assert.Empty(HighlightRanges.AssignLanes([]));

    [Fact]
    public void WithinClip_KeepsOverlappingHighlights_ShiftedToTheClipAndClampedToIt()
    {
        static HighlightItem Range(int id, double start, double end, int lane) =>
            new(id, id, $"Highlight {id}", null, start, end, false, lane);

        var inClip = HighlightRanges.WithinClip(
        [
            Range(1, 50, 100, 0),   // ends where the clip starts: left out
            Range(2, 90, 130, 0),   // starts before the clip: clamped to 0:00
            Range(3, 120, 140, 1),  // inside, overlaps 2
            Range(4, 150, 250, 2),  // ends after the clip: clamped to its end
            Range(5, 200, 220, 0),  // starts where the clip ends: left out
        ], 100, 200);

        Assert.Equal(
            [(2, 0d, 30d, 0), (3, 20d, 40d, 1), (4, 50d, 100d, 0)],
            inClip.Select(h => (h.Id, h.StartSeconds, h.EndSeconds, h.Lane)));
    }

    [Fact]
    public void WithinClip_NoHighlightsInRange_ReturnsEmpty() =>
        Assert.Empty(HighlightRanges.WithinClip([Highlight(1, 5)], 100, 200));
}
