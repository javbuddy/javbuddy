using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class ClipTreeTests
{
    private static ClipTreeModel Build(ClipCase clipCase) =>
        ClipTree.Build(clipCase.CastItems, clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

    private static ClipCase Case(string name) => ClipCases.List.Single(c => c.Name == name);

    private static int[] TagIds(IReadOnlyList<ImplicitTag> tags) => tags.Select(t => t.Tag.TagId).ToArray();

    private static string Key(EffectiveActors actors) =>
        $"{actors.Source}/{actors.From}/{string.Join(",", actors.Actors.Select(a => a.ActorId))}";

    [Fact]
    public void ApexInAHighlight_SitsUnderTheHighlight_InsideItsScene()
    {
        var tree = Build(Case("apex-in-highlight-in-scene"));

        var scene = Assert.Single(tree.Scenes);
        var highlight = Assert.Single(scene.Highlights);
        Assert.Equal(1, Assert.Single(highlight.Apexes).Apex.Id);
        Assert.Empty(scene.Apexes);
        Assert.Empty(tree.OutsideHighlights);
        Assert.Empty(tree.OutsideApexes);
    }

    [Fact]
    public void ApexInASceneButNoHighlight_SitsDirectlyUnderTheScene()
    {
        var tree = Build(Case("apex-on-boundary"));

        Assert.Empty(tree.Scenes[0].Apexes);
        Assert.Equal(1, Assert.Single(tree.Scenes[1].Apexes).Apex.Id);
    }

    [Fact]
    public void ApexInOverlappingHighlights_SitsUnderTheEarlierStartingOneOnly()
    {
        var tree = Build(Case("overlapping-highlights"));

        Assert.Equal([1, 2], tree.OutsideHighlights.Select(h => h.Highlight.Id));
        Assert.Single(tree.OutsideHighlights[0].Apexes);
        Assert.Empty(tree.OutsideHighlights[1].Apexes);
    }

    [Fact]
    public void HighlightStartingInAGap_IsOutsideScenes_AndKeepsItsApexes()
    {
        var tree = Build(Case("apex-in-gap"));

        Assert.All(tree.Scenes, s => Assert.Empty(s.Highlights));
        var outside = Assert.Single(tree.OutsideHighlights);
        Assert.Single(outside.Apexes);
        Assert.Empty(tree.OutsideApexes);
    }

    [Fact]
    public void SceneCrossingHighlight_SitsUnderItsStartScene_AndSaysWhichScenesItContinuesInto()
    {
        var tree = Build(Case("highlight-crosses-boundary"));

        var highlight = Assert.Single(tree.Scenes[0].Highlights);
        Assert.Equal([2], highlight.ContinuesIntoScenePositions);
        Assert.Empty(tree.Scenes[1].Highlights);
    }

    [Fact]
    public void ApexInAGapWithoutAHighlight_IsOutsideScenes()
    {
        var tree = Build(Case("apex-in-gap-no-highlight"));

        Assert.Equal(1, Assert.Single(tree.OutsideApexes).Apex.Id);
    }

    [Theory]
    [MemberData(nameof(ClipCases.All), MemberType = typeof(ClipCases))]
    public void EveryClipAppearsOnce_WithTheRolledUpTagsAndInheritedActorsOfTheHelpers(ClipCase clipCase)
    {
        var tree = Build(clipCase);
        var rollup = ClipRollup.Compute(clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);
        var actors = ClipActors.Compute(clipCase.CastItems, clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

        var highlightNodes = tree.Scenes.SelectMany(s => s.Highlights).Concat(tree.OutsideHighlights).ToList();
        var apexNodes = tree.Scenes.SelectMany(s => s.Apexes).Concat(highlightNodes.SelectMany(h => h.Apexes)).Concat(tree.OutsideApexes).ToList();
        Assert.Equal(clipCase.Scenes.Select(s => s.Id).Order(), tree.Scenes.Select(s => s.Scene.Id).Order());
        Assert.Equal(clipCase.Highlights.Select(h => h.Id).Order(), highlightNodes.Select(h => h.Highlight.Id).Order());
        Assert.Equal(clipCase.Apexes.Select(a => a.Id).Order(), apexNodes.Select(a => a.Apex.Id).Order());

        foreach (var node in tree.Scenes)
        {
            Assert.Equal(TagIds(rollup.Scenes[node.Scene.Id]), TagIds(node.ImplicitTags));
            Assert.Equal(Key(actors.Scenes[node.Scene.Id]), Key(node.Actors));
        }
        foreach (var node in highlightNodes)
        {
            Assert.Equal(TagIds(rollup.Highlights[node.Highlight.Id]), TagIds(node.ImplicitTags));
            Assert.Equal(Key(actors.Highlights[node.Highlight.Id]), Key(node.Actors));
        }
        foreach (var node in apexNodes)
        {
            Assert.Equal(Key(actors.Apexes[node.Apex.Id]), Key(node.Actors));
        }
    }

    [Fact]
    public void InTimeOrder_InterleavesHighlightsAndApexes_HighlightFirstOnATie()
    {
        HighlightNode Highlight(int id, double start) =>
            new(new HighlightItem(id, id, "Highlight " + id, null, start, start + 30, false, 0), [], EffectiveActors.None, [], []);
        ApexNode Apex(int id, double seconds) => new(new ApexItem(id, id, seconds, []), EffectiveActors.None);

        var order = ClipTree.InTimeOrder([Highlight(1, 10), Highlight(2, 50)], [Apex(1, 5), Apex(2, 50), Apex(3, 20)]);

        Assert.Equal(["a1", "h1", "a3", "h2", "a2"],
            order.Select(c => c.Highlight is { } h ? "h" + h.Highlight.Id : "a" + c.Apex!.Apex.Id));
    }
}
