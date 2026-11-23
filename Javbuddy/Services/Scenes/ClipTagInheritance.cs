namespace Javbuddy.Services.Scenes;

/// <summary>A tag a scene, highlight or apex inherits from a parent that passes it down. Sources name the parents
/// ("the movie", "scene 2", "highlight 1"). IsExcluded: the clip opted out, so it doesn't count.</summary>
public sealed record InheritedTag(SceneTagItem Tag, IReadOnlyList<string> Sources, bool IsExcluded);

public sealed record ClipTagInheritanceResult(
    IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> Scenes,
    IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> Highlights,
    IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> Apexes)
{
    public static ClipTagInheritanceResult Empty { get; } = new(
        new Dictionary<int, IReadOnlyList<InheritedTag>>(),
        new Dictionary<int, IReadOnlyList<InheritedTag>>(),
        new Dictionary<int, IReadOnlyList<InheritedTag>>());
}

/// <summary>Pure downward inheritance of tags, opt-in per tag link (PropagatesDown) — the opposite direction to
/// <see cref="ClipRollup"/>. Each source reaches its clips directly, never through another clip:
/// a movie tag every scene, highlight and apex; a scene tag the highlights starting in it and the apexes in it
/// (as <see cref="ClipTree"/> places them); a highlight tag every apex in its [start, end). Only own tags pass down,
/// so an inherited tag goes no further and never rolls back up. A clip's exclusions (ExcludedTagIds) mark a tag
/// it would inherit as excluded, from every source, for that clip only. A clip's own copy of a tag doesn't stop it
/// being inherited too (the editors mark that redundant). ClipActorSync stores the non-excluded ones for the wall.</summary>
public static class ClipTagInheritance
{
    public const string MovieLabel = "the movie";

    public static ClipTagInheritanceResult Compute(
        IReadOnlyList<SceneTagItem> movieTags,
        IReadOnlyList<SceneItem> scenes,
        IReadOnlyList<HighlightItem> highlights,
        IReadOnlyList<ApexItem> apexes)
    {
        var fromMovie = movieTags.Where(t => t.PropagatesDown).ToList();

        SceneItem? SceneAt(double seconds) =>
            scenes.OrderBy(s => s.Position).FirstOrDefault(s => ApexRanges.IsWithin(s.StartSeconds, s.EffectiveEndSeconds, seconds));

        var sceneTags = scenes.ToDictionary(s => s.Id, s =>
        {
            var pool = new Pool();
            pool.Add(MovieLabel, fromMovie);
            return pool.Build(s.ExcludedTagIds);
        });

        var highlightTags = highlights.ToDictionary(h => h.Id, h =>
        {
            var pool = new Pool();
            pool.Add(MovieLabel, fromMovie);
            if (SceneAt(h.StartSeconds) is { } scene) pool.Add($"scene {scene.Position}", Passed(scene.Tags));
            return pool.Build(h.ExcludedTagIds);
        });

        var apexTags = apexes.ToDictionary(a => a.Id, a =>
        {
            var pool = new Pool();
            pool.Add(MovieLabel, fromMovie);
            if (SceneAt(a.Seconds) is { } scene) pool.Add($"scene {scene.Position}", Passed(scene.Tags));
            foreach (var highlight in highlights.Where(h => ClipRollup.ApexInHighlight(h, a)).OrderBy(h => h.Position))
            {
                pool.Add($"highlight {highlight.Position}", Passed(highlight.Tags));
            }
            return pool.Build(a.ExcludedTagIds);
        });

        return new ClipTagInheritanceResult(sceneTags, highlightTags, apexTags);
    }

    private static IEnumerable<SceneTagItem> Passed(IEnumerable<SceneTagItem> own) => own.Where(t => t.PropagatesDown);

    private sealed class Pool
    {
        private readonly Dictionary<int, (SceneTagItem Item, List<string> Sources)> tags = [];

        public void Add(string source, IEnumerable<SceneTagItem> items)
        {
            foreach (var tag in items)
            {
                if (!tags.TryGetValue(tag.TagId, out var entry))
                {
                    tags[tag.TagId] = entry = (tag with { PropagatesDown = false }, []);
                }
                if (!entry.Sources.Contains(source)) entry.Sources.Add(source);
            }
        }

        public IReadOnlyList<InheritedTag> Build(IReadOnlySet<int> excluded) =>
            tags.Values
                .Select(e => new InheritedTag(e.Item, e.Sources, excluded.Contains(e.Item.TagId)))
                .OrderBy(t => t.Tag.ParentName ?? t.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
