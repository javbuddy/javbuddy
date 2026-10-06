namespace Javbuddy.Services.Scenes;

/// <summary>The "Mei — Creampie, Squirt" label of an untitled apex or highlight
/// The actor names, then the tag names.</summary>
public static class ActorTagLabel
{
    /// <summary>The label, or null when there are neither actors nor tags.</summary>
    public static string? Format(IEnumerable<string> actorNames, IEnumerable<string> tagNames)
    {
        var parts = new[] { actorNames.ToList(), tagNames.ToList() }
            .Where(names => names.Count > 0)
            .Select(names => string.Join(", ", names))
            .ToList();
        return parts.Count == 0 ? null : string.Join(" — ", parts);
    }

    /// <summary>The actor names a label on the movie's own page names: none in a solo
    /// movie (a cast of at most one), where repeating her name on every clip says nothing.</summary>
    public static IEnumerable<string> ActorsToName(IEnumerable<string> actorNames, int castCount) =>
        castCount <= 1 ? [] : actorNames;
}
