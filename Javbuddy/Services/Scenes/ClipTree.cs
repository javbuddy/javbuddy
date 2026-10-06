namespace Javbuddy.Services.Scenes;

public sealed record ApexNode(ApexItem Apex, EffectiveActors Actors);

/// <summary>A highlight in the editor's tree. ContinuesIntoScenePositions lists the later scenes it reaches
/// into ("continues into scene 2"), since it is only drawn under the scene it starts in. RedundantTags are
/// its own tags its apexes carry anyway.</summary>
public sealed record HighlightNode(
    HighlightItem Highlight,
    IReadOnlyList<ImplicitTag> ImplicitTags,
    EffectiveActors Actors,
    IReadOnlyList<ApexNode> Apexes,
    IReadOnlyList<int> ContinuesIntoScenePositions)
{
    public IReadOnlyList<ImplicitTag> RedundantTags { get; init; } = [];
}

/// <summary>A scene in the editor's tree, with the highlights starting in it and the apexes in it that no
/// highlight holds. RedundantTags are its own tags its highlights and apexes carry anyway.</summary>
public sealed record SceneNode(
    SceneItem Scene,
    IReadOnlyList<ImplicitTag> ImplicitTags,
    EffectiveActors Actors,
    IReadOnlyList<HighlightNode> Highlights,
    IReadOnlyList<ApexNode> Apexes)
{
    public IReadOnlyList<ImplicitTag> RedundantTags { get; init; } = [];
}

/// <summary>One direct child of a scene (or of the "outside scenes" group): a highlight or an apex.</summary>
public sealed record ClipChild(HighlightNode? Highlight, ApexNode? Apex);

public sealed record ClipTreeModel(
    IReadOnlyList<SceneNode> Scenes,
    IReadOnlyList<HighlightNode> OutsideHighlights,
    IReadOnlyList<ApexNode> OutsideApexes);

/// <summary>Lays a movie's scenes, highlights and apexes out as the nested editor's tree, each
/// drawn exactly once. Containment is computed from times, never stored: a highlight sits under the scene it
/// starts in; an apex under its parent highlight (<see cref="ClipActors.ParentHighlight"/>, the one it inherits
/// actors from), else directly under the scene holding it; what no scene holds goes in the "outside scenes"
/// group. Each node carries its rolled-up tags (<see cref="ClipRollup"/>) and effective actors
/// (<see cref="ClipActors"/>).</summary>
public static class ClipTree
{
    public static ClipTreeModel Build(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<ApexItem> apexes)
    {
        var rollup = ClipRollup.Compute(scenes, highlights, apexes);
        var actors = ClipActors.Compute(cast, scenes, highlights, apexes);
        var orderedScenes = scenes.OrderBy(s => s.Position).ToList();

        SceneItem? SceneAt(double seconds) =>
            orderedScenes.FirstOrDefault(s => ApexRanges.IsWithin(s.StartSeconds, s.EffectiveEndSeconds, seconds));

        var apexesByParent = new Dictionary<int, List<ApexNode>>();
        var apexesByScene = new Dictionary<int, List<ApexNode>>();
        var outsideApexes = new List<ApexNode>();
        foreach (var apex in apexes.OrderBy(a => a.Seconds).ThenBy(a => a.Id))
        {
            var node = new ApexNode(apex, actors.Apexes[apex.Id]);
            if (ClipActors.ParentHighlight(highlights, apex.Seconds) is { } parent)
            {
                Add(apexesByParent, parent.Id, node);
            }
            else if (SceneAt(apex.Seconds) is { } scene)
            {
                Add(apexesByScene, scene.Id, node);
            }
            else
            {
                outsideApexes.Add(node);
            }
        }

        var highlightsByScene = new Dictionary<int, List<HighlightNode>>();
        var outsideHighlights = new List<HighlightNode>();
        foreach (var highlight in highlights.OrderBy(h => h.StartSeconds).ThenBy(h => h.Id))
        {
            var start = SceneAt(highlight.StartSeconds);
            var continuesInto = orderedScenes
                .Where(s => s.Position > (start?.Position ?? 0) && ClipRollup.HighlightOverlapsScene(s, highlight))
                .Select(s => s.Position)
                .ToList();
            var node = new HighlightNode(highlight, rollup.Highlights[highlight.Id], actors.Highlights[highlight.Id],
                apexesByParent.GetValueOrDefault(highlight.Id, []), continuesInto)
            {
                RedundantTags = rollup.RedundantHighlights[highlight.Id],
            };
            if (start is not null)
            {
                Add(highlightsByScene, start.Id, node);
            }
            else
            {
                outsideHighlights.Add(node);
            }
        }

        var sceneNodes = orderedScenes
            .Select(s => new SceneNode(s, rollup.Scenes[s.Id], actors.Scenes[s.Id],
                highlightsByScene.GetValueOrDefault(s.Id, []), apexesByScene.GetValueOrDefault(s.Id, []))
            {
                RedundantTags = rollup.RedundantScenes[s.Id],
            })
            .ToList();
        return new ClipTreeModel(sceneNodes, outsideHighlights, outsideApexes);
    }

    /// <summary>A scene's highlights and apexes interleaved by time: a highlight by its start,
    /// an apex by its moment, a highlight first on a tie.</summary>
    public static IReadOnlyList<ClipChild> InTimeOrder(IReadOnlyList<HighlightNode> highlights, IReadOnlyList<ApexNode> apexes) =>
        highlights.Select(h => (Seconds: h.Highlight.StartSeconds, Kind: 0, Id: h.Highlight.Id, Child: new ClipChild(h, null)))
            .Concat(apexes.Select(a => (Seconds: a.Apex.Seconds, Kind: 1, Id: a.Apex.Id, Child: new ClipChild(null, a))))
            .OrderBy(c => c.Seconds)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Id)
            .Select(c => c.Child)
            .ToList();

    private static void Add<T>(Dictionary<int, List<T>> map, int key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
    }
}
