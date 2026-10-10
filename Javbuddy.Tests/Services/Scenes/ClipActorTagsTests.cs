using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>ClipActorTags: per-actor nearest-level-wins inheritance down, and per-actor roll-up of the explicit tags up.</summary>
public sealed class ClipActorTagsTests
{
    private const int Mei = 1, Rin = 2, Blonde = 10, Brunette = 11, Tattoo = 12;

    private static readonly IReadOnlyList<SceneActorItem> Cast = [new(Mei, "Mei"), new(Rin, "Rin")];

    // Scene 1: 0–500, scene 2: 500–1000. Highlight 1: 100–300 (in scene 1), highlight 2: 450–600 (crosses both).
    private static readonly IReadOnlyList<SceneItem> Scenes =
    [
        new(1, 1, "Scene 1", null, 0, 500, 500),
        new(2, 2, "Scene 2", null, 500, null, 1000),
    ];

    private static readonly IReadOnlyList<HighlightItem> Highlights =
    [
        new(1, 1, "Highlight 1", null, 100, 300, false, 0),
        new(2, 2, "Highlight 2", null, 450, 600, false, 1),
    ];

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> Tags(params (int Actor, int[] Tags)[] entries) =>
        entries.ToDictionary(e => e.Actor, e => (IReadOnlySet<int>)e.Tags.ToHashSet());

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> Owners(params (int Owner, IReadOnlyDictionary<int, IReadOnlySet<int>> Tags)[] entries) =>
        entries.ToDictionary(e => e.Owner, e => e.Tags);

    private static ClipActorTagsResult Compute(IReadOnlyList<ApexItem> apexes, ActorTagSets sets) =>
        ClipActorTags.Compute(ClipActors.Compute(Cast, Scenes, Highlights, apexes), Scenes, Highlights, apexes, sets);

    private static ActorTagSets Sets(
        IReadOnlyDictionary<int, IReadOnlySet<int>>? movie = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>? scenes = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>? highlights = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>>? apexes = null) =>
        new(movie ?? Tags(), scenes ?? Owners(), highlights ?? Owners(), apexes ?? Owners());

    private static List<(int Actor, int Tag, bool RolledUp, string? From)> Of(IReadOnlyList<EffectiveActorTag> tags) =>
        tags.Select(t => (t.ActorId, t.TagId, t.IsRolledUp, t.From)).ToList();

    [Fact]
    public void MovieTagsFlowDownToEveryClip_ForTheClipsActors()
    {
        ApexItem[] apexes = [new(1, 1, 150, [])];
        var result = Compute(apexes, Sets(movie: Tags((Mei, [Blonde]))));

        Assert.Equal([(Mei, Blonde, false, "the movie")], Of(result.Scenes[1]));
        Assert.Equal([(Mei, Blonde, false, "the movie")], Of(result.Highlights[1]));
        Assert.Equal([(Mei, Blonde, false, "the movie")], Of(result.Apexes[1]));
    }

    [Fact]
    public void OwnTagsReplaceInheritedOnes_PerActor()
    {
        var result = Compute([], Sets(
            movie: Tags((Mei, [Blonde, Tattoo]), (Rin, [Blonde])),
            scenes: Owners((1, Tags((Mei, [Brunette]))))));

        Assert.Equal(
            [(Mei, Brunette, false, (string?)null), (Rin, Blonde, false, "the movie")],
            Of(result.Scenes[1]));
        Assert.Equal([(Mei, Blonde, false, "the movie"), (Mei, Tattoo, false, "the movie"), (Rin, Blonde, false, "the movie")], Of(result.Scenes[2]));
    }

    [Fact]
    public void HighlightsInheritFromTheSceneTheyStartIn_ApexesFromTheirParentHighlight()
    {
        ApexItem[] apexes = [new(1, 1, 150, []), new(2, 2, 480, []), new(3, 3, 700, [])];
        var result = Compute(apexes, Sets(
            movie: Tags((Mei, [Blonde])),
            scenes: Owners((1, Tags((Mei, [Brunette]))))));

        Assert.Equal([(Mei, Brunette, false, "scene 1")], Of(result.Highlights[1]));
        Assert.Equal([(Mei, Brunette, false, "highlight 1")], Of(result.Apexes[1]));
        // Highlight 2 starts in scene 1 too; apex 2 (480) is in scene 1 but its parent is highlight 2.
        Assert.Equal([(Mei, Brunette, false, "highlight 2")], Of(result.Apexes[2]));
        // Apex 3 is in highlight 2's range? 700 > 600, so no parent: its scene (2) has none of its own, so the movie.
        Assert.Equal([(Mei, Blonde, false, "the movie")], Of(result.Apexes[3]));
    }

    [Fact]
    public void AHighlightThatFellBackToTheMovie_PassesTheMovieOnToItsApex_PerActor()
    {
        // Highlight 2 starts in scene 1, which has tags for Rin only: Mei falls back to the movie through the highlight.
        ApexItem[] apexes = [new(1, 1, 460, [])];
        var result = Compute(apexes, Sets(movie: Tags((Mei, [Blonde])), scenes: Owners((1, Tags((Rin, [Brunette]))))));

        Assert.Equal([(Mei, Blonde, false, "the movie"), (Rin, Brunette, false, "highlight 2")], Of(result.Apexes[1]));
    }

    [Fact]
    public void ExplicitTagsRollUp_PerActor_AndNotTransitively()
    {
        ApexItem[] apexes = [new(1, 1, 150, [])];
        var result = Compute(apexes, Sets(
            highlights: Owners((1, Tags((Rin, [Tattoo])))),
            apexes: Owners((1, Tags((Mei, [Brunette]))))));

        // The apex's tag reaches the highlight holding it and its scene.
        Assert.Contains((Mei, Brunette, true, "apex 1"), Of(result.Highlights[1]));
        Assert.Contains((Mei, Brunette, true, "apex 1"), Of(result.Scenes[1]));
        // The highlight's own tag reaches the scene, but the apex's roll-up into the highlight doesn't go on twice.
        Assert.Contains((Rin, Tattoo, true, "highlight 1"), Of(result.Scenes[1]));
        Assert.DoesNotContain(Of(result.Scenes[2]), t => t.Actor == Mei);
        // Nothing rolls up into an apex.
        Assert.DoesNotContain(result.Apexes[1], t => t.IsRolledUp);
    }

    [Fact]
    public void ARolledUpTagTheClipAlreadyHas_IsNotListedAgain()
    {
        ApexItem[] apexes = [new(1, 1, 150, [])];
        var result = Compute(apexes, Sets(
            scenes: Owners((1, Tags((Mei, [Blonde])))),
            apexes: Owners((1, Tags((Mei, [Blonde]))))));

        Assert.Equal([(Mei, Blonde, false, (string?)null)], Of(result.Scenes[1]));
    }

    [Fact]
    public void ANarrowedClip_KeepsItsOwnTagsForAnActorItNoLongerListsEffectively()
    {
        // Scene 1 narrowed to Rin, but Mei still has a tag of her own on it.
        SceneItem[] withRin = [new(1, 1, "Scene 1", null, 0, 500, 500) { Actors = [new(Rin, "Rin")] }, Scenes[1]];
        var effective = ClipActors.Compute(Cast, withRin, [], []);
        var result = ClipActorTags.Compute(effective, withRin, [], [], Sets(
            movie: Tags((Mei, [Blonde]), (Rin, [Blonde])),
            scenes: Owners((1, Tags((Mei, [Brunette]))))));

        Assert.Equal([(Mei, Brunette, false, (string?)null), (Rin, Blonde, false, "the movie")], Of(result.Scenes[1]));
    }
}
