using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class ClipRollupTests
{
    [Theory]
    [MemberData(nameof(ClipCases.All), MemberType = typeof(ClipCases))]
    public void Compute_RollsTagsUpAsTheCaseExpects(ClipCase clipCase)
    {
        var result = ClipRollup.Compute(clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

        foreach (var scene in clipCase.Scenes)
        {
            Assert.Equal(clipCase.ExpectedSceneImplicitTags.GetValueOrDefault(scene.Id, []), result.Scenes[scene.Id].Select(t => t.Tag.TagId));
        }
        foreach (var highlight in clipCase.Highlights)
        {
            Assert.Equal(clipCase.ExpectedHighlightImplicitTags.GetValueOrDefault(highlight.Id, []), result.Highlights[highlight.Id].Select(t => t.Tag.TagId));
        }
    }

    [Fact]
    public void Sources_NameEveryChildCarryingTheTag_HighlightsBeforeApexes()
    {
        var clipCase = ClipCases.List.Single(c => c.Name == "explicit-not-implicit");

        var result = ClipRollup.Compute(clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

        Assert.Equal(["highlight 1", "apex 1"], result.Scenes[1].Single().Sources);
    }

    [Fact]
    public void AnOwnTagSomethingInsideCarries_IsRedundant_NotImplicit()
    {
        var clipCase = ClipCases.List.Single(c => c.Name == "explicit-not-implicit");

        var result = ClipRollup.Compute(clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

        // Scene 1 holds 7, which highlight 1 carries; highlight 1 holds 8, which apex 1 carries.
        var scene = Assert.Single(result.RedundantScenes[1]);
        Assert.Equal(7, scene.Tag.TagId);
        Assert.Equal(["highlight 1"], scene.Sources);
        var highlight = Assert.Single(result.RedundantHighlights[1]);
        Assert.Equal(8, highlight.Tag.TagId);
        Assert.Equal(["apex 1"], highlight.Sources);
        Assert.DoesNotContain(result.Scenes[1], t => t.Tag.TagId == 7);
    }

    [Fact]
    public void NothingInside_GivesAnEmptyList()
    {
        var scenes = new[] { new SceneItem(1, 1, "Scene 1", null, 0, 10, 10) };

        var result = ClipRollup.Compute(scenes, [], []);

        Assert.Empty(result.Scenes[1]);
    }
}
