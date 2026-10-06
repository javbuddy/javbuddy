using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.Scenes;

/// <summary>Tunables for turning raw ffmpeg signals into suggestions; defaults from the research on
/// real movies documented on.</summary>
public sealed record SceneDetectionOptions
{
    /// <summary>Black stretches shorter than this are flashes, not scene breaks. Every real
    /// boundary in the research sample was a black of 1.1 s or longer.</summary>
    public double MinBlackSeconds { get; init; } = 1.0;

    /// <summary>Hard cuts need at least this scdet score; null (the default) ignores them. On the
    /// real movies checked for every hard cut — scores 9 to 35, the highest included —
    /// was a camera-angle change inside one scene, not a boundary between scenes.</summary>
    public double? MinCutScore { get; init; }

    /// <summary>JAV has many hard cuts inside one scene, so suggestions closer together than this are
    /// thinned out (black breaks win, then higher scores).</summary>
    public double MinSpacingSeconds { get; init; } = 60;

    /// <summary>Points this close to the start of the file aren't suggested. Small, so the fade from
    /// a studio logo or title card into the first scene still is.</summary>
    public double StartMarginSeconds { get; init; } = 5;

    /// <summary>Points this close to the end of the file aren't suggested: movies end on a long black
    /// followed by a few seconds of credits/warning card, which isn't a scene.</summary>
    public double EndMarginSeconds { get; init; } = 30;

    /// <summary>Points this close to an existing scene start or a dismissed suggestion are dropped.</summary>
    public double NearbySeconds { get; init; } = 5;
}

public sealed record DetectedBoundary(double Seconds, SceneSuggestionSource Source, double Score);

/// <summary>Combines blackdetect/scdet output into suggested scene boundaries. Pure, so
/// the thresholds are testable without decoding a movie.</summary>
public static class SceneBoundaryDetector
{
    public static IReadOnlyList<DetectedBoundary> Suggest(
        FfmpegSceneSignals signals,
        double durationSeconds,
        SceneDetectionOptions options,
        IEnumerable<double> existingSceneStarts,
        IEnumerable<double> dismissedSeconds)
    {
        var candidates = signals.BlackStretches
            .Select(b => ToCandidate(b, durationSeconds, options))
            .Concat(signals.Cuts.Select(c => ToCandidate(c, durationSeconds, options)))
            .OfType<DetectedBoundary>()
            .ToList();
        return Thin(candidates, existingSceneStarts.Concat(dismissedSeconds), options);
    }

    /// <summary>The boundary a black stretch would suggest, or null when it's too short or too
    /// close to either end of the file.</summary>
    public static DetectedBoundary? ToCandidate(FfmpegBlackStretch black, double durationSeconds, SceneDetectionOptions options) =>
        black.EndSeconds - black.StartSeconds >= options.MinBlackSeconds
            // The new scene starts where the picture comes back.
            ? WithinMargins(new DetectedBoundary(black.EndSeconds, SceneSuggestionSource.Black, black.EndSeconds - black.StartSeconds), durationSeconds, options)
            : null;

    /// <summary>The boundary a hard cut would suggest, or null when cuts are off, it scores too
    /// low, or it's too close to either end of the file.</summary>
    public static DetectedBoundary? ToCandidate(FfmpegSceneCut cut, double durationSeconds, SceneDetectionOptions options) =>
        options.MinCutScore is { } minScore && cut.Score >= minScore
            ? WithinMargins(new DetectedBoundary(cut.Seconds, SceneSuggestionSource.Cut, cut.Score), durationSeconds, options)
            : null;

    private static DetectedBoundary? WithinMargins(DetectedBoundary candidate, double durationSeconds, SceneDetectionOptions options) =>
        candidate.Seconds >= options.StartMarginSeconds && candidate.Seconds <= durationSeconds - options.EndMarginSeconds ? candidate : null;

    /// <summary>Drops candidates near a blocked point (an existing scene start or a dismissed
    /// suggestion), then thins the rest to MinSpacingSeconds apart. Candidates are in file order
    /// per source, which breaks ties between equal scores.</summary>
    public static IReadOnlyList<DetectedBoundary> Thin(IEnumerable<DetectedBoundary> candidates, IEnumerable<double> blocked, SceneDetectionOptions options)
    {
        var blockedPoints = blocked.ToList();
        var remaining = candidates.Where(c => !blockedPoints.Any(b => Math.Abs(b - c.Seconds) < options.NearbySeconds));

        // Black breaks first (the strongest signal between JAV scenes), then the highest-scoring cuts;
        // each accepted point claims MinSpacingSeconds either side.
        var accepted = new List<DetectedBoundary>();
        foreach (var candidate in remaining
                     .OrderBy(c => c.Source == SceneSuggestionSource.Black ? 0 : 1)
                     .ThenByDescending(c => c.Score))
        {
            if (accepted.All(a => Math.Abs(a.Seconds - candidate.Seconds) >= options.MinSpacingSeconds))
            {
                accepted.Add(candidate);
            }
        }

        return accepted.OrderBy(a => a.Seconds).ToList();
    }
}
