using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class ClipActorsTests
{
    private static ClipActorsResult Compute(ClipCase clipCase) =>
        ClipActors.Compute(clipCase.CastItems, clipCase.SceneItems, clipCase.HighlightItems, clipCase.ApexItems);

    private static ClipCase Case(string name) => ClipCases.List.Single(c => c.Name == name);

    [Theory]
    [MemberData(nameof(ClipCases.All), MemberType = typeof(ClipCases))]
    public void Compute_FlowsActorsDownAsTheCaseExpects(ClipCase clipCase)
    {
        var result = Compute(clipCase);

        foreach (var scene in clipCase.Scenes)
        {
            Assert.Equal(clipCase.ExpectedSceneActors.GetValueOrDefault(scene.Id, []), result.Scenes[scene.Id].Actors.Select(a => a.ActorId));
        }
        foreach (var highlight in clipCase.Highlights)
        {
            Assert.Equal(clipCase.ExpectedHighlightActors.GetValueOrDefault(highlight.Id, []), result.Highlights[highlight.Id].Actors.Select(a => a.ActorId));
        }
        foreach (var apex in clipCase.Apexes)
        {
            Assert.Equal(clipCase.ExpectedApexActors.GetValueOrDefault(apex.Id, []), result.Apexes[apex.Id].Actors.Select(a => a.ActorId));
        }
    }

    [Fact]
    public void HighlightWithoutActors_InheritsFromTheSceneItStartsIn()
    {
        var actors = Compute(Case("highlight-crosses-boundary")).Highlights[1];

        Assert.Equal(ActorSource.Scene, actors.Source);
        Assert.Equal("scene 1", actors.From);
        Assert.True(actors.IsInherited);
    }

    [Fact]
    public void ApexWithoutActors_InheritsFromItsParentHighlight()
    {
        var actors = Compute(Case("apex-in-gap")).Apexes[1];

        Assert.Equal(ActorSource.Highlight, actors.Source);
        Assert.Equal("highlight 1", actors.From);
    }

    [Fact]
    public void OutsideEveryScene_FallsBackToTheCast()
    {
        var actors = Compute(Case("apex-in-gap-no-highlight")).Apexes[1];

        Assert.Equal(ActorSource.Cast, actors.Source);
        Assert.Equal("the cast", actors.From);
    }

    [Fact]
    public void SceneWithoutActors_InheritsTheCast()
    {
        var actors = Compute(Case("scene-without-actors")).Scenes[1];

        Assert.Equal(ActorSource.Cast, actors.Source);
        Assert.Equal("the cast", actors.From);
        Assert.Equal(actors.Actors, ClipActors.ForNewScene(Case("scene-without-actors").CastItems).Actors);
    }

    [Fact]
    public void ExplicitActors_AreNotInherited()
    {
        var actors = Compute(Case("narrowed-apex")).Apexes[1];

        Assert.Equal(ActorSource.Explicit, actors.Source);
        Assert.Null(actors.From);
        Assert.False(actors.IsInherited);
    }

    [Fact]
    public void ParentHighlight_IsTheEarliestStarting_TiesById()
    {
        IReadOnlyList<HighlightItem> highlights =
        [
            new(2, 2, "H2", null, 10, 50, false, 0),
            new(1, 1, "H1", null, 10, 40, false, 1),
            new(3, 3, "H3", null, 0, 5, false, 0),
        ];

        Assert.Equal(1, ClipActors.ParentHighlight(highlights, 20)?.Id);
        Assert.Null(ClipActors.ParentHighlight(highlights, 60));
    }

    [Fact]
    public void ForNewApex_MatchesWhatComputeGivesAnApexAtThatTime()
    {
        var clipCase = Case("apex-follows-crossing-parent-highlight");

        var forNew = ClipActors.ForNewApex(clipCase.CastItems, clipCase.SceneItems, clipCase.HighlightItems, 120);

        var computed = Compute(clipCase).Apexes[1];
        Assert.Equal(computed.Actors, forNew.Actors);
        Assert.Equal((computed.Source, computed.From), (forNew.Source, forNew.From));
    }

    [Fact]
    public void ForNewHighlight_InheritsFromTheSceneAtItsStart()
    {
        var clipCase = Case("highlight-crosses-boundary");

        var forNew = ClipActors.ForNewHighlight(clipCase.CastItems, clipCase.SceneItems, 150);

        Assert.Equal([2], forNew.Actors.Select(a => a.ActorId));
        Assert.Equal("scene 2", forNew.From);
    }
}
