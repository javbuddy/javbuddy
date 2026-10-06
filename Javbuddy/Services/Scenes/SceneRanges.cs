using Javbuddy.Models;

namespace Javbuddy.Services.Scenes;

/// <summary>A scene with its position (1-based, by start time) and the end it effectively runs
/// to: its own EndSeconds, else the next scene's start, else the movie's duration (null when the
/// last scene has no end and the duration is unknown).</summary>
public sealed record ResolvedScene(Scene Scene, int Position, double? EffectiveEndSeconds)
{
    public string DisplayTitle => SceneRanges.DisplayTitle(Scene.Title, Position);
}

/// <summary>Pure helpers for scene ordering, null-end resolution and validation —
/// no I/O, so MovieSceneService and later chapter export share one definition of a scene's range.</summary>
public static class SceneRanges
{
    public static string DisplayTitle(string? title, int position) =>
        string.IsNullOrWhiteSpace(title) ? $"Scene {position}" : title;

    /// <summary>Orders scenes by start time and fills in each null end from the next scene's start,
    /// or <paramref name="durationSeconds"/> for the last scene.</summary>
    public static IReadOnlyList<ResolvedScene> ResolveEffectiveRanges(IEnumerable<Scene> scenes, double? durationSeconds)
    {
        var ordered = Order(scenes);
        var resolved = new List<ResolvedScene>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var scene = ordered[i];
            var effectiveEnd = scene.EndSeconds
                ?? (i + 1 < ordered.Count ? ordered[i + 1].StartSeconds : durationSeconds);
            resolved.Add(new ResolvedScene(scene, i + 1, effectiveEnd));
        }
        return resolved;
    }

    /// <summary>Validates <paramref name="candidate"/> against the movie's duration and its other
    /// scenes (<paramref name="others"/> must not contain the candidate itself). Returns an error
    /// message, or null when the scene is valid.</summary>
    public static string? Validate(Scene candidate, IEnumerable<Scene> others, double? durationSeconds)
    {
        var start = candidate.StartSeconds;
        var end = candidate.EndSeconds;

        if (!double.IsFinite(start) || (end.HasValue && !double.IsFinite(end.Value)))
        {
            return "Start and end must be valid times.";
        }
        if (start < 0)
        {
            return "Start can't be negative.";
        }
        if (end.HasValue && end.Value <= start)
        {
            return "End must be after start.";
        }
        if (durationSeconds is { } duration)
        {
            if (start >= duration)
            {
                return "Start must be before the end of the movie.";
            }
            if (end.HasValue && end.Value > duration)
            {
                return "End can't be past the end of the movie.";
            }
        }

        var existing = Order(others);
        var ordered = Order(existing.Append(candidate));
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var current = ordered[i];
            var next = ordered[i + 1];
            if (!ReferenceEquals(current, candidate) && !ReferenceEquals(next, candidate))
            {
                continue;
            }
            var overlaps = next.StartSeconds == current.StartSeconds
                || (current.EndSeconds is { } currentEnd && currentEnd > next.StartSeconds);
            if (!overlaps)
            {
                continue;
            }

            // Name the conflicting scene by its position among the existing scenes — what the
            // user currently sees — not the renumbering the candidate would cause.
            var other = ReferenceEquals(current, candidate) ? next : current;
            return $"Overlaps \"{DisplayTitle(other.Title, existing.IndexOf(other) + 1)}\".";
        }

        return null;
    }

    // Id as tiebreaker keeps positions stable for scenes sharing a start (only possible in data
    // that bypassed validation).
    private static List<Scene> Order(IEnumerable<Scene> scenes) =>
        scenes.OrderBy(s => s.StartSeconds).ThenBy(s => s.Id).ToList();
}
