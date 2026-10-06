using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The player modal's scene editor keeps its header, add buttons and open add forms pinned while a
/// long scene list scrolls, with nothing scrolled peeking above them, and unpins them while
/// they're taller than the panel — real-layout checks bUnit can't make.</summary>
[Collection(E2ECollection.Name)]
public class SceneEditorStickyAddRowFlowTests
{
    private const string Video = "video.video-player-video";

    // Whether the element's center is inside the side panel and a click there lands on it.
    private const string ClickableInPanel =
        "e => { const r = e.getBoundingClientRect(), p = e.closest('.video-player-side-panel').getBoundingClientRect(); " +
        "const x = r.left + r.width / 2, y = r.top + r.height / 2; " +
        "return y > p.top && y < p.bottom && e.contains(document.elementFromPoint(x, y)); }";

    private static readonly Regex Unpinned = new("clip-editor-top-unpinned");

    private readonly E2EFixture fixture;

    public SceneEditorStickyAddRowFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task ScrollingTheSceneList_KeepsTheAddButtonsInView()
    {
        var panel = await OpenEditorWithManyScenesAsync("E2E-STICKY-1");

        await ScrollToBottomAsync(panel);

        var panelBox = (await panel.BoundingBoxAsync())!;
        var firstRowBox = (await panel.Locator(".scene-editor-row").First.BoundingBoxAsync())!;
        Assert.True(firstRowBox.Y + firstRowBox.Height < panelBox.Y, "the panel didn't scroll past its first scene");

        // Pinned at the panel's top edge, and on top: a click there lands on the button, not a scrolled row.
        var addBox = (await panel.Locator(".scene-editor-add-btn").BoundingBoxAsync())!;
        Assert.InRange(addBox.Y, panelBox.Y, panelBox.Y + 60);
        Assert.True(await ClickableAsync(panel.Locator(".scene-editor-add-btn")), "a scrolled row covers the pinned add button");
        Assert.True(await panel.Locator(".scene-editor-heading").IsVisibleAsync());

        // The three add buttons share one row.
        var addYs = await panel.Locator(".clip-editor-add-row button").EvaluateAllAsync<double[]>("bs => bs.map(b => b.getBoundingClientRect().y)");
        Assert.Equal(3, addYs.Length);
        Assert.All(addYs, y => Assert.Equal(addYs[0], y, 0.5));

        // Nothing scrolled shows above or around the pinned block: at its top edge and just inside the panel,
        // the topmost element belongs to it.
        var topCovered = await panel.Locator(".clip-editor-top").EvaluateAsync<bool>(
            "t => { const r = t.getBoundingClientRect(), p = t.closest('.video-player-side-panel').getBoundingClientRect(); " +
            "return r.top <= p.top + 2 && [r.left + 4, r.left + r.width / 2, r.right - 4].every(x => t.contains(document.elementFromPoint(x, p.top + 2))); }");
        Assert.True(topCovered, "a scrolled row shows above the pinned header");
    }

    [Fact]
    public async Task AddingWhileScrolledDown_ShowsTheNewFormUnderTheButtons()
    {
        var panel = await OpenEditorWithManyScenesAsync("E2E-STICKY-2");

        await ScrollToBottomAsync(panel);
        await panel.Locator(".highlight-editor-add-btn").ClickAsync();
        Assert.True(await ClickableAsync(panel.Locator(".highlight-editor-start-input")), "the new highlight form isn't in view");
        Assert.True(await ClickableAsync(panel.Locator(".highlight-editor-save-btn")), "the new highlight form's Add isn't in view");

        // It stays pinned while the list scrolls under it.
        await panel.EvaluateAsync("p => p.scrollTop = 0");
        await ScrollToBottomAsync(panel);
        Assert.True(await ClickableAsync(panel.Locator(".highlight-editor-save-btn")), "the highlight form scrolled away");
        await Expect(panel.Locator(".clip-editor-top")).Not.ToHaveClassAsync(Unpinned);
    }

    [Fact]
    public async Task FormsTallerThanThePanel_Unpin_SoTheirAddStaysReachable()
    {
        var panel = await OpenEditorWithManyScenesAsync("E2E-STICKY-3");
        var top = panel.Locator(".clip-editor-top");

        // A short window: three open forms no longer fit the panel.
        await panel.Page.SetViewportSizeAsync(1280, 480);
        await ScrollToBottomAsync(panel);
        await panel.Locator(".scene-editor-add-btn").ClickAsync();
        await panel.Locator(".highlight-editor-add-btn").ClickAsync();
        await panel.Locator(".apex-editor-add-btn").ClickAsync();
        await Expect(top).ToHaveClassAsync(Unpinned);

        // Scrolling reaches the last form's Add, which a pinned block taller than the panel would hide.
        var apexSave = panel.Locator(".apex-editor-save-btn");
        await apexSave.ScrollIntoViewIfNeededAsync();
        Assert.True(await ClickableAsync(apexSave), "the apex form's Add can't be reached");

        // Closing the forms pins it again.
        await panel.Locator(".apex-editor-cancel-btn").ClickAsync();
        await panel.Locator(".highlight-editor-cancel-btn").ClickAsync();
        await panel.Locator(".scene-editor-cancel-btn").ClickAsync();
        await Expect(top).Not.ToHaveClassAsync(Unpinned);
    }

    private async Task<ILocator> OpenEditorWithManyScenesAsync(string code)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.JellyfinItemId = $"fake-jf-item-{code.ToLowerInvariant()}";
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            // The fixture video is a 3-second clip; enough scenes inside it to overflow the panel.
            m.MediaDurationSeconds = 3;
            for (var i = 0; i < 40; i++)
            {
                db.Scenes.Add(new Scene { MovieId = m.Id, StartSeconds = i * 0.07, Title = $"Sticky {i + 1}" });
            }
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}");
        var panel = page.Locator(".video-player-side-panel");
        await Expect(panel.Locator(".scene-editor-row")).ToHaveCountAsync(40);
        return panel;
    }

    private static Task ScrollToBottomAsync(ILocator panel) => panel.EvaluateAsync("p => p.scrollTop = p.scrollHeight");

    private static Task<bool> ClickableAsync(ILocator element) => element.EvaluateAsync<bool>(ClickableInPanel);
}
