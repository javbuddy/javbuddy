using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The shared VirtualizedGrid remembers the scroll position for a back-navigation. The
/// position is written on a throttle, so leaving the page right after scrolling must still keep the
/// last position, which a real browser history round trip is the only way to check.</summary>
[Collection(E2ECollection.Name)]
public class MoviesScrollRestoreTests(E2EFixture fixture)
{
    [Fact]
    public async Task LeavingRightAfterScrolling_RestoresTheLastPositionOnBack()
    {
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 300, "E2E-RESTORE", MovieStatus.Got);
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!document.querySelector('.virtualized-grid > .virtualized-grid-spacer:last-child')?.style.height");

        // Scroll and leave (a full page load, which discards the page and its timers) in the same
        // task, well inside the persistence throttle: only a flush on 'pagehide' can save the position.
        var target = await page.EvaluateAsync<double>(
            """
            () => {
                window.scrollTo({ top: 4000, behavior: 'instant' });
                const y = window.scrollY;
                location.assign('/activity/queue');
                return y;
            }
            """);
        Assert.True(target > 1000, $"The page should have been tall enough to scroll, but scrolled to {target}px.");

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/activity/queue$"));
        await page.GoBackAsync();

        await page.WaitForFunctionAsync("t => Math.abs(window.scrollY - t) < 5", target, new() { Timeout = 10000 });
    }
}
