using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>ClipTagInheritance: opt-in downward tags, each source reaching its clips directly.</summary>
public class ClipTagInheritanceTests
{
    private static SceneTagItem Tag(int id, bool passes = true) => new(id, $"tag {id}", null) { PropagatesDown = passes };

    // Scene 1 0–100, scene 2 100–200 (ids 1, 2).
    private static SceneItem Scene(int id, int position, double start, double end, params SceneTagItem[] tags) =>
        new(id, position, "", null, start, end, end) { Tags = tags };

    private static HighlightItem Highlight(int id, int position, double start, double end, params SceneTagItem[] tags) =>
        new(id, position, "", null, start, end, false, 0) { Tags = tags };

    private static ApexItem Apex(int id, int position, double seconds) => new(id, position, seconds, []);

    private static IEnumerable<int> Ids(IReadOnlyList<InheritedTag> tags) => tags.Where(t => !t.IsExcluded).Select(t => t.Tag.TagId);

    [Fact]
    public void MovieTag_ReachesEveryClip_WithTheMovieAsSource()
    {
        var result = ClipTagInheritance.Compute([Tag(9)],
            [Scene(1, 1, 0, 100)], [Highlight(10, 1, 20, 30)], [Apex(20, 1, 250)]);

        Assert.Equal([9], Ids(result.Scenes[1]));
        Assert.Equal([9], Ids(result.Highlights[10]));
        // Outside every scene and highlight, still the movie's.
        Assert.Equal([9], Ids(result.Apexes[20]));
        Assert.Equal([ClipTagInheritance.MovieLabel], result.Apexes[20].Single().Sources);
    }

    [Fact]
    public void TagsWithoutTheToggle_DoNotPassDown()
    {
        var result = ClipTagInheritance.Compute([Tag(9, passes: false)],
            [Scene(1, 1, 0, 100, Tag(5, passes: false))], [Highlight(10, 1, 20, 30, Tag(6, passes: false))], [Apex(20, 1, 25)]);

        Assert.Empty(result.Scenes[1]);
        Assert.Empty(result.Highlights[10]);
        Assert.Empty(result.Apexes[20]);
    }

    [Fact]
    public void SceneTag_ReachesHighlightsStartingInIt_NotOnesOnlyOverlappingIt()
    {
        var result = ClipTagInheritance.Compute([],
            [Scene(1, 1, 0, 100, Tag(5)), Scene(2, 2, 100, 200, Tag(6))],
            [Highlight(10, 1, 90, 150)], []);

        Assert.Equal([5], Ids(result.Highlights[10]));
        Assert.Equal(["scene 1"], result.Highlights[10].Single().Sources);
    }

    [Fact]
    public void SceneTag_ReachesApexesInItsRange()
    {
        var result = ClipTagInheritance.Compute([],
            [Scene(1, 1, 0, 100, Tag(5)), Scene(2, 2, 100, 200, Tag(6))], [], [Apex(20, 1, 50), Apex(21, 2, 150)]);

        Assert.Equal([5], Ids(result.Apexes[20]));
        Assert.Equal([6], Ids(result.Apexes[21]));
    }

    [Fact]
    public void HighlightTag_ReachesApexesInEveryHighlightHoldingThem()
    {
        var result = ClipTagInheritance.Compute([], [],
            [Highlight(10, 1, 0, 60, Tag(7)), Highlight(11, 2, 40, 90, Tag(8))],
            [Apex(20, 1, 50), Apex(21, 2, 70), Apex(22, 3, 60)]);

        Assert.Equal([7, 8], Ids(result.Apexes[20]));
        Assert.Equal([8], Ids(result.Apexes[21]));
        // [start, end): 60 is past highlight 1.
        Assert.Equal([8], Ids(result.Apexes[22]));
    }

    [Fact]
    public void InheritedTags_GoNoFurther()
    {
        // The highlight starts in scene 1 and inherits its tag, but the apex lies in scene 2: the scene 1 tag
        // reaches the highlight only, never the apex through it.
        var result = ClipTagInheritance.Compute([],
            [Scene(1, 1, 0, 100, Tag(5)), Scene(2, 2, 100, 200)],
            [Highlight(10, 1, 90, 150)], [Apex(20, 1, 120)]);

        Assert.Equal([5], Ids(result.Highlights[10]));
        Assert.Empty(result.Apexes[20]);
    }

    [Fact]
    public void ATagFromSeveralSources_IsListedOnce_WithEverySource()
    {
        var result = ClipTagInheritance.Compute([Tag(5)],
            [Scene(1, 1, 0, 100, Tag(5))], [Highlight(10, 1, 20, 30, Tag(5))], [Apex(20, 1, 25)]);

        var tag = Assert.Single(result.Apexes[20]);
        Assert.Equal([ClipTagInheritance.MovieLabel, "scene 1", "highlight 1"], tag.Sources);
        Assert.False(tag.Tag.PropagatesDown);
    }

    [Fact]
    public void AnExclusion_MarksTheTagExcluded_ForThatClipOnly()
    {
        var highlight = Highlight(10, 1, 20, 60) with { ExcludedTagIds = new HashSet<int> { 5 } };
        var result = ClipTagInheritance.Compute([],
            [Scene(1, 1, 0, 100, Tag(5))], [highlight], [Apex(20, 1, 30)]);

        Assert.True(Assert.Single(result.Highlights[10]).IsExcluded);
        // The apex inside the highlight still gets it straight from the scene.
        Assert.Equal([5], Ids(result.Apexes[20]));
    }

    [Fact]
    public void AnOwnCopy_DoesNotStopTheTagBeingInherited()
    {
        var result = ClipTagInheritance.Compute([],
            [Scene(1, 1, 0, 100, Tag(5))], [Highlight(10, 1, 20, 30, Tag(5, passes: false))], []);

        Assert.Equal([5], Ids(result.Highlights[10]));
    }

    [Fact]
    public void ClipTree_ATagBothInheritedAndRolledUp_ShowsOnceAsInherited()
    {
        var apex = new ApexItem(20, 1, 50, [Tag(5, passes: false)]);
        var tree = ClipTree.Build([], [Scene(1, 1, 0, 100)], [], [apex], movieTags: [Tag(5)]);

        var scene = tree.Scenes.Single();
        Assert.Empty(scene.ImplicitTags);
        Assert.Equal([5], Ids(scene.InheritedTags));
    }
}
