using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class SystemStatusHealthFlowTests
{
    private readonly E2EFixture fixture;

    public SystemStatusHealthFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>Locates a Sub-services row by its bolded service name (a &lt;strong&gt; tag,
    /// only used in that table) rather than page-wide text, since the "Health" messages table
    /// above it can also mention a service name in plain text (e.g. "No Jellyfin libraries are
    /// selected...") and would otherwise collide.</summary>
    private static ILocator SubServiceRow(IPage page, string serviceName) =>
        page.Locator(".health-row", new() { Has = page.Locator("strong", new() { HasText = serviceName }) });

    [Fact]
    public async Task SubServiceHealth_ReflectsFakeServersGoingDownAfterRefresh()
    {
        // Prowlarr isn't env-configured for this suite (see E2EFixture) — seed its DB-backed
        // settings explicitly rather than relying on whatever another test left behind in this
        // shared single-row table.
        await DbSeeding.SeedProwlarrSettingsAsync(fixture.App.Services, fixture.FakeProwlarr.Address, FakeProwlarrServer.ApiKey);
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/system/status");

        await Expect(SubServiceRow(page, "javinizer-go").Locator(".health-icon-ok")).ToBeVisibleAsync();
        await Expect(SubServiceRow(page, "Prowlarr").Locator(".health-icon-ok")).ToBeVisibleAsync();
        await Expect(SubServiceRow(page, "qBittorrent").Locator(".health-icon-ok")).ToBeVisibleAsync();
        await Expect(SubServiceRow(page, "Jellyfin").Locator(".health-icon-ok")).ToBeVisibleAsync();

        await Expect(SubServiceRow(page, "javinizer-go")).ToContainTextAsync($"({FakeJavinizerServer.Version})");
        await Expect(SubServiceRow(page, "Prowlarr")).ToContainTextAsync($"({FakeProwlarrServer.Version})");
        await Expect(SubServiceRow(page, "qBittorrent")).ToContainTextAsync($"({FakeQBittorrentServer.Version})");
        await Expect(SubServiceRow(page, "Jellyfin")).ToContainTextAsync($"({FakeJellyfinServer.Version})");

        var javinizerLink = SubServiceRow(page, "javinizer-go").GetByRole(AriaRole.Link, new() { Name = "javinizer-go" });
        await Expect(javinizerLink).ToHaveAttributeAsync("href", fixture.FakeJavinizer.Address.TrimEnd('/'));
        await Expect(SubServiceRow(page, "javinizer-go")).Not.ToContainTextAsync("Connected successfully.");

        fixture.FakeProwlarr.ConnectionSucceeds = false;
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();

        await Expect(SubServiceRow(page, "Prowlarr").Locator(".health-icon-warning")).ToBeVisibleAsync();
        await Expect(SubServiceRow(page, "Prowlarr")).ToContainTextAsync("API key was rejected");
        // The other three stay healthy — confirms the page reflects per-service state, not a
        // single global "something is down" flag.
        await Expect(SubServiceRow(page, "javinizer-go").Locator(".health-icon-ok")).ToBeVisibleAsync();
    }
}
