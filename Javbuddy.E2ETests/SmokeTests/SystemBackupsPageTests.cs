using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class SystemBackupsPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task BackupsPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/system/backups");

        await Expect(page).ToHaveTitleAsync("Backups · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Automatic Backups" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Backup Now" })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "System" })).ToHaveClassAsync(new Regex("active"));
    }
}
