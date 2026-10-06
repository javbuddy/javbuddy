using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The app's central Sonarr/Radarr-style loop: search Prowlarr from a movie's page,
/// grab a release, and see it land in the Activity queue.</summary>
[Collection(E2ECollection.Name)]
public class SearchAndGrabFlowTests
{
    private readonly E2EFixture fixture;

    public SearchAndGrabFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task SearchGrabAndAppearInQueue()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-GRAB-1");
        await DbSeeding.SeedProwlarrSettingsAsync(fixture.App.Services, fixture.FakeProwlarr.Address, FakeProwlarrServer.ApiKey);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search Prowlarr" }).ClickAsync();

        var modal = page.Locator(".prowlarr-modal-panel");
        await Expect(modal.GetByText("Test Release 1080p")).ToBeVisibleAsync();

        await modal.GetByRole(AriaRole.Button, new() { Name = "Grab" }).ClickAsync();

        await Expect(modal.Locator(".alert-success", new() { HasText = "Grabbed \"Test Release 1080p\"." })).ToBeVisibleAsync();
        Assert.Contains(fixture.FakeQBittorrent.AddedTorrents, t => t.Urls?.StartsWith("magnet:") == true);

        await page.GotoInteractiveAsync("/activity/queue");
        var row = page.Locator(".activity-row-queue", new() { Has = page.GetByRole(AriaRole.Link, new() { Name = movie.Code }) });
        await Expect(row).ToContainTextAsync("Test Release 1080p");
    }
}
