using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class MoviesRapidNavigationFlowTests(E2EFixture fixture)
{
    /// <summary>Regression test: rapidly bouncing between Missing and Movies
    /// intermittently crashed the circuit with "Failed to execute 'observe' on 'ResizeObserver':
    /// parameter 1 is not of type 'Element'" from Movies.razor.js's init(). Blazor resolves an
    /// ElementReference on the JS side with a plain document.querySelector at call time, so when
    /// the user navigated away while OnAfterRenderAsync was still awaiting the module import, the
    /// grid was already gone from the DOM and init() received null.
    ///
    /// Leaving at just the right moment is a timing lottery, so the import is held back with a
    /// route instead — which makes "navigated away before init() ran" deterministic rather than
    /// the roughly 1-in-3 it was in the wild. Needs a real browser: bUnit has no real DOM teardown
    /// or JS-side reference resolution.</summary>
    [Fact]
    public async Task LeavingMoviesBeforeItsGridInitialized_KeepsTheCircuitAlive()
    {
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 40, "E2E-BOUNCE");
        var page = await fixture.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error") consoleErrors.Add(msg.Text);
        };

        // MapStaticAssets fingerprints the collocated module (Movies.<hash>.razor.js), hence the
        // wildcard rather than the path Movies.razor imports it by.
        var releaseModule = new TaskCompletionSource();
        await page.RouteAsync("**/Pages/Movies.*razor.js", async route =>
        {
            await releaseModule.Task;
            await route.ContinueAsync();
        });

        // Starts on Missing so the Movies module hasn't been imported (and cached) yet.
        await page.GotoAsync("/missing");
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Missing" })).ToHaveClassAsync(new Regex("active"));

        var nav = page.Locator("nav.nav");
        await nav.Locator("a.nav-link", new() { HasText = "Movies" }).ClickAsync();
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();
        await nav.Locator("a.nav-link", new() { HasText = "Missing" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/missing$"));
        await Expect(page.Locator(".movies-grid")).ToHaveCountAsync(0);

        releaseModule.SetResult();
        await page.UnrouteAsync("**/Pages/Movies.*razor.js");

        // The circuit has to have survived the stale init: coming back must still produce a fully
        // initialized grid. init() sizes the trailing spacer from real geometry, which a dead
        // circuit (or a skipped init) never does.
        await nav.Locator("a.nav-link", new() { HasText = "Movies" }).ClickAsync();
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();
        await page.WaitForFunctionAsync(
            "document.querySelectorAll('.movies-grid .virtualized-grid-spacer')[1].style.height !== ''");
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.DoesNotContain(consoleErrors, e => e.Contains("ResizeObserver") || e.Contains("Connected' State"));
    }
}
