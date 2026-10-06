using Javbuddy.Models;

namespace Javbuddy.Services.Scenes;

/// <summary>Pure helpers for highlight ordering, validation and timeline lanes — no
/// I/O. Unlike SceneRanges there's no overlap rule: highlights may overlap freely.</summary>
public static class HighlightRanges
{
    /// <summary>The title, else the actor and tag names ("Mei — Creampie"), else "Highlight N".</summary>
    public static string DisplayTitle(string? title, int position, IEnumerable<string>? actorNames = null, IEnumerable<string>? tagNames = null) =>
        !string.IsNullOrWhiteSpace(title) ? title
            : ActorTagLabel.Format(actorNames ?? [], tagNames ?? []) ?? $"Highlight {position}";

    /// <summary>Returns an error message, or null when the range is valid: finite,
    /// <c>0 ≤ start &lt; end</c>, and <c>end ≤ duration</c> when the duration is known.</summary>
    public static string? Validate(double startSeconds, double endSeconds, double? durationSeconds)
    {
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds))
        {
            return "Start and end must be valid times.";
        }
        if (startSeconds < 0)
        {
            return "Start can't be negative.";
        }
        if (endSeconds <= startSeconds)
        {
            return "End must be after start.";
        }
        if (durationSeconds is { } duration && endSeconds > duration)
        {
            return "End can't be past the end of the movie.";
        }
        return null;
    }

    /// <summary>How many highlights start inside each scene, keyed by scene id; scenes
    /// with none are left out. See the start-time overload for how a start maps to a scene.</summary>
    public static IReadOnlyDictionary<int, int> CountStartsByScene(IReadOnlyList<SceneItem> scenes, IEnumerable<HighlightItem> highlights) =>
        CountStartsByScene(scenes, highlights.Select(h => h.StartSeconds));

    /// <summary>How many of the given start times (a highlight's start, an apex's time —)
    /// fall inside each scene, keyed by scene id; scenes with none are left out. A scene covers
    /// [start, effective end), or runs on without an end when that's unknown, so each time counts for
    /// one scene at most and one in a gap between scenes counts for none.</summary>
    public static IReadOnlyDictionary<int, int> CountStartsByScene(IReadOnlyList<SceneItem> scenes, IEnumerable<double> startSeconds)
    {
        var counts = new Dictionary<int, int>();
        foreach (var start in startSeconds)
        {
            var scene = scenes.LastOrDefault(s => s.StartSeconds <= start);
            if (scene is null || scene.EffectiveEndSeconds <= start) continue;
            counts[scene.Id] = counts.GetValueOrDefault(scene.Id) + 1;
        }
        return counts;
    }

    /// <summary>The highlights a clip of [start, end) overlaps, for the clip player's track
    /// Shifted so the clip starts at 0, cut to the clip, and given lanes of their
    /// own so a highlight isn't left in a lane that only a highlight outside the clip needed.
    /// Expects them in start order, as GetHighlightsAsync returns them.</summary>
    public static IReadOnlyList<HighlightItem> WithinClip(IReadOnlyList<HighlightItem> highlights, double startSeconds, double endSeconds)
    {
        var length = endSeconds - startSeconds;
        var shifted = highlights
            .Where(h => h.StartSeconds < endSeconds && h.EndSeconds > startSeconds)
            .Select(h => h with
            {
                StartSeconds = Math.Max(h.StartSeconds - startSeconds, 0),
                EndSeconds = Math.Min(h.EndSeconds - startSeconds, length),
            })
            .ToList();
        var lanes = AssignLanes(shifted.Select(h => (h.StartSeconds, h.EndSeconds)).ToList());
        return shifted.Select((h, i) => h with { Lane = lanes[i] }).ToList();
    }

    // Id as tiebreaker keeps positions stable for highlights sharing a start.
    public static List<MovieHighlight> Order(IEnumerable<MovieHighlight> highlights) =>
        highlights.OrderBy(h => h.StartSeconds).ThenBy(h => h.Id).ToList();

    /// <summary>Assigns each range (in start order) the lowest timeline lane it fits in without
    /// overlapping another range in that lane, so overlapping highlights stack instead of hiding
    /// each other. Ranges that only touch (one ends where the next starts) share a lane.</summary>
    public static IReadOnlyList<int> AssignLanes(IReadOnlyList<(double Start, double End)> ordered)
    {
        var laneEnds = new List<double>();
        var lanes = new int[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            var lane = laneEnds.FindIndex(end => end <= ordered[i].Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(ordered[i].End);
            }
            else
            {
                laneEnds[lane] = ordered[i].End;
            }
            lanes[i] = lane;
        }
        return lanes;
    }
}
