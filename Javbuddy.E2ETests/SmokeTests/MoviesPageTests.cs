using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class MoviesPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task HomePage_RendersMoviesGridWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/");

        await Expect(page).ToHaveTitleAsync("Movies · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();

        // Confirms the interactive SignalR circuit actually came up (NavMenu.razor's active-link
        // state is computed client-side from NavigationManager, not present in the static HTML).
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Movies" })).ToHaveClassAsync(new Regex("active"));
    }
}
