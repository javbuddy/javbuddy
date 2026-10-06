using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Services.QBittorrent;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActivityQueueInteractionTests
{
    private readonly E2EFixture fixture;

    public ActivityQueueInteractionTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task RemoveSelected_DeletesFromQBittorrentAndDropsRowFromQueue()
    {
        const string hash = "e2e-remove-hash-1";
        // qBittorrent has to report it before it's tracked: the app's background qBittorrent sync marks
        // a tracked hash it doesn't list as removed, which dropped the row whenever a sync landed first.
        fixture.FakeQBittorrent.Torrents.Add(new QBittorrentTorrentDto { Hash = hash, Name = "E2E-REMOVE-1", State = "downloading", Tags = "E2E-REMOVE-1" });
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-REMOVE-1");
        await DbSeeding.SeedTorrentDownloadAsync(fixture.DbFactory, movie, hash: hash);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/activity/queue");
        var row = page.Locator(".activity-row-queue", new() { Has = page.GetByRole(AriaRole.Link, new() { Name = movie.Code }) });
        await Expect(row).ToBeVisibleAsync();

        await row.Locator("input[type=checkbox]").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Selected" }).ClickAsync();

        await Expect(row).ToBeHiddenAsync();
        Assert.Contains(hash, fixture.FakeQBittorrent.DeletedHashes);
    }
}
