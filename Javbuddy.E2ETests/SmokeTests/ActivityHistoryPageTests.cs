using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class ActivityHistoryPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task HistoryPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/activity/history");

        await Expect(page).ToHaveTitleAsync("History · Javbuddy");
        await Expect(page.Locator("h1.activity-title")).ToHaveTextAsync("History");
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Activity" })).ToHaveClassAsync(new Regex("active"));
    }
}
