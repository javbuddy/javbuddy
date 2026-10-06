using Javbuddy.Services.Scenes;

namespace Javbuddy.Components.Shared;

/// <summary>Where the players' highlight and apex jump keys land, in the video's time:
/// a highlight's start, or an apex's lead-in before it. Only markers inside
/// [min, max) count (a clip's range; the whole video when null), and a target is kept at or after min.
/// Sorted and distinct; VideoSeekKeys.razor.js picks the next or previous one from the playhead.</summary>
public static class MarkerJumps
{
    public static IReadOnlyList<double> HighlightTargets(IEnumerable<HighlightItem>? highlights, double? minSeconds, double? maxSeconds) =>
        Targets(highlights?.Select(h => (h.StartSeconds, h.StartSeconds)), minSeconds, maxSeconds);

    public static IReadOnlyList<double> ApexTargets(IEnumerable<ApexItem>? apexes, double? minSeconds, double? maxSeconds) =>
        Targets(apexes?.Select(a => (a.Seconds, ApexRanges.StartFor(a))), minSeconds, maxSeconds);

    /// <summary>Each marker is its time, which decides whether it's in range, and where a jump lands.</summary>
    private static IReadOnlyList<double> Targets(IEnumerable<(double Seconds, double Target)>? markers, double? minSeconds, double? maxSeconds)
    {
        var min = minSeconds ?? 0;
        return markers is null
            ? []
            : markers.Where(m => m.Seconds >= min && (maxSeconds is not { } max || m.Seconds < max))
                .Select(m => Math.Max(m.Target, min))
                .Distinct()
                .Order()
                .ToList();
    }
}
