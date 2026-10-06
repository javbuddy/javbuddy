using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Regression test: a first implementation attempt persisted the
/// filter/sort cookie through a standalone JS module (Components/Shared/JsonCookieState.js) with
/// no matching .razor component. bUnit's mocked JSInterop never noticed, but Blazor's real
/// static-web-asset pipeline only discovers collocated `Component.razor.js` files — the module
/// 404d at runtime with no visible error, so the cookie write silently never happened and every
/// selection reset on refresh. This needs a real browser hitting the real static file server to
/// catch; bUnit can't.</summary>
[Collection(E2ECollection.Name)]
public class MoviesFilterPersistenceFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task ChangingStatusFilter_PersistsAcrossPageReload()
    {
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-FILTER-001", MovieStatus.Missing);
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-FILTER-002", MovieStatus.Got);

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        var missingButton = page.Locator(".btn-group button", new() { HasText = "Missing (" });
        await missingButton.ClickAsync();
        await Expect(missingButton).ToHaveClassAsync(new Regex("btn-secondary"));
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(1);

        // The cookie write is fire-and-forget from the server (Movies.razor's ResetAndLoadAsync ->
        // SaveViewStateAsync -> JS interop) — give it a moment to actually land in the browser
        // before reloading, rather than racing it.
        await page.WaitForFunctionAsync("document.cookie.includes('javbuddy-movies-view')");

        await page.ReloadAsync();
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        // The filter must already be applied in the very first (server-rendered) response, not
        // reset-then-reapplied a moment later — asserting immediately after the reload, before any
        // further interaction, is deliberate.
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(1);
        await Expect(page.Locator(".btn-group button", new() { HasText = "Missing (" })).ToHaveClassAsync(new Regex("btn-secondary"));
    }
}
