using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The scene form's fields stack tightly. Its CSS is scoped to ClipSceneForm, and a field rule
/// copied over with a flex-basis once gave the Title row 8rem of height in the form's column, which only a
/// real browser's layout shows.</summary>
[Collection(E2ECollection.Name)]
public class ClipSceneFormLayoutFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task NewSceneForm_TitleAndStartSitCloseTogether()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-FORMGAP-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.MediaDurationSeconds = 3;
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        var form = page.Locator(".video-player-side-panel .scene-editor-form");
        await page.Locator(".video-player-side-panel .scene-editor-add-btn").ClickAsync();
        await Expect(form).ToBeVisibleAsync();

        var title = (await form.Locator(".scene-editor-title-input").BoundingBoxAsync())!;
        var start = (await form.Locator(".scene-editor-times .scene-editor-label").First.BoundingBoxAsync())!;
        var gap = start.Y - (title.Y + title.Height);
        Assert.True(gap < 20, $"Title input and the Start label are {gap}px apart.");
    }
}
