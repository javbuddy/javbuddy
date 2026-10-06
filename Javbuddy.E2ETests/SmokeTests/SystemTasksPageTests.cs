using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class SystemTasksPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task TasksPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/system/tasks");

        await Expect(page).ToHaveTitleAsync("Tasks · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Scheduled" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Queue", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "System" })).ToHaveClassAsync(new Regex("active"));
    }
}
