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
        new(3, 10, "ABC-123", "Title", "Aika — Squirt", 300, ["Aika"], [], false, 333) { DurationSeconds = 3600 };

    public ClipCardTests()
    {
        Services.AddSingleton(media);
        Services.AddSingleton(highlightMedia);
        Services.AddSingleton(apexMedia);
        highlightMedia.ServesVariant(Arg.Any<string>()).Returns(true);
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

    [Fact]
    public void Hover_PlaysThePreview()
    {
        var cut = Render<ClipCard>(p => p.Add(c => c.Scene, Scene));
        Assert.DoesNotContain("<video", cut.Markup);

        cut.Find("a").MouseEnter();
        Assert.Contains("/scene-image/1/preview?v=222", cut.Markup);

        cut.Find("a").MouseLeave();
        Assert.DoesNotContain("/scene-image/1/preview", cut.Markup);
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
