using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.Scenes;

/// <summary>SceneBoundaryDetector fed signal by signal while ffmpeg is still decoding, so suggestions
/// can be shown before the scan finishes. Thinning is greedy by priority, not by time:
/// a longer black (or a black over a cut) up to MinSpacingSeconds later still wins, and through a
/// chain of such points a decision can depend on candidates further away. So candidates are held in
/// a cluster — points linked by gaps under MinSpacingSeconds — until the decode is MinSpacingSeconds
/// past its last point: no later signal can reach it then, and thinning settled clusters on their own
/// gives the same answer as thinning the whole file at once. Pure, so that equivalence is testable.
/// Signals must arrive in file order, as ffmpeg reports them.</summary>
public sealed class SceneBoundaryStream(double durationSeconds, SceneDetectionOptions options)
{
    private readonly List<DetectedBoundary> settled = [];
    private readonly List<DetectedBoundary> held = [];
    private double heldUntil;

    /// <summary>Whether settled candidates are waiting for TakeSettled.</summary>
    public bool HasSettled => settled.Count > 0;

    public void Add(FfmpegBlackStretch black) => Add(SceneBoundaryDetector.ToCandidate(black, durationSeconds, options));

    public void Add(FfmpegSceneCut cut) => Add(SceneBoundaryDetector.ToCandidate(cut, durationSeconds, options));

    private void Add(DetectedBoundary? candidate)
    {
        if (candidate is null) return;
        if (held.Count > 0 && candidate.Seconds - heldUntil >= options.MinSpacingSeconds)
        {
            SettleHeld();
        }
        held.Add(candidate);
        heldUntil = Math.Max(heldUntil, candidate.Seconds);
    }

    /// <summary>The decode has reached decodedSeconds: settles the held cluster once nothing still
    /// to come can be within MinSpacingSeconds of it.</summary>
    public void Advance(double decodedSeconds)
    {
        if (held.Count > 0 && decodedSeconds - heldUntil >= options.MinSpacingSeconds)
        {
            SettleHeld();
        }
    }

    /// <summary>The decode finished: everything held is settled.</summary>
    public void Complete() => SettleHeld();

    /// <summary>Thins and hands out the settled candidates, against the blocked points (scene starts
    /// and dismissed suggestions — read fresh by the caller, since they change mid-scan).</summary>
    public IReadOnlyList<DetectedBoundary> TakeSettled(IEnumerable<double> blocked)
    {
        var result = SceneBoundaryDetector.Thin(settled, blocked, options);
        settled.Clear();
        return result;
    }

    private void SettleHeld()
    {
        settled.AddRange(held);
        held.Clear();
        heldUntil = 0;
    }
}
