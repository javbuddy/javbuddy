namespace Javbuddy.Services.Scenes;

/// <summary>A tag a scene or highlight shows only because something inside it carries it.
/// Sources name where it comes from ("highlight 2", "apex 3").</summary>
public sealed record ImplicitTag(SceneTagItem Tag, IReadOnlyList<string> Sources);

/// <summary>Scenes/Highlights hold each owner's implicit tags; RedundantScenes/RedundantHighlights its
/// explicit tags that something inside it carries anyway, with the same sources.</summary>
public sealed record ClipRollupResult(
    IReadOnlyDictionary<int, IReadOnlyList<ImplicitTag>> Scenes,
    IReadOnlyDictionary<int, IReadOnlyList<ImplicitTag>> Highlights,
    IReadOnlyDictionary<int, IReadOnlyList<ImplicitTag>> RedundantScenes,
    IReadOnlyDictionary<int, IReadOnlyList<ImplicitTag>> RedundantHighlights);

/// <summary>Pure roll-up of tags from apexes and highlights to the highlights and scenes around them
/// Computed from times each time, never stored. An apex rolls up into every highlight
/// whose [start, end) holds it and into the scene whose effective range holds it; a highlight into
/// every scene its range overlaps. Only the children's explicit tags count (not transitive), and a tag
/// the parent already holds explicitly isn't implicit but redundant. Actors don't roll up — they flow down
/// (<see cref="ClipActors"/>). ClipRollupQueries mirrors these rules in SQL for the wall.</summary>
public static class ClipRollup
{
    public static ClipRollupResult Compute(IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<ApexItem> apexes)
    {
        var sceneTags = new Dictionary<int, IReadOnlyList<ImplicitTag>>();
        var redundantSceneTags = new Dictionary<int, IReadOnlyList<ImplicitTag>>();
        foreach (var scene in scenes)
        {
            var pool = new Pool();
            foreach (var highlight in highlights.Where(h => HighlightOverlapsScene(scene, h)))
            {
                pool.Add($"highlight {highlight.Position}", highlight.Tags);
            }
            foreach (var apex in apexes.Where(a => ApexRanges.IsWithin(scene.StartSeconds, scene.EffectiveEndSeconds, a.Seconds)))
            {
                pool.Add($"apex {apex.Position}", apex.Tags);
            }
            sceneTags[scene.Id] = pool.Build(scene.Tags);
            redundantSceneTags[scene.Id] = pool.Redundant(scene.Tags);
        }

        var highlightTags = new Dictionary<int, IReadOnlyList<ImplicitTag>>();
        var redundantHighlightTags = new Dictionary<int, IReadOnlyList<ImplicitTag>>();
        foreach (var highlight in highlights)
        {
            var pool = new Pool();
            foreach (var apex in apexes.Where(a => ApexInHighlight(highlight, a)))
            {
                pool.Add($"apex {apex.Position}", apex.Tags);
            }
            highlightTags[highlight.Id] = pool.Build(highlight.Tags);
            redundantHighlightTags[highlight.Id] = pool.Redundant(highlight.Tags);
        }
        return new ClipRollupResult(sceneTags, highlightTags, redundantSceneTags, redundantHighlightTags);
    }

    /// <summary>The highlight's range overlaps the scene's effective range; touching edges don't count.</summary>
    public static bool HighlightOverlapsScene(SceneItem scene, HighlightItem highlight) =>
        highlight.StartSeconds < (scene.EffectiveEndSeconds ?? double.PositiveInfinity) && highlight.EndSeconds > scene.StartSeconds;

    /// <summary>The apex lies in the highlight's [start, end).</summary>
    public static bool ApexInHighlight(HighlightItem highlight, ApexItem apex) =>
        apex.Seconds >= highlight.StartSeconds && apex.Seconds < highlight.EndSeconds;

    private sealed class Pool
    {
        private readonly Dictionary<int, (SceneTagItem Item, List<string> Sources)> tags = [];

        public void Add(string source, IEnumerable<SceneTagItem> items)
        {
            foreach (var tag in items)
            {
                if (!tags.TryGetValue(tag.TagId, out var entry))
                {
                    tags[tag.TagId] = entry = (tag, []);
                }
                entry.Sources.Add(source);
            }
        }

        public IReadOnlyList<ImplicitTag> Build(IReadOnlyList<SceneTagItem> explicitTags) => Select(explicitTags, redundant: false);

        // The parent's explicit tags the pool holds too.
        public IReadOnlyList<ImplicitTag> Redundant(IReadOnlyList<SceneTagItem> explicitTags) => Select(explicitTags, redundant: true);

        private IReadOnlyList<ImplicitTag> Select(IReadOnlyList<SceneTagItem> explicitTags, bool redundant)
        {
            var explicitIds = explicitTags.Select(t => t.TagId).ToHashSet();
            return tags.Where(kv => explicitIds.Contains(kv.Key) == redundant)
                .Select(kv => new ImplicitTag(kv.Value.Item, kv.Value.Sources))
                .OrderBy(t => t.Tag.ParentName ?? t.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
