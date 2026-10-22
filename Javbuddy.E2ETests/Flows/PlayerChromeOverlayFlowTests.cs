using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The player's header and scrub bar overlay the video instead of taking space around it, so the
/// video fills the player from top to bottom and doesn't resize when the chrome shows or hides.</summary>
[Collection(E2ECollection.Name)]
public class PlayerChromeOverlayFlowTests
{
    private readonly E2EFixture fixture;

    public PlayerChromeOverlayFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task TheHeaderAndScrubBar_OverlayTheVideoArea()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-OVERLAY-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == movie.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.MediaDurationSeconds, 3));
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
            await Expect(page.Locator(".video-player-column .scrub-bar-track")).ToBeVisibleAsync();

            var panel = (await page.Locator(".video-player-modal-panel").BoundingBoxAsync())!;
            var area = (await page.Locator(".video-player-area").BoundingBoxAsync())!;
            var header = (await page.Locator(".video-player-modal-header").BoundingBoxAsync())!;
            var scrub = (await page.Locator(".video-player-column .scrub-bar").BoundingBoxAsync())!;

            Assert.Equal(panel.Y, area.Y, 1);
            Assert.Equal(panel.Y + panel.Height, area.Y + area.Height, 1);
            var side = (await page.Locator(".video-player-side-panel").BoundingBoxAsync())!;
            Assert.True(area.X + area.Width <= side.X, "the side panel still narrows the video");
            Assert.True(header.Y >= area.Y && header.Y + header.Height <= area.Y + area.Height, "header sits inside the video area");
            Assert.True(scrub.Y >= area.Y && scrub.Y + scrub.Height <= area.Y + area.Height + 1, "scrub bar sits inside the video area");

            // Fullscreen keeps the overlay: the controls sit inside a video that fills the screen.
            await Expect(page.Locator(".scrub-bar-clock")).ToHaveTextAsync(new Regex(@"^\d+:\d\d / \d+:\d\d$"));
            await page.Locator(".video-player-modal-backdrop").FocusAsync();
            await page.Keyboard.PressAsync("f");
            await page.WaitForFunctionAsync("() => document.fullscreenElement?.classList.contains('video-player-column')");
            var fsArea = (await page.Locator(".video-player-area").BoundingBoxAsync())!;
            var fsScrub = (await page.Locator(".video-player-column .scrub-bar").BoundingBoxAsync())!;
            var viewport = page.ViewportSize!;
            Assert.Equal(0, fsArea.Y, 1);
            Assert.Equal(viewport.Height, fsArea.Y + fsArea.Height, 1);
            Assert.True(fsScrub.Y >= fsArea.Y && fsScrub.Y + fsScrub.Height <= fsArea.Y + fsArea.Height + 1, "scrub bar overlays the fullscreen video");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
