namespace Javbuddy.Services.Scenes;

/// <summary>Explicit actor tags per level, as stored: actor id → tag ids. Scenes, Highlights and Apexes are keyed by owner id.</summary>
public sealed record ActorTagSets(
    IReadOnlyDictionary<int, IReadOnlySet<int>> Movie,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> Scenes,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> Highlights,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> Apexes)
{
    public static ActorTagSets Empty { get; } = new(
        new Dictionary<int, IReadOnlySet<int>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>());
}

/// <summary>One actor tag a clip has for an actor. <see cref="From"/> is null for the clip's own, "scene 2", "highlight 1" or
/// "the movie" for an inherited one, and for a rolled-up one (<see cref="IsRolledUp"/>) the clips that carry it
/// ("highlight 2, apex 3").</summary>
public sealed record EffectiveActorTag(int ActorId, int TagId, bool IsRolledUp, string? From);

public sealed record ClipActorTagsResult(
    IReadOnlyDictionary<int, IReadOnlyList<EffectiveActorTag>> Scenes,
    IReadOnlyDictionary<int, IReadOnlyList<EffectiveActorTag>> Highlights,
    IReadOnlyDictionary<int, IReadOnlyList<EffectiveActorTag>> Apexes);

/// <summary>Pure rules for actor-scoped tags (Tag.IsActorTag): computed from times each time, never stored apart from
/// ClipActorSync's copy for the wall's filters. Per actor, the nearest level that has any tag of its own for them
/// wins, flowing down like <see cref="ClipActors"/>: a scene has its own, else the movie's; a highlight its own, else
/// the scene's it starts in, else the movie's; an apex its own, else its parent highlight's, else its scene's, else the
/// movie's. Own tags replace inherited ones, so a scene can say "brunette" over the movie's "blonde". Like plain
/// tags (<see cref="ClipRollup"/>), the explicit tags of what's inside roll up, per actor and not transitively: an
/// apex's into the highlights holding it and its scene, a highlight's into the scenes it overlaps.</summary>
public static class ClipActorTags
{
    private const string MovieLabel = "the movie";

    public static ClipActorTagsResult Compute(
        ClipActorsResult effectiveActors,
        IReadOnlyList<SceneItem> scenes,
        IReadOnlyList<HighlightItem> highlights,
        IReadOnlyList<ApexItem> apexes,
        ActorTagSets sets)
    {
        var sceneResult = new Dictionary<int, IReadOnlyList<EffectiveActorTag>>();
        foreach (var scene in scenes)
        {
            var own = Own(sets.Scenes, scene.Id);
            var tags = Resolve(Actors(effectiveActors.Scenes, scene.Id, own), own, actor => FromMovie(sets, actor));
            var pool = new RollupPool();
            foreach (var highlight in highlights.Where(h => ClipRollup.HighlightOverlapsScene(scene, h)))
            {
                pool.Add($"highlight {highlight.Position}", Own(sets.Highlights, highlight.Id));
            }
            foreach (var apex in apexes.Where(a => ApexRanges.IsWithin(scene.StartSeconds, scene.EffectiveEndSeconds, a.Seconds)))
            {
                pool.Add($"apex {apex.Position}", Own(sets.Apexes, apex.Id));
            }
            sceneResult[scene.Id] = tags.Concat(pool.Build(tags)).ToList();
        }

        var highlightResult = new Dictionary<int, IReadOnlyList<EffectiveActorTag>>();
        foreach (var highlight in highlights)
        {
            var own = Own(sets.Highlights, highlight.Id);
            var tags = Resolve(Actors(effectiveActors.Highlights, highlight.Id, own), own, actor => FromScene(scenes, sets, highlight.StartSeconds, actor));
            var pool = new RollupPool();
            foreach (var apex in apexes.Where(a => ClipRollup.ApexInHighlight(highlight, a)))
            {
                pool.Add($"apex {apex.Position}", Own(sets.Apexes, apex.Id));
            }
            highlightResult[highlight.Id] = tags.Concat(pool.Build(tags)).ToList();
        }

        var apexResult = new Dictionary<int, IReadOnlyList<EffectiveActorTag>>();
        foreach (var apex in apexes)
        {
            var own = Own(sets.Apexes, apex.Id);
            var parent = ClipActors.ParentHighlight(highlights, apex.Seconds);
            apexResult[apex.Id] = Resolve(Actors(effectiveActors.Apexes, apex.Id, own), own, actor =>
            {
                if (parent is null) return FromScene(scenes, sets, apex.Seconds, actor);
                var inherited = Own(sets.Highlights, parent.Id).GetValueOrDefault(actor) is { Count: > 0 } highlightOwn
                    ? (Tags: highlightOwn, From: null)
                    : FromScene(scenes, sets, parent.StartSeconds, actor);
                return (inherited.Tags, inherited.From == MovieLabel ? MovieLabel : $"highlight {parent.Position}");
            });
        }
        return new ClipActorTagsResult(sceneResult, highlightResult, apexResult);
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> Own(IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> byOwner, int owner) =>
        byOwner.TryGetValue(owner, out var own) ? own : new Dictionary<int, IReadOnlySet<int>>();

    // The clip's effective actors, plus any actor with tags of its own (narrowing a clip's actors keeps their tags).
    private static IEnumerable<int> Actors(IReadOnlyDictionary<int, EffectiveActors> effective, int owner, IReadOnlyDictionary<int, IReadOnlySet<int>> own) =>
        (effective.TryGetValue(owner, out var actors) ? actors.Actors.Select(a => a.ActorId) : [])
            .Concat(own.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key))
            .Distinct()
            .Order();

    private static (IReadOnlySet<int> Tags, string? From) FromMovie(ActorTagSets sets, int actor) =>
        (sets.Movie.GetValueOrDefault(actor) ?? new HashSet<int>(), MovieLabel);

    // The scene holding the time passes on its own tags for the actor, else the movie's.
    private static (IReadOnlySet<int> Tags, string? From) FromScene(IReadOnlyList<SceneItem> scenes, ActorTagSets sets, double seconds, int actor)
    {
        var scene = scenes.FirstOrDefault(s => ApexRanges.IsWithin(s.StartSeconds, s.EffectiveEndSeconds, seconds));
        return scene is not null && Own(sets.Scenes, scene.Id).GetValueOrDefault(actor) is { Count: > 0 } own
            ? (own, $"scene {scene.Position}")
            : FromMovie(sets, actor);
    }

    private static List<EffectiveActorTag> Resolve(
        IEnumerable<int> actors,
        IReadOnlyDictionary<int, IReadOnlySet<int>> own,
        Func<int, (IReadOnlySet<int> Tags, string? From)> inherited)
    {
        var result = new List<EffectiveActorTag>();
        foreach (var actor in actors)
        {
            var (tags, from) = own.GetValueOrDefault(actor) is { Count: > 0 } explicitTags ? (explicitTags, null) : inherited(actor);
            result.AddRange(tags.Order().Select(tag => new EffectiveActorTag(actor, tag, false, from)));
        }
        return result;
    }

    private sealed class RollupPool
    {
        private readonly SortedDictionary<(int Actor, int Tag), List<string>> sources = [];

        public void Add(string source, IReadOnlyDictionary<int, IReadOnlySet<int>> own)
        {
            foreach (var (actor, tags) in own)
            {
                foreach (var tag in tags)
                {
                    if (!sources.TryGetValue((actor, tag), out var list)) sources[(actor, tag)] = list = [];
                    list.Add(source);
                }
            }
        }

        // Only pairs the parent doesn't already have, own or inherited.
        public IEnumerable<EffectiveActorTag> Build(IReadOnlyList<EffectiveActorTag> has)
        {
            var held = has.Select(t => (t.ActorId, t.TagId)).ToHashSet();
            return sources.Where(kv => !held.Contains(kv.Key))
                .Select(kv => new EffectiveActorTag(kv.Key.Actor, kv.Key.Tag, true, string.Join(", ", kv.Value)));
        }
    }
}
