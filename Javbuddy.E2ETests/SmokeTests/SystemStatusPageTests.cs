using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>Proves the page renders and the live health-check HTTP round trip completes without
/// throwing — the specific per-service health results (healthy vs. down) get real coverage from
/// Flows/SystemStatusHealthFlowTests.cs.</summary>
[Collection(E2ECollection.Name)]
public class SystemStatusPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task StatusPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/system/status");

        await Expect(page).ToHaveTitleAsync("Status · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Health", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sub-services" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Disk Space" })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "System" })).ToHaveClassAsync(new Regex("active"));
    }
}
