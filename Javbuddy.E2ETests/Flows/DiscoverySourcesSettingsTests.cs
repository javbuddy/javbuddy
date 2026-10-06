using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class DiscoverySourcesSettingsTests(E2EFixture fixture)
{
    [Fact]
    public async Task MetadataSettings_ShowsDiscoveryAndSourcesSections_AndSavedSourceTogglesPersist()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/settings/metadata");

        await Expect(page.Locator("h2.metadata-section-title")).ToHaveTextAsync(["Movies", "Actors", "Discovery", "Sources"]);
        var sources = page.Locator("section.metadata-section", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Sources" }) });
        await Expect(sources.Locator(".status-tile-title", new() { HasText = "r18.dev" })).ToBeVisibleAsync();

        var tile = page.Locator(".status-tile", new() { HasText = "Studio sources" });
        await Expect(tile).ToContainTextAsync("4 of 4 enabled");
        await tile.ClickAsync();
        await page.GetByLabel("Moodyz").UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).Last.ClickAsync();
        await Expect(tile).ToContainTextAsync("3 of 4 enabled");

        await page.ReloadAsync();
        await Expect(page.Locator(".status-tile", new() { HasText = "Studio sources" })).ToContainTextAsync("3 of 4 enabled");

        // restore, so the shared fixture's later tests see every source enabled
        await page.Locator(".status-tile", new() { HasText = "Studio sources" }).ClickAsync();
        await page.GetByLabel("Moodyz").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).Last.ClickAsync();
        await Expect(page.Locator(".status-tile", new() { HasText = "Studio sources" })).ToContainTextAsync("4 of 4 enabled");
    }
}
