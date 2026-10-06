using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>The Settings landing page and its three thin sub-pages, grouped in one file — the
/// Connections page's actual connection forms get real interaction coverage from
/// Flows/SettingsConnectionsFlowTests.cs; this only proves all four routes render.</summary>
[Collection(E2ECollection.Name)]
public class SettingsPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task Landing_RendersWithActiveNavLink()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/settings");

        await Expect(page).ToHaveTitleAsync("Settings · Javbuddy");
        await Expect(page.Locator(".settings-links").GetByRole(AriaRole.Link, new() { Name = "Connections" })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Settings" })).ToHaveClassAsync(new Regex("active"));
    }

    [Fact]
    public async Task Connections_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/settings/connections");

        await Expect(page).ToHaveTitleAsync("Connections · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Connections", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Metadata_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/settings/metadata");

        await Expect(page).ToHaveTitleAsync("Metadata · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Metadata", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Ui_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/settings/ui");

        await Expect(page).ToHaveTitleAsync("UI · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "UI", Exact = true })).ToBeVisibleAsync();
    }
}
