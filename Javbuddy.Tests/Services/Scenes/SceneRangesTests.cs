using Javbuddy.Models;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class SceneRangesTests
{
    private static Scene S(double start, double? end = null, string? title = null, int id = 0) =>
        new() { Id = id, StartSeconds = start, EndSeconds = end, Title = title };

    [Fact]
    public void ResolveEffectiveRanges_OrdersByStartAndFillsNullEnds()
    {
        var resolved = SceneRanges.ResolveEffectiveRanges([S(600), S(0, 120), S(300)], durationSeconds: 900);

        Assert.Equal([0d, 300d, 600d], resolved.Select(r => r.Scene.StartSeconds));
        Assert.Equal([1, 2, 3], resolved.Select(r => r.Position));
        // Explicit end kept (leaving a gap), null end runs to the next start, last one to the duration.
        Assert.Equal([120d, 600d, 900d], resolved.Select(r => r.EffectiveEndSeconds!.Value));
    }

    [Fact]
    public void ResolveEffectiveRanges_LastSceneWithoutEndAndUnknownDuration_HasNullEffectiveEnd()
    {
        var resolved = SceneRanges.ResolveEffectiveRanges([S(0), S(300)], durationSeconds: null);

        Assert.Equal(300d, resolved[0].EffectiveEndSeconds);
        Assert.Null(resolved[1].EffectiveEndSeconds);
    }

    [Theory]
    [InlineData(null, 2, "Scene 2")]
    [InlineData("  ", 1, "Scene 1")]
    [InlineData("Interview", 3, "Interview")]
    public void DisplayTitle_FallsBackToPosition(string? title, int position, string expected) =>
        Assert.Equal(expected, SceneRanges.DisplayTitle(title, position));

    [Theory]
    [InlineData(-1, null, "Start can't be negative.")]
    [InlineData(100, 100.0, "End must be after start.")]
    [InlineData(100, 50.0, "End must be after start.")]
    [InlineData(900, null, "Start must be before the end of the movie.")]
    [InlineData(800, 901.0, "End can't be past the end of the movie.")]
    [InlineData(double.NaN, null, "Start and end must be valid times.")]
    public void Validate_RejectsOutOfBounds(double start, double? end, string expected) =>
        Assert.Equal(expected, SceneRanges.Validate(S(start, end), [], durationSeconds: 900));

    [Fact]
    public void Validate_AllowsEndExactlyAtDuration() =>
        Assert.Null(SceneRanges.Validate(S(800, 900), [], durationSeconds: 900));

    [Fact]
    public void Validate_UnknownDuration_SkipsDurationBounds() =>
        Assert.Null(SceneRanges.Validate(S(99999, 100000), [], durationSeconds: null));

    [Fact]
    public void Validate_AllowsGapsAndTouchingRanges()
    {
        Scene[] others = [S(0, 100, id: 1), S(300, id: 2)];

        Assert.Null(SceneRanges.Validate(S(150, 200), others, 900)); // in the gap
        Assert.Null(SceneRanges.Validate(S(100, 300), others, 900)); // touches both neighbours
    }

    [Fact]
    public void Validate_NullEndCandidate_NeverOverlapsTheNextScene() =>
        Assert.Null(SceneRanges.Validate(S(200), [S(300, id: 1)], 900));

    [Fact]
    public void Validate_RejectsEndPastNextStart() =>
        Assert.Equal("Overlaps \"Scene 1\".", SceneRanges.Validate(S(200, 350), [S(300, id: 1)], 900));

    [Fact]
    public void Validate_RejectsStartInsidePreviousExplicitRange() =>
        Assert.Equal("Overlaps \"Intro\".", SceneRanges.Validate(S(50), [S(0, 100, "Intro", id: 1)], 900));

    [Fact]
    public void Validate_RejectsSameStart() =>
        Assert.Equal("Overlaps \"Scene 1\".", SceneRanges.Validate(S(0, 10), [S(0, id: 1)], 900));

    [Fact]
    public void Validate_NamesConflictByPositionAmongExistingScenes()
    {
        // Inserting before both would renumber them; the message uses the numbering the user sees.
        Scene[] others = [S(100, id: 1), S(500, id: 2)];

        Assert.Equal("Overlaps \"Scene 1\".", SceneRanges.Validate(S(50, 150), others, 900));
    }

    [Fact]
    public void Validate_IgnoresPreexistingOverlapNotInvolvingCandidate()
    {
        Scene[] others = [S(0, 200, id: 1), S(100, id: 2)];

        Assert.Null(SceneRanges.Validate(S(600, 700), others, 900));
    }
}
