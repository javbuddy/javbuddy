using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class MovieCleanupPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task CleanupPage_RendersUnderTheActiveMoviesSection_WithoutItsOwnNavSubLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/movies/cleanup");

        await Expect(page).ToHaveTitleAsync("Cleanup · Javbuddy");
        await Expect(page.Locator("h1.cleanup-page-title")).ToHaveTextAsync("Cleanup");
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Movies" })).ToHaveClassAsync(new Regex("active"));
        await Expect(page.Locator("nav.nav a.nav-sublink", new() { HasText = "Cleanup" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ReviewPage_RendersWithActiveNavSubLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/movies/review");

        await Expect(page).ToHaveTitleAsync("Review · Javbuddy");
        await Expect(page.Locator("h1.cleanup-page-title")).ToHaveTextAsync("Review");
        await Expect(page.Locator("nav.nav a.nav-sublink", new() { HasText = "Review" })).ToHaveClassAsync(new Regex("active"));
    }
}
