using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>Smoke coverage only — the actual discovery-from-disk flow gets real coverage from
/// Flows/LibraryImportFlowTests.cs against on-disk fixture folders.</summary>
[Collection(E2ECollection.Name)]
public class LibraryImportPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task ImportPage_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/add/import");

        await Expect(page).ToHaveTitleAsync("Library Import · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Import movies you already have" })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Movies" })).ToHaveClassAsync(new Regex("active"));
    }
}
