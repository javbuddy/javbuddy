using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class ApexPreviewPopoverTests : BunitContext
{
    private IRenderedComponent<ApexPreviewPopover> RenderPopover(string? previewUrl = null, bool generating = false, IReadOnlyList<SceneTagItem>? tags = null, IReadOnlyList<SceneActorItem>? actors = null) =>
        Render<ApexPreviewPopover>(p => p
            .Add(x => x.PreviewUrl, previewUrl)
            .Add(x => x.Generating, generating)
            .Add(x => x.Actors, actors ?? [])
            .Add(x => x.Tags, tags ?? [])
            .Add(x => x.Seconds, 2530));

    [Fact]
    public void WithAPreview_PlaysIt()
    {
        var cut = RenderPopover("/apex-image/4/preview?v=9");

        Assert.Equal("/apex-image/4/preview?v=9", cut.Find("video").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".scene-preview-shimmer"));
    }

    [Fact]
    public void WhileGenerating_ShowsTheShimmer()
    {
        var cut = RenderPopover(generating: true);

        Assert.Empty(cut.FindAll("video"));
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
    }

    [Fact]
    public void WhenNoneCanBeMade_SaysSo()
    {
        var cut = RenderPopover();

        Assert.Equal("No preview", cut.Find(".scene-preview-none").TextContent);
    }

    [Fact]
    public void ShowsTheTagsAsChips_AndTheTime()
    {
        var cut = RenderPopover(tags: [new SceneTagItem(1, "Facial", null), new SceneTagItem(2, "Kiss", "Play")]);

        Assert.Equal(["Facial", "Play › Kiss"], cut.FindAll(".apex-preview-tag").Select(t => t.TextContent));
        Assert.Equal("42:10", cut.Find(".apex-preview-time").TextContent);
    }

    [Fact]
    public void WithoutTags_IsLabelledApex()
    {
        var cut = RenderPopover();

        Assert.Equal(["Apex"], cut.FindAll(".apex-preview-tag").Select(t => t.TextContent));
    }

    [Fact]
    public void ShowsTheActorsBeforeTheTags_AndNoApexLabelWithOnlyActors()
    {
        var actors = new[] { new SceneActorItem(1, "Aika"), new SceneActorItem(2, "Bea") };

        var withTags = RenderPopover(tags: [new SceneTagItem(1, "Facial", null)], actors: actors);
        Assert.Equal("Aika, Bea", withTags.Find(".apex-preview-tags > :first-child.apex-preview-actors").TextContent);

        var actorsOnly = RenderPopover(actors: actors);
        Assert.Empty(actorsOnly.FindAll(".apex-preview-tag"));
    }
}
