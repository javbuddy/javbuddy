using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class MissingPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task MissingPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/missing");

        await Expect(page).ToHaveTitleAsync("Missing · Javbuddy");
        await Expect(page.Locator("h1.missing-title")).ToHaveTextAsync("Missing");
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Missing" })).ToHaveClassAsync(new Regex("active"));
    }

    /// <summary>Regression test: the Missing page's twin of the Actors page's sort bug: flipping the
    /// Release Date sort, then navigating away and back in-app, used to revert to the default
    /// (descending). The circuit's second OnInitializedAsync pass read the cookie from the
    /// HttpContext captured when the circuit first connected, which never sees a cookie written
    /// since; a real browser/SignalR circuit is needed to reproduce it.</summary>
    [Fact]
    public async Task MissingSort_Persists_AfterNavigatingAwayAndBack()
    {
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-MISSORT-001", MovieStatus.Missing);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/missing");
        var arrow = page.Locator(".missing-sort-arrow");
        await Expect(arrow).ToHaveTextAsync("▼");

        await page.Locator(".missing-sortable").ClickAsync();
        await Expect(arrow).ToHaveTextAsync("▲");
        await page.WaitForFunctionAsync("document.cookie.includes('javbuddy-missing-view')");

        await page.Locator("nav.nav a.nav-link", new() { HasText = "Actors" }).First.ClickAsync();
        await Expect(page).ToHaveTitleAsync("Actors · Javbuddy");
        await page.Locator("nav.nav a.nav-link", new() { HasText = "Missing" }).First.ClickAsync();
        await Expect(page).ToHaveTitleAsync("Missing · Javbuddy");

        await Expect(page.Locator(".missing-row-item").First).ToBeVisibleAsync();
        await Expect(arrow).ToHaveTextAsync("▲");
    }
}
