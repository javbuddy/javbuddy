using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class InheritedTagChipsTests : BunitContext
{
    private static readonly InheritedTag Outdoor = new(new SceneTagItem(1, "Outdoor", null), ["scene 2"], IsExcluded: false);
    private static readonly InheritedTag Night = new(new SceneTagItem(2, "Night", null), ["the movie", "highlight 1"], IsExcluded: true);
    private static readonly InheritedTag Own = new(new SceneTagItem(3, "Own", null), ["scene 2"], IsExcluded: false);

    [Fact]
    public void ReadOnly_ShowsInheritedTagsMarkedAsSuch_HidingExcludedAndOwnOnes()
    {
        var cut = Render<InheritedTagChips>(p => p.Add(x => x.Items, [Outdoor, Night, Own]).Add(x => x.OwnTagIds, new HashSet<int> { 3 }));

        var chip = Assert.Single(cut.FindAll(".inherited-chip"));
        Assert.Equal("↳Outdoor, inherited from scene 2", chip.TextContent.Trim());
        Assert.Equal(", inherited from scene 2", chip.QuerySelector(".visually-hidden")!.TextContent);
        Assert.Equal("Inherited from scene 2, not added on this clip", chip.GetAttribute("title"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Editable_OffersToExclude_AndToRestoreAnExcludedOne()
    {
        var toggled = new List<InheritedTag>();
        var cut = Render<InheritedTagChips>(p => p
            .Add(x => x.Items, [Outdoor, Night])
            .Add(x => x.OnToggleExcluded, (InheritedTag tag) => toggled.Add(tag)));

        Assert.Equal("Night", cut.Find(".inherited-chip-excluded s").TextContent);
        cut.Find("button[aria-label='Don\\'t inherit Outdoor here']").Click();
        cut.Find("button[aria-label='Inherit Night again']").Click();

        Assert.Equal([Outdoor, Night], toggled);
    }

    [Fact]
    public void Editable_AnExcludedTagTheClipAlsoOwns_StaysVisibleToRestore()
    {
        var excludedOwn = Own with { IsExcluded = true };
        var cut = Render<InheritedTagChips>(p => p
            .Add(x => x.Items, [excludedOwn])
            .Add(x => x.OwnTagIds, new HashSet<int> { 3 })
            .Add(x => x.OnToggleExcluded, (InheritedTag _) => { }));

        Assert.NotNull(cut.Find("button[aria-label='Inherit Own again']"));
    }
}
