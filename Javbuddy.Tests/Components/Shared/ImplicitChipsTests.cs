using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class ImplicitChipsTests : BunitContext
{
    [Fact]
    public void RendersOneReadOnlyChipPerItem_WithItsSourcesAsTheTooltip()
    {
        var chips = ImplicitChip.ForTags(
        [
            new ImplicitTag(new SceneTagItem(1, "Squirt", null), ["highlight 2", "apex 3"]),
            new ImplicitTag(new SceneTagItem(2, "Rough", "Play"), ["apex 1"]),
        ]);

        var cut = Render<ImplicitChips>(p => p.Add(x => x.Items, chips));

        var spans = cut.FindAll(".implicit-chip");
        Assert.Equal(["Squirt", "Play › Rough"], spans.Select(s => s.TextContent));
        Assert.Equal("From highlight 2, apex 3", spans[0].GetAttribute("title"));
        Assert.Equal("Play › Rough, implied", spans[1].GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void RendersNothing_WhenThereAreNoItems()
    {
        var cut = Render<ImplicitChips>(p => p.Add(x => x.Items, []));

        cut.MarkupMatches(string.Empty);
    }

    [Theory]
    [InlineData(ActorSource.Scene, "scene 2", "From scene 2")]
    [InlineData(ActorSource.Highlight, "highlight 1", "From highlight 1")]
    [InlineData(ActorSource.Cast, "the cast", "From the cast")]
    public void ForActors_GivesInheritedActorsTheirParent(ActorSource source, string from, string tooltip)
    {
        var chips = ImplicitChip.ForActors(new EffectiveActors([new SceneActorItem(1, "Aika"), new SceneActorItem(2, "Bea")], source, from));

        Assert.Equal([new ImplicitChip("Aika", tooltip), new ImplicitChip("Bea", tooltip)], chips);
    }

    [Fact]
    public void ForActors_GivesNothing_ForExplicitActors() =>
        Assert.Empty(ImplicitChip.ForActors(new EffectiveActors([new SceneActorItem(1, "Aika")], ActorSource.Explicit, null)));
}
