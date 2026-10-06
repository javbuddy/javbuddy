using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Drives the Prowlarr connection form specifically — it's the one integration this
/// suite deliberately leaves un-env-configured (see E2EFixture) so its Settings &gt; Connections
/// form stays editable rather than locked behind the "configured via environment variables"
/// banner every other integration shows here. Since, the form lives inside a drawer
/// opened by clicking the Prowlarr tile — not an always-expanded card.</summary>
[Collection(E2ECollection.Name)]
public class SettingsConnectionsFlowTests
{
    private readonly E2EFixture fixture;

    public SettingsConnectionsFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    private static ILocator ProwlarrTile(IPage page) =>
        page.Locator(".status-tile", new() { HasText = "Prowlarr" });

    private static ILocator Drawer(IPage page) => page.Locator(".slide-over-drawer-panel");

    private static async Task<ILocator> OpenProwlarrDrawerAsync(IPage page)
    {
        await ProwlarrTile(page).ClickAsync();
        var drawer = Drawer(page);
        await Expect(drawer).ToBeVisibleAsync();
        return drawer;
    }

    [Fact]
    public async Task TestConnection_SavesAndPersistsAcrossReload()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/settings/connections");
        var drawer = await OpenProwlarrDrawerAsync(page);

        await drawer.GetByPlaceholder("http://localhost:9696").FillAsync(fixture.FakeProwlarr.Address);
        await drawer.Locator("input[type=password]").FillAsync(FakeProwlarrServer.ApiKey);
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Test connection" }).ClickAsync();

        var connectedAlert = drawer.Locator(".alert-success", new() { HasText = "Connected successfully." });
        await Expect(connectedAlert).ToBeVisibleAsync();
        await Expect(connectedAlert).ToContainTextAsync($"Server version: {FakeProwlarrServer.Version}");
        await Expect(drawer.Locator(".alert-success", new() { HasText = "Saved." })).ToBeVisibleAsync();

        await page.ReloadAsync();
        drawer = await OpenProwlarrDrawerAsync(page);
        await Expect(drawer.GetByPlaceholder("http://localhost:9696")).ToHaveValueAsync(fixture.FakeProwlarr.Address);
    }

    [Fact]
    public async Task TestConnection_UnreachableServer_ShowsFailure()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/settings/connections");
        var drawer = await OpenProwlarrDrawerAsync(page);

        // Port 1 is reserved and essentially guaranteed not to be listening — a real
        // "could not reach the server" failure, distinct from a rejected-credentials failure.
        await drawer.GetByPlaceholder("http://localhost:9696").FillAsync("http://127.0.0.1:1");
        await drawer.Locator("input[type=password]").FillAsync("irrelevant-key");
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Test connection" }).ClickAsync();

        await Expect(drawer.Locator(".alert-danger", new() { HasText = "Could not reach Prowlarr" })).ToBeVisibleAsync();
    }

    private static ILocator JellyfinTile(IPage page) =>
        page.Locator(".status-tile", new() { HasText = "Jellyfin" });

    private static async Task<ILocator> OpenJellyfinDrawerAsync(IPage page)
    {
        await JellyfinTile(page).ClickAsync();
        var drawer = Drawer(page);
        await Expect(drawer).ToBeVisibleAsync();
        return drawer;
    }

    [Fact]
    public async Task TestConnection_Jellyfin_DisplaysVersionAndServerId()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/settings/connections");
        var drawer = await OpenJellyfinDrawerAsync(page);

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Test connection" }).ClickAsync();

        var alert = drawer.Locator(".alert-success", new() { HasText = "Connected successfully." });
        await Expect(alert).ToBeVisibleAsync();
        await Expect(alert).ToContainTextAsync($"Server version: {FakeJellyfinServer.Version}");
        await Expect(alert).ToContainTextAsync($"Server ID: {FakeJellyfinServer.ServerId}");
    }

    private static ILocator JavinizerTile(IPage page) =>
        page.Locator(".status-tile").Filter(new() { Has = page.Locator(".status-tile-title", new() { HasText = "javinizer-go" }) });

    private static async Task<ILocator> OpenJavinizerDrawerAsync(IPage page)
    {
        await JavinizerTile(page).ClickAsync();
        var drawer = Drawer(page);
        await Expect(drawer).ToBeVisibleAsync();
        return drawer;
    }

    [Fact]
    public async Task TestConnection_Javinizer_DisplaysVersion()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/settings/connections");
        var drawer = await OpenJavinizerDrawerAsync(page);

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Test connection" }).ClickAsync();

        var alert = drawer.Locator(".alert-success", new() { HasText = "Connected successfully." });
        await Expect(alert).ToBeVisibleAsync();
        await Expect(alert).ToContainTextAsync($"Server version: {FakeJavinizerServer.Version}");
    }
}

