using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class SceneBoundaryDetectorTests
{
    private static readonly SceneDetectionOptions Options = new() { MinBlackSeconds = 1.0, MinCutScore = 30, MinSpacingSeconds = 60, StartMarginSeconds = 5, EndMarginSeconds = 30, NearbySeconds = 5 };

    private static IReadOnlyList<DetectedBoundary> Suggest(
        IReadOnlyList<FfmpegBlackStretch>? blacks = null,
        IReadOnlyList<FfmpegSceneCut>? cuts = null,
        double duration = 3600,
        IEnumerable<double>? existing = null,
        IEnumerable<double>? dismissed = null,
        SceneDetectionOptions? options = null) =>
        SceneBoundaryDetector.Suggest(new FfmpegSceneSignals(blacks ?? [], cuts ?? []), duration, options ?? Options, existing ?? [], dismissed ?? []);

    [Fact]
    public void BlackStretch_SuggestsWhereThePictureReturns_IgnoringShortFlashes()
    {
        var result = Suggest(blacks: [new(600, 601.2), new(1200, 1200.8)]);

        Assert.Equal([new DetectedBoundary(601.2, SceneSuggestionSource.Black, 1.2000000000000455)], result);
    }

    [Fact]
    public void Cuts_NeedTheMinimumScore_AndCanBeSwitchedOff()
    {
        FfmpegSceneCut[] cuts = [new(600, 45), new(1300, 20)];

        Assert.Equal([600d], Suggest(cuts: cuts).Select(b => b.Seconds));
        Assert.Empty(Suggest(cuts: cuts, options: Options with { MinCutScore = null }));
    }

    [Fact]
    public void Spacing_PrefersBlackBreaks_ThenHigherScores()
    {
        var result = Suggest(
            blacks: [new(620, 621)],
            cuts: [new(600, 90), new(1000, 40), new(1030, 70)]);

        // The cut at 600 loses to the nearby black break; of the two cuts 30 s apart, the stronger wins.
        Assert.Equal([(621d, SceneSuggestionSource.Black), (1030d, SceneSuggestionSource.Cut)], result.Select(b => (b.Seconds, b.Source)));
    }

    [Fact]
    public void DropsEdges_ExistingSceneStarts_AndDismissedPoints()
    {
        var result = Suggest(
            cuts: [new(2, 50), new(600, 50), new(1200, 50), new(1800, 50), new(3580, 50)],
            existing: [598],
            dismissed: [1203]);

        Assert.Equal([1800d], result.Select(b => b.Seconds));
    }

    [Fact]
    public void OpeningTitleCard_FadingIn_IsSuggested()
    {
        // MIDE-536 opens on ~10 s of black before the first scene.
        Assert.Equal([9.9], Suggest(blacks: [new(0, 9.9)]).Select(b => b.Seconds));
    }

    [Fact]
    public void DefaultOptions_OnMide536_SuggestTheRealSceneBreaksOnly()
    {
        // blackdetect/scdet output of the full MIDE-536 run (research). The three
        // mid-movie blacks were checked frame by frame to be scene changes; the opening black ends
        // at the first scene; the last one is the credits -> warning-card outro.
        FfmpegBlackStretch[] blacks =
        [
            new(0.067, 0.7006), new(8.475044, 9.910178), new(1152.586189, 1154.955222), new(2917.751356, 2921.755356),
            new(4588.688956, 4592.592856), new(7108.407678, 7118.986289), new(7126.627256, 7127.760989),
        ];
        FfmpegSceneCut[] cuts = [new(1500, 34), new(3000, 25)];

        var result = SceneBoundaryDetector.Suggest(new FfmpegSceneSignals(blacks, cuts), 7127.794722, new SceneDetectionOptions(), [], []);

        Assert.Equal([9.910178, 1154.955222, 2921.755356, 4592.592856], result.Select(b => b.Seconds));
    }
}
