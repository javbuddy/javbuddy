using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>A scene row's favorite, edit and delete buttons sit level with its number and title, so its tags
/// can use the row's full width — a real-layout check bUnit can't make.</summary>
[Collection(E2ECollection.Name)]
public class ClipRowActionsLayoutFlowTests
{
    private const string Video = "video.video-player-video";

    private readonly E2EFixture fixture;

    public ClipRowActionsLayoutFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task SceneRowButtons_SitOnTheTitleLine_AndTheTagsRunUnderThem()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ROWBTN-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.MediaDurationSeconds = 3;
            var scene = new Scene { MovieId = m.Id, StartSeconds = 0, Title = "Row buttons" };
            foreach (var tagName in new[] { "Cosplay", "Fingering", "Kiss", "Stockings", "Blowjob", "Squirting", "Cowgirl", "Missionary" })
            {
                scene.SceneTags.Add(new SceneTag { Tag = new Tag { Name = $"RB {tagName}" } });
            }
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}");
        var panel = page.Locator(".video-player-side-panel");
        var row = panel.Locator(".scene-editor-row");
        await Expect(row).ToHaveCountAsync(1);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "clip-row-buttons.png") });

        var name = (await row.Locator(".scene-editor-name").BoundingBoxAsync())!;
        var edit = (await panel.Locator(".scene-editor-delete-btn").BoundingBoxAsync())!;
        var rowBox = (await row.BoundingBoxAsync())!;
        var tagBoxes = await row.Locator(".scene-editor-meta-tag").EvaluateAllAsync<double[][]>(
            "ts => ts.map(t => { const r = t.getBoundingClientRect(); return [r.left, r.right, r.top]; })");

        // On the title line: the button's vertical centre is within the title's own height.
        var buttonMid = edit.Y + (edit.Height / 2);
        Assert.InRange(buttonMid, name.Y, name.Y + name.Height);

        // The buttons end at the row's right edge, and the title stops before them.
        Assert.InRange(edit.X + edit.Width, rowBox.X + rowBox.Width - 12, rowBox.X + rowBox.Width + 1);
        Assert.True(name.X + name.Width <= (await panel.Locator(".scene-editor-fav-btn").BoundingBoxAsync())!.X + 1, "the title runs under the buttons");

        // The tags wrap on lines below the buttons, so none is squeezed beside them: all sit under the title line.
        Assert.Equal(8, tagBoxes.Length);
        Assert.All(tagBoxes, t => Assert.True(t[2] >= name.Y + name.Height - 1, "a tag sits on the title line"));
    }
}
