using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ClipCardTests : BunitContext
{
    private readonly ISceneMediaService media = Substitute.For<ISceneMediaService>();
    private readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    private readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();

    private static readonly SceneWallCard Scene =
        new(1, 10, "ABC-123", "Title", "Scene 1", 60, 600, ["Aika"], [new SceneTagItem(1, "Rough", "Play")], true, 111, 222) { ApexCount = 2 };

    private static readonly HighlightWallCard Highlight =
        new(2, 10, "ABC-123", "Title", "Highlight 1", 90, 120, [], [], false, null, null) { HasLocalVideo = true };

    private static readonly ApexWallCard Apex =
        new(3, 10, "ABC-123", "Title", "Aika — Squirt", 300, ["Aika"], [], false, 333) { DurationSeconds = 3600, HasLocalVideo = true, ThumbVersion = 222 };

    public ClipCardTests()
    {
        Services.AddSingleton(media);
        Services.AddSingleton(highlightMedia);
        Services.AddSingleton(apexMedia);
        highlightMedia.ServesVariant(Arg.Any<string>()).Returns(true);
        apexMedia.ServesVariant(Arg.Any<string>()).Returns(true);
    }

    [Fact]
    public void SceneCard_LinksToTheScene_AndShowsItsDetails()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, Scene));

        var card = cut.Find("a.scene-wall-card");
        Assert.Equal("/movies/ABC-123?scene=1", card.GetAttribute("href"));
        Assert.Equal("9:00", cut.Find(".scene-wall-duration").TextContent);
        Assert.Equal("Scene 1", cut.Find(".scene-wall-scene-title").TextContent);
        Assert.Equal("Play › Rough", cut.Find(".scene-wall-tag").TextContent);
        Assert.Contains("2", cut.Find(".scene-wall-apex-count").TextContent);
        Assert.Single(cut.FindAll(".scene-wall-fav"));
    }

    [Fact]
    public void SceneCard_ShowsRolledUpTagsAfterItsOwn_MarkedAsImplied()
    {
        var scene = Scene with { ImplicitTags = [new ImplicitTag(new SceneTagItem(2, "Squirt", null), ["highlight 1", "apex 2"])] };

        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, scene));

        Assert.Equal(["Play › Rough", "Squirt"], cut.FindAll(".scene-wall-tag").Select(t => t.TextContent));
        var implied = cut.Find(".scene-wall-tag-implicit");
        Assert.Equal("Squirt", implied.TextContent);
        Assert.Equal("From highlight 1, apex 2", implied.GetAttribute("title"));
    }

    // The +N badge's count, which tags it hides and the tray it opens are ClipCard.razor.js's, measured
    // in the browser: the card renders the row in its one-line slot, ending in the empty
    // badge, with no browser tooltip in their place.
    [Fact]
    public void TagRow_EndsInAnEmptyOverflowBadge_WithoutABrowserTooltip()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, Scene));

        var row = cut.Find(".scene-wall-tags-slot > .scene-wall-tags");
        Assert.Null(row.GetAttribute("title"));
        Assert.Null(row.GetAttribute("data-overflow"));
        var badge = row.LastElementChild!;
        Assert.Contains("scene-wall-tags-more", badge.ClassList);
        Assert.Empty(badge.TextContent);
        Assert.Null(badge.GetAttribute("data-count"));
    }

    [Fact]
    public void HighlightCard_ShowsTagsRolledUpFromItsApexes()
    {
        var highlight = Highlight with { ImplicitTags = [new ImplicitTag(new SceneTagItem(3, "Creampie", null), ["apex 1"])] };

        var cut = Render<ClipCard>(p => p.Add(c => c.Highlight, highlight));

        Assert.Equal("From apex 1", cut.Find(".scene-wall-tag-implicit").GetAttribute("title"));
    }

    // The preview is played by the browser (ClipCard.razor.js's bindHoverPreviews) from the URL in
    // data-preview-src, not by a server-side hover handler: that took a round trip and a re-render for
    // every card the pointer crossed.
    [Fact]
    public void Scene_LeavesItsPreviewForTheBrowserToPlayOnHover()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, Scene));

        Assert.Equal("/scene-image/1/preview?v=222", cut.Find(".scene-wall-media .scene-preview-live").GetAttribute("data-preview-src"));
        Assert.Empty(cut.FindAll("video"));
        Assert.Empty(cut.Find(".scene-preview-live").Children);
        Assert.DoesNotContain("blazor:onmouseenter", cut.Markup);
        Assert.DoesNotContain("blazor:onmouseleave", cut.Markup);
    }

    [Fact]
    public void Highlight_LeavesItsPreviewForTheBrowserToPlayOnHover()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Highlight, Highlight with { ThumbVersion = 11, PreviewVersion = 22 }));

        Assert.Equal("/highlight-image/2/preview?v=22", cut.Find(".scene-wall-media .scene-preview-live").GetAttribute("data-preview-src"));
        Assert.Empty(cut.FindAll("video"));
        Assert.DoesNotContain("blazor:onmouseenter", cut.Markup);
    }

    [Fact]
    public void ACardWithAScreenshotButNoPreview_HasNothingToPlayOnHover()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, Scene with { PreviewVersion = null }));

        Assert.Single(cut.FindAll(".scene-wall-media img"));
        Assert.Empty(cut.FindAll(".scene-preview-live"));
    }

    [Fact]
    public void HighlightCard_WithoutAScreenshot_ShowsTheGeneratingPlaceholder()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Highlight, Highlight));

        Assert.Equal("/movies/ABC-123?highlight=2", cut.Find("a.scene-wall-card-highlight").GetAttribute("href"));
        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
        Assert.Equal("0:30", cut.Find(".scene-wall-duration").TextContent);
    }

    [Fact]
    public void ApexCard_HasNoTitle_AndLinksToItsClipStart()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Apex, Apex));

        Assert.StartsWith("/movies/ABC-123?t=", cut.Find("a.scene-wall-card-apex").GetAttribute("href"));
        Assert.Empty(cut.FindAll(".scene-wall-scene-title"));
    }

    [Fact]
    public void ApexCard_ShowsItsScreenshotAsAnImage_AndHasNoVideoOfItsOwn()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Apex, Apex));

        Assert.Equal("/apex-image/3/thumb?v=222", cut.Find(".scene-wall-media img").GetAttribute("src"));
        Assert.Equal("lazy", cut.Find(".scene-wall-media img").GetAttribute("loading"));
        Assert.Empty(cut.FindAll("video"));
        Assert.Equal("/apex-image/3/preview?v=333", cut.Find(".scene-preview-live").GetAttribute("data-preview-src"));
        Assert.DoesNotContain("blazor:onmouseenter", cut.Markup);
    }

    [Fact]
    public void ApexCard_WithoutAScreenshot_ShowsTheGeneratingPlaceholder_NotTheVideo()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Apex, Apex with { ThumbVersion = null }));

        Assert.Single(cut.FindAll(".scene-preview-shimmer"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("video"));
    }

    [Fact]
    public void PlainClick_PlaysTheClip()
    {
        PlayerClip? played = null;
        var cut = Render<ClipCard>(p => p
            .Add(c => c.Apex, Apex)
            .Add(c => c.OnPlay, EventCallback.Factory.Create<PlayerClip>(this, clip => played = clip)));

        cut.Find("a").Click();

        Assert.Equal(PlayerClip.FromApex(Apex), played);
    }

    [Theory]
    [InlineData(true, false, false, false, 0)]
    [InlineData(false, true, false, false, 0)]
    [InlineData(false, false, true, false, 0)]
    [InlineData(false, false, false, true, 0)]
    [InlineData(false, false, false, false, 1)]
    public void ModifierOrMiddleClick_LeavesItToTheLink(bool ctrl, bool meta, bool shift, bool alt, long button)
    {
        PlayerClip? played = null;
        var cut = Render<ClipCard>(p => p
            .Add(c => c.Scene, Scene)
            .Add(c => c.OnPlay, EventCallback.Factory.Create<PlayerClip>(this, clip => played = clip)));

        cut.Find("a").Click(new MouseEventArgs { CtrlKey = ctrl, MetaKey = meta, ShiftKey = shift, AltKey = alt, Button = button });

        Assert.Null(played);
    }
}
