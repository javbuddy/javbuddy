using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class SceneBoundaryStreamTests
{
    private static readonly SceneDetectionOptions WithCuts = new() { MinCutScore = 30 };

    /// <summary>Feeds the signals the way ffmpeg reports them — merged in file order, with a time=
    /// stats line every statsEvery seconds — taking what settles along the way, then completes.</summary>
    private static (List<DetectedBoundary> All, List<(double DecodedSeconds, DetectedBoundary Boundary)> Timeline) Stream(
        FfmpegSceneSignals signals, double duration, SceneDetectionOptions options, IReadOnlyList<double>? blocked = null, double statsEvery = 7)
    {
        var stream = new SceneBoundaryStream(duration, options);
        var timeline = new List<(double, DetectedBoundary)>();
        var events = signals.BlackStretches.Select(b => (Seconds: b.EndSeconds, Signal: (object)b))
            .Concat(signals.Cuts.Select(c => (c.Seconds, Signal: (object)c)))
            .Concat(Enumerable.Range(0, (int)(duration / statsEvery) + 1).Select(i => (Seconds: i * statsEvery, Signal: (object)i)))
            .OrderBy(e => e.Seconds)
            .ToList();
        foreach (var (seconds, signal) in events)
        {
            switch (signal)
            {
                case FfmpegBlackStretch black: stream.Add(black); break;
                case FfmpegSceneCut cut: stream.Add(cut); break;
                default: stream.Advance(seconds); break;
            }
            if (stream.HasSettled)
            {
                timeline.AddRange(stream.TakeSettled(blocked ?? []).Select(b => (seconds, b)));
            }
        }
        stream.Complete();
        timeline.AddRange(stream.TakeSettled(blocked ?? []).Select(b => (duration, b)));
        return (timeline.Select(t => t.Item2).OrderBy(b => b.Seconds).ToList(), timeline);
    }

    private static void AssertSameAsWholeFile(FfmpegSceneSignals signals, double duration, SceneDetectionOptions options, IReadOnlyList<double>? blocked = null)
    {
        var whole = SceneBoundaryDetector.Suggest(signals, duration, options, blocked ?? [], []);
        Assert.Equal(whole, Stream(signals, duration, options, blocked).All);
    }

    // The full MIDE-536 run from SceneBoundaryDetectorTests (research).
    private static readonly FfmpegSceneSignals Mide536 = new(
        [
            new(0.067, 0.7006), new(8.475044, 9.910178), new(1152.586189, 1154.955222), new(2917.751356, 2921.755356),
            new(4588.688956, 4592.592856), new(7108.407678, 7118.986289), new(7126.627256, 7127.760989),
        ],
        [new(1500, 34), new(3000, 25)]);

    [Fact]
    public void Mide536_SettlesEachBreak_AMinuteAfterIt_WithTheWholeFileResult()
    {
        var (all, timeline) = Stream(Mide536, 7127.794722, new SceneDetectionOptions());

        Assert.Equal(SceneBoundaryDetector.Suggest(Mide536, 7127.794722, new SceneDetectionOptions(), [], []), all);
        // Each is known about a minute into the decode after it, long before the scan ends.
        Assert.All(timeline, t => Assert.InRange(t.DecodedSeconds - t.Boundary.Seconds, 60, 67));
    }

    [Fact]
    public void ALongerBlackUpToAMinuteLater_StillWins()
    {
        // Settling 601 as soon as it ended would have kept it; the whole-file pass prefers 640.
        var signals = new FfmpegSceneSignals([new(600, 601.2), new(637, 640)], []);

        var (all, _) = Stream(signals, 3600, new SceneDetectionOptions());

        Assert.Equal([640d], all.Select(b => b.Seconds));
        AssertSameAsWholeFile(signals, 3600, new SceneDetectionOptions());
    }

    [Fact]
    public void AChainOfCloseCandidates_IsSettledTogether()
    {
        // 600 (weak) loses to 650 unless 700 (strongest) knocks 650 out first: 700 is 100 s past 600,
        // so 600 can only be decided once the whole chain has been seen.
        var signals = new FfmpegSceneSignals([new(598.8, 600), new(648, 650), new(695, 700)], []);

        var (all, timeline) = Stream(signals, 3600, new SceneDetectionOptions());

        Assert.Equal([600d, 700d], all.Select(b => b.Seconds));
        Assert.All(timeline, t => Assert.True(t.DecodedSeconds >= 760));
        AssertSameAsWholeFile(signals, 3600, new SceneDetectionOptions());
    }

    [Fact]
    public void ACutIsHeld_UntilNoLaterBlackCanSupersedeIt()
    {
        var signals = new FfmpegSceneSignals([new(1040, 1041.5)], [new(1000, 90), new(2000, 50)]);

        var (all, timeline) = Stream(signals, 3600, WithCuts);

        Assert.Equal([(1041.5, SceneSuggestionSource.Black), (2000d, SceneSuggestionSource.Cut)], all.Select(b => (b.Seconds, b.Source)));
        Assert.DoesNotContain(timeline, t => t.Boundary.Seconds == 1000);
        AssertSameAsWholeFile(signals, 3600, WithCuts);
    }

    [Fact]
    public void BlockedPoints_AreAppliedWhenSettling()
    {
        AssertSameAsWholeFile(Mide536, 7127.794722, WithCuts with { MinCutScore = 20 }, blocked: [1152, 3001]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomSignals_GiveTheWholeFileResult(int seed)
    {
        // Dense enough that candidates cluster and compete, with and without cuts.
        var random = new Random(seed);
        var blacks = new List<FfmpegBlackStretch>();
        for (var t = random.NextDouble() * 30; t < 3600; t += random.NextDouble() * 90)
        {
            blacks.Add(new FfmpegBlackStretch(t, t + 0.3 + Math.Round(random.NextDouble() * 3, 1)));
            t += 4;
        }
        var cuts = Enumerable.Range(0, 120)
            .Select(_ => new FfmpegSceneCut(Math.Round(random.NextDouble() * 3600, 2), Math.Round(8 + random.NextDouble() * 60)))
            .OrderBy(c => c.Seconds)
            .ToList();
        var signals = new FfmpegSceneSignals(blacks, cuts);

        AssertSameAsWholeFile(signals, 3600, new SceneDetectionOptions());
        AssertSameAsWholeFile(signals, 3600, WithCuts);
        AssertSameAsWholeFile(signals, 3600, WithCuts, blocked: [cuts[10].Seconds, blacks[5].EndSeconds + 2]);
    }
}
