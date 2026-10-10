using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;

namespace Javbuddy.Tests.Components.Shared;

public class ActorTagHoverTests : BunitContext
{
    private static readonly IReadOnlyList<ActorTagListItem> Library = [new(1, "Blonde", 0)];

    [Fact]
    public void Host_IsDescribedByItsPopover()
    {
        var cut = Render<ActorTagHover>(p => p
            .Add(c => c.Tags, [new EffectiveActorTag(5, 1, false, null)])
            .Add(c => c.Library, Library)
            .AddChildContent("Mei"));

        var popover = cut.Find(".actor-hover-popover");
        Assert.False(string.IsNullOrEmpty(popover.Id));
        Assert.Equal(popover.Id, cut.Find(".actor-hover").GetAttribute("aria-describedby"));
        Assert.Contains("Blonde", popover.TextContent);
    }

    [Fact]
    public void EachInstance_HasItsOwnPopoverId()
    {
        var first = Render<ActorTagHover>(p => p.Add(c => c.InheritedFrom, "scene 1").AddChildContent("Mei"));
        var second = Render<ActorTagHover>(p => p.Add(c => c.InheritedFrom, "scene 2").AddChildContent("Rin"));

        Assert.NotEqual(first.Find(".actor-hover-popover").Id, second.Find(".actor-hover-popover").Id);
    }

    [Fact]
    public void WithoutTagsOrInheritance_RendersJustTheContent()
    {
        var cut = Render<ActorTagHover>(p => p.AddChildContent("Mei"));

        Assert.Empty(cut.FindAll(".actor-hover"));
        Assert.Equal("Mei", cut.Markup.Trim());
    }
}
