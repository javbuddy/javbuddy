using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>Highlight and apex rows show actor names the way scene rows do: each name wrapped in
/// ActorTagHover's popover of that actor's tags on the clip.</summary>
public class ClipRowActorNamesTests : BunitContext
{
    private static readonly IReadOnlyList<ActorTagListItem> Library = [new(1, "Blonde", 0)];
    private static readonly IReadOnlyList<EffectiveActorTag> Tags = [new(5, 1, false, null)];
    private static readonly SceneActorItem[] Actors = [new(5, "Mei"), new(6, "Rin")];

    [Fact]
    public void HighlightRow_ShowsActorNamesWithTagHovers()
    {
        var highlight = new HighlightItem(1, 1, "Highlight 1", null, 10, 20, false, 0) { Actors = Actors };
        var cut = Render<ClipHighlightRow>(p => p
            .Add(x => x.Node, new HighlightNode(highlight, [], EffectiveActors.None, [], []))
            .Add(x => x.ActorTags, Tags)
            .Add(x => x.ActorTagLibrary, Library));

        var hovers = cut.FindAll(".highlight-editor-actors .actor-hover");
        Assert.Single(hovers);
        Assert.Contains("Mei", hovers[0].TextContent);
        Assert.Contains("Blonde", cut.Find(".highlight-editor-actors .actor-hover-popover").TextContent);
        Assert.Contains("Rin", cut.Find(".highlight-editor-actors").TextContent);
    }

    [Fact]
    public void HighlightRow_ActorNames_OverrideTheRowsSeekTitle()
    {
        var highlight = new HighlightItem(1, 1, "Highlight 1", null, 10, 20, false, 0) { Actors = Actors };
        var cut = Render<ClipHighlightRow>(p => p
            .Add(x => x.Node, new HighlightNode(highlight, [], EffectiveActors.None, [], []))
            .Add(x => x.ActorTags, Tags)
            .Add(x => x.ActorTagLibrary, Library));

        Assert.Equal("", cut.Find(".highlight-editor-actors").GetAttribute("title"));
        Assert.Equal("", cut.Find(".highlight-editor-actors .actor-hover").GetAttribute("title"));
    }

    [Fact]
    public void ApexRow_ActorNames_OverrideTheRowsSeekTitle()
    {
        var apex = new ApexItem(1, 1, 100, []) { Actors = Actors };
        var cut = Render<ClipApexRow>(p => p
            .Add(x => x.Node, new ApexNode(apex, EffectiveActors.None))
            .Add(x => x.ActorTags, Tags)
            .Add(x => x.ActorTagLibrary, Library));

        Assert.Equal("", cut.Find(".apex-editor-actors").GetAttribute("title"));
        Assert.Equal("", cut.Find(".apex-editor-actors .actor-hover").GetAttribute("title"));
    }

    [Fact]
    public void ApexRow_ShowsActorNamesWithTagHovers()
    {
        var apex = new ApexItem(1, 1, 100, []) { Actors = Actors };
        var cut = Render<ClipApexRow>(p => p
            .Add(x => x.Node, new ApexNode(apex, EffectiveActors.None))
            .Add(x => x.ActorTags, Tags)
            .Add(x => x.ActorTagLibrary, Library));

        var hovers = cut.FindAll(".apex-editor-actors .actor-hover");
        Assert.Single(hovers);
        Assert.Contains("Mei", hovers[0].TextContent);
        Assert.Contains("Blonde", cut.Find(".apex-editor-actors .actor-hover-popover").TextContent);
    }

    [Fact]
    public void ApexRow_InheritedActors_GetTheTagPopoverToo()
    {
        var apex = new ApexItem(1, 1, 100, []);
        var inherited = new EffectiveActors([new SceneActorItem(5, "Mei")], ActorSource.Scene, "scene 2");
        var cut = Render<ClipApexRow>(p => p
            .Add(x => x.Node, new ApexNode(apex, inherited))
            .Add(x => x.ActorTags, Tags)
            .Add(x => x.ActorTagLibrary, Library));

        var popover = cut.Find(".implicit-chips .actor-hover-popover");
        Assert.Contains("From scene 2", popover.TextContent);
        Assert.Contains("Blonde", popover.TextContent);
    }

    [Fact]
    public void Rows_WithoutActors_RenderNoActorSpan()
    {
        var apex = new ApexItem(1, 1, 100, []);
        var cut = Render<ClipApexRow>(p => p.Add(x => x.Node, new ApexNode(apex, EffectiveActors.None)));

        Assert.Empty(cut.FindAll(".apex-editor-actors"));
    }
}
