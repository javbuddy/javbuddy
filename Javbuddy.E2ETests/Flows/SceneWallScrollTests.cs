using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The scene wall's virtualized scrolling, against real layout: the shared
/// VirtualizedGrid only keeps its scrollbar honest while every scene card is the same height, which
/// bUnit can't measure.</summary>
[Collection(E2ECollection.Name)]
public class SceneWallScrollTests(E2EFixture fixture)
{
    [Fact]
    public async Task ScrollingToTheBottom_ReachesTheLastScene_WithoutTheWallChangingHeight()
    {
        await DbSeeding.SeedManyScenesAsync(fixture.DbFactory, 40, 8, "E2E-WALL");
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/movies/scenes");
        await Expect(page.Locator(".scene-wall-card").First).ToBeVisibleAsync();
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Load more" }).CountAsync());

        // Same frame-by-frame sampling as MoviesGridScrollTests: scroll in fixed steps and record the
        // wall's height every frame, then keep stepping until the bottom of the page.
        var heightsJson = await page.EvaluateAsync<string>(
            """
            async () => {
                const grid = document.querySelector('.virtualized-grid');
                window.scrollTo({ top: 0, behavior: 'instant' });
                const heights = [];
                await new Promise(resolve => {
                    let y = 0;
                    let frames = 0;
                    function tick() {
                        heights.push(grid.getBoundingClientRect().height);
                        y += 120;
                        window.scrollTo({ top: y, behavior: 'instant' });
                        frames++;
                        const atBottom = window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 1;
                        if (frames < 400 && !atBottom) requestAnimationFrame(tick); else resolve();
                    }
                    requestAnimationFrame(tick);
                });
                return JSON.stringify(heights);
            }
            """);
        var heights = System.Text.Json.JsonSerializer.Deserialize<List<double>>(heightsJson)!;

        var settled = heights[^1];
        var deviations = heights.Select((height, frame) => (frame, height)).Where(x => Math.Abs(x.height - settled) > 1).ToList();
        Assert.True(deviations.Count == 0,
            $"Wall height should hold steady at {settled}px, but deviated {deviations.Count} time(s): " +
            string.Join(", ", deviations.Take(5).Select(d => $"frame {d.frame}: {d.height}px")));

        // At the bottom, the loaded window ends with the wall's last card.
        var spacer = page.Locator(".virtualized-grid-spacer").First;
        await Expect(spacer).Not.ToHaveAttributeAsync("data-window-start", "0");
        await page.WaitForFunctionAsync(
            """
            () => {
                const d = document.querySelector('.virtualized-grid-spacer').dataset;
                return Number(d.windowStart) + Number(d.windowCount) === Number(d.totalCount);
            }
            """);
        await Expect(page.Locator(".scene-wall-card").Last).ToBeInViewportAsync();
    }

    [Fact]
    public async Task SwitchingToHighlights_StartsBackAtTheTop()
    {
        await DbSeeding.SeedManyScenesAsync(fixture.DbFactory, 40, 8, "E2E-WALLTOP");
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/movies/scenes");
        await Expect(page.Locator(".scene-wall-card").First).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.scrollTo({ top: 6000, behavior: 'instant' })");
        await Expect(page.Locator(".virtualized-grid-spacer").First).Not.ToHaveAttributeAsync("data-window-start", "0");

        // Clicked from script: a Playwright click would scroll the toggle into view (the top) itself.
        await page.EvaluateAsync("() => document.querySelector('[data-view=highlights]').click()");

        await Expect(page.Locator(".virtualized-grid-spacer").First).ToHaveAttributeAsync("data-window-start", "0");
        await page.WaitForFunctionAsync("() => window.scrollY === 0");
    }
}
