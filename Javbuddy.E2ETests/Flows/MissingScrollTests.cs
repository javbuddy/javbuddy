using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The Missing page's virtualized list, against real layout: the shared
/// VirtualizedGrid only keeps its scrollbar honest while every row is the same height, which bUnit
/// can't measure.</summary>
[Collection(E2ECollection.Name)]
public class MissingScrollTests(E2EFixture fixture)
{
    [Fact]
    public async Task ScrollingToTheBottom_ReachesTheLastMissingMovie_WithoutTheListChangingHeight()
    {
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 300, "E2E-MISS", MovieStatus.Missing);
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/missing");
        await Expect(page.Locator(".missing-row-item").First).ToBeVisibleAsync();
        // That row can still be the prerendered list, which the circuit replaces with its own once it
        // attaches; sampling across that swap measures a detached element. The grid's script sizes the
        // live list's spacers, so once it has, the list being sampled is the one that stays.
        await page.WaitForFunctionAsync("() => !!document.querySelector('.virtualized-grid > .virtualized-grid-spacer:last-child')?.style.height");

        // Same frame-by-frame sampling as SceneWallScrollTests: scroll in fixed steps and record the
        // list's height every frame, then keep stepping until the bottom of the page.
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
            $"List height should hold steady at {settled}px, but deviated {deviations.Count} time(s): " +
            string.Join(", ", deviations.Take(5).Select(d => $"frame {d.frame}: {d.height}px")));

        // At the bottom, the loaded window ends with the last Missing movie, and only a window's worth
        // of rows is in the DOM.
        var spacer = page.Locator(".virtualized-grid-spacer").First;
        await Expect(spacer).Not.ToHaveAttributeAsync("data-window-start", "0");
        await page.WaitForFunctionAsync(
            """
            () => {
                const d = document.querySelector('.virtualized-grid-spacer').dataset;
                return Number(d.windowStart) + Number(d.windowCount) === Number(d.totalCount);
            }
            """);
        await Expect(page.Locator(".missing-row-item").Last).ToBeInViewportAsync();
        Assert.True(await page.Locator(".missing-row-item").CountAsync() < 300);
    }
}
