using Bunit;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>ClipEditor's nesting: highlights under the scene they start in, apexes under their
/// parent highlight or scene, the rest under "Outside scenes", with rolled-up tags shown grayed.</summary>
public class ClipEditorTreeTests : ClipEditorTestBase
{
    private int sceneId;

    private async Task<int> SeedScenesAsync()
    {
        sceneId = (await sceneService.AddSceneAsync(movieId, 0, 100, "Intro")).SceneId!.Value;
        await sceneService.AddSceneAsync(movieId, 100, 200, "Main");
        return sceneId;
    }

    [Fact]
    public async Task HighlightsAndApexes_NestUnderWhatHoldsThem()
    {
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await apexService.AddApexAsync(movieId, 20, []);   // in the highlight
        await apexService.AddApexAsync(movieId, 150, []);  // in Main, no highlight

        var cut = RenderClipEditor();

        var scenes = cut.FindAll(".scene-editor-list > .scene-editor-item");
        Assert.Equal(2, scenes.Count);
        var highlight = Assert.Single(scenes[0].QuerySelectorAll(".highlight-editor-item"));
        Assert.Contains("Kiss", highlight.QuerySelector(".highlight-editor-name")!.TextContent);
        Assert.Equal("0:20", Assert.Single(highlight.QuerySelectorAll(".apex-editor-item .apex-editor-time")).TextContent);
        Assert.Equal("2:30", Assert.Single(scenes[1].QuerySelectorAll(".apex-editor-item .apex-editor-time")).TextContent);
        Assert.Empty(cut.FindAll(".clip-editor-outside"));
    }

    [Fact]
    public async Task ClipsNoSceneHolds_AreGroupedUnderOutsideScenes()
    {
        await sceneService.AddSceneAsync(movieId, 100, 200, "Main");
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Before");
        await apexService.AddApexAsync(movieId, 300, []);

        var cut = RenderClipEditor();

        var outside = cut.Find(".clip-editor-outside");
        Assert.Equal("Outside scenes", outside.QuerySelector(".clip-editor-outside-heading")!.TextContent);
        Assert.Single(outside.QuerySelectorAll(".highlight-editor-item"));
        Assert.Single(outside.QuerySelectorAll(".apex-editor-item"));
    }

    [Fact]
    public async Task ARolledUpTag_ShowsGrayedOnTheSceneAndHighlight_WithoutARemoveButton()
    {
        var squirt = SeedTag("Squirt");
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await apexService.AddApexAsync(movieId, 20, [squirt]);

        var cut = RenderClipEditor();

        var sceneChip = cut.Find(".scene-editor-row .implicit-chip");
        Assert.Equal(("Squirt", "From apex 1"), (sceneChip.TextContent, sceneChip.GetAttribute("title")));
        Assert.Equal("Squirt", cut.Find(".highlight-editor-row .implicit-chip").TextContent);
        Assert.Empty(cut.FindAll(".scene-editor-row .scene-editor-meta-tag"));
    }

    [Fact]
    public async Task ASceneHighlightsAndApexes_AreListedInTimeOrder()
    {
        await SeedScenesAsync();
        await apexService.AddApexAsync(movieId, 5, []);
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await apexService.AddApexAsync(movieId, 60, []);

        var cut = RenderClipEditor();

        var children = cut.FindAll(".scene-editor-list > .scene-editor-item:first-child > .clip-editor-children > li");
        Assert.Equal(["apex-editor-item", "highlight-editor-item", "apex-editor-item"], children.Select(c => c.ClassName));
        Assert.Equal("0:05", children[0].QuerySelector(".apex-editor-time")!.TextContent);
    }

    [Fact]
    public async Task EditingAScene_ShowsItsRolledUpTagsGrayed_BesideItsOwn()
    {
        var bath = SeedTag("Bath");
        var squirt = SeedTag("Squirt");
        await SeedScenesAsync();
        await sceneService.AddSceneTagAsync(sceneId, bath);
        await apexService.AddApexAsync(movieId, 20, [squirt]);
        var cut = RenderClipEditor();

        await cut.Find(".scene-editor-edit-btn").ClickAsync();

        var links = cut.Find(".scene-editor-links");
        Assert.Contains("Bath", links.QuerySelector(".scene-editor-tag-chip")!.TextContent);
        var implied = Assert.Single(links.QuerySelectorAll(".implicit-chip"));
        Assert.Equal(("Squirt", "From apex 1"), (implied.TextContent, implied.GetAttribute("title")));
        Assert.Single(links.QuerySelectorAll(".scene-editor-chip-remove"));
    }

    [Fact]
    public async Task EditingAScene_MarksItsOwnTagsItsClipsCarry_AndRemovingOneLeavesItGrayed()
    {
        var bath = SeedTag("Bath");
        var squirt = SeedTag("Squirt");
        await SeedScenesAsync();
        await sceneService.AddSceneTagAsync(sceneId, bath);
        await sceneService.AddSceneTagAsync(sceneId, squirt);
        await apexService.AddApexAsync(movieId, 20, [squirt]);
        var cut = RenderClipEditor();

        await cut.Find(".scene-editor-edit-btn").ClickAsync();

        var chips = cut.FindAll(".scene-editor-links .scene-editor-tag-chip");
        Assert.Null(chips.Single(c => c.TextContent.Contains("Bath")).QuerySelector(".redundant-mark"));
        Assert.Equal("Also from apex 1. Remove it and it stays, grayed, as implied.",
            chips.Single(c => c.TextContent.Contains("Squirt")).QuerySelector(".redundant-mark")!.GetAttribute("title"));
        Assert.Empty(cut.FindAll(".scene-editor-links .implicit-chip"));

        await cut.Find(".scene-editor-links .scene-editor-chip-remove[aria-label='Remove Squirt']").ClickAsync();

        Assert.Equal("Squirt", cut.Find(".scene-editor-links .implicit-chip").TextContent);
        Assert.Empty(cut.FindAll(".scene-editor-links .redundant-mark"));
    }

    [Fact]
    public async Task EditingAHighlight_ShowsItsApexesTagsGrayed_UntilOneIsAddedAsItsOwn()
    {
        var squirt = SeedTag("Squirt");
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await apexService.AddApexAsync(movieId, 20, [squirt]);
        var cut = RenderClipEditor();

        await cut.Find(".highlight-editor-edit-btn").ClickAsync();

        var implied = Assert.Single(cut.FindAll(".highlight-editor-form-wrap .implicit-chip"));
        Assert.Equal(("Squirt", "From apex 1"), (implied.TextContent, implied.GetAttribute("title")));
        Assert.Empty(cut.FindAll(".highlight-editor-form-wrap .highlight-editor-chip-remove"));

        await cut.InvokeAsync(() => cut.FindComponent<Javbuddy.Components.Shared.TagSearchAdd>().Instance.OnAdd.InvokeAsync(squirt));

        Assert.Contains("Squirt", cut.Find(".highlight-editor-form-wrap .highlight-editor-chip").TextContent);
        Assert.Empty(cut.FindAll(".highlight-editor-form-wrap .implicit-chip"));
    }

    [Fact]
    public async Task ASceneCrossingHighlight_SaysWhichScenesItContinuesInto()
    {
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 90, 120, "Crossing");

        var cut = RenderClipEditor();

        var scenes = cut.FindAll(".scene-editor-list > .scene-editor-item");
        Assert.Single(scenes[0].QuerySelectorAll(".highlight-editor-item"));
        Assert.Empty(scenes[1].QuerySelectorAll(".highlight-editor-item"));
        Assert.Equal("continues into scene 2", cut.Find(".highlight-editor-continues").TextContent);
    }

    [Fact]
    public async Task DeletingAScene_KeepsItsClips_UnderOutsideScenes()
    {
        await sceneService.AddSceneAsync(movieId, 0, 100, "Intro");
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        var cut = RenderClipEditor();

        await cut.Find(".scene-editor-delete-btn").ClickAsync();
        Assert.Contains("Highlights and apexes inside it stay.", cut.Find(".delete-confirm-dialog").TextContent);
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync();

        Assert.Empty(cut.FindAll(".scene-editor-list"));
        Assert.Single(cut.Find(".clip-editor-outside").QuerySelectorAll(".highlight-editor-item"));
    }

    [Fact]
    public async Task ChangingASceneTag_TellsTheHostTheMovieTagsMayHaveChanged()
    {
        var bath = SeedTag("Bath");
        await sceneService.AddSceneAsync(movieId, 0, 100, "Intro");
        var raised = 0;
        var cut = RenderClipEditor(onMovieTags: () => raised++);

        await cut.Find(".scene-editor-edit-btn").ClickAsync();
        await cut.InvokeAsync(() => cut.FindComponent<Javbuddy.Components.Shared.TagSearchAdd>().Instance.OnAdd.InvokeAsync(bath));
        Assert.Equal(1, raised);

        await cut.Find(".scene-editor-chip-remove").ClickAsync();
        Assert.Equal(2, raised);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.MovieTags);
    }

    [Fact]
    public async Task AllThreeAddButtons_ShowUntilTheirFormOpens()
    {
        var cut = RenderClipEditor();

        Assert.Equal(["+ Scene", "+ Highlight", "+ Apex"], cut.FindAll(".clip-editor-add-row button").Select(b => b.TextContent));
        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        Assert.Equal(["+ Scene", "+ Apex"], cut.FindAll(".clip-editor-add-row button").Select(b => b.TextContent));
        Assert.Single(cut.FindAll(".highlight-editor-form"));
    }

    [Fact]
    public async Task CollapsingAScene_HidesItsHighlightsAndApexes_AndShowsWhatItHides()
    {
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await highlightService.AddHighlightAsync(movieId, 50, 60, "Hug");
        await apexService.AddApexAsync(movieId, 20, []);   // in Kiss
        await apexService.AddApexAsync(movieId, 150, []);  // in Main

        var cut = RenderClipEditor();
        var toggle = cut.Find(".scene-editor-list > .scene-editor-item:first-child .clip-editor-collapse-btn");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".clip-editor-collapsed-badge"));

        toggle.Click();

        var intro = cut.Find(".scene-editor-list > .scene-editor-item:first-child");
        Assert.Empty(intro.QuerySelectorAll(".clip-editor-children"));
        Assert.Equal("false", intro.QuerySelector(".clip-editor-collapse-btn")!.GetAttribute("aria-expanded"));
        Assert.Equal("2 highlights · 1 apex", intro.QuerySelector(".scene-editor-row .clip-editor-collapsed-badge")!.TextContent);
        Assert.Single(cut.FindAll(".scene-editor-list > .scene-editor-item:last-child .apex-editor-item"));

        cut.Find(".scene-editor-list > .scene-editor-item:first-child .clip-editor-collapse-btn").Click();

        Assert.Equal(3, cut.FindAll(".scene-editor-list > .scene-editor-item:first-child li").Count);
        Assert.Empty(cut.FindAll(".clip-editor-collapsed-badge"));
    }

    [Fact]
    public async Task ASceneHoldingNothing_HasNoCollapseToggle()
    {
        await SeedScenesAsync();
        await apexService.AddApexAsync(movieId, 150, []);

        var cut = RenderClipEditor();

        var scenes = cut.FindAll(".scene-editor-list > .scene-editor-item");
        Assert.Null(scenes[0].QuerySelector(".clip-editor-collapse-btn"));
        Assert.NotNull(scenes[0].QuerySelector(".clip-editor-collapse-spacer"));
        Assert.NotNull(scenes[1].QuerySelector(".clip-editor-collapse-btn"));
    }

    [Fact]
    public async Task CollapseAll_CollapsesEveryScene_ThenExpandAllShowsThemAgain()
    {
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 10, 40, "Kiss");
        await apexService.AddApexAsync(movieId, 150, []);

        var cut = RenderClipEditor();
        Assert.Equal("Collapse all", cut.Find(".clip-editor-collapse-all-btn").GetAttribute("aria-label"));

        cut.Find(".clip-editor-collapse-all-btn").Click();

        Assert.Empty(cut.FindAll(".clip-editor-children"));
        Assert.Equal(["1 highlight", "1 apex"], cut.FindAll(".clip-editor-collapsed-badge").Select(b => b.TextContent));
        Assert.Equal("Expand all", cut.Find(".clip-editor-collapse-all-btn").GetAttribute("aria-label"));

        cut.Find(".clip-editor-collapse-all-btn").Click();

        Assert.Equal(2, cut.FindAll(".clip-editor-children").Count);
        Assert.Equal("Collapse all", cut.Find(".clip-editor-collapse-all-btn").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task WithoutHighlightsOrApexesInScenes_ThereIsNoCollapseAll()
    {
        await SeedScenesAsync();
        await highlightService.AddHighlightAsync(movieId, 300, 400, "Outside");

        var cut = RenderClipEditor();

        Assert.Empty(cut.FindAll(".clip-editor-collapse-all-btn"));
        Assert.Empty(cut.FindAll(".clip-editor-collapse-btn"));
    }
}
