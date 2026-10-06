using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class ActivityQueuePageTests(E2EFixture fixture)
{
    /// <summary>Seeds its own row and asserts on that row specifically (rather than asserting an
    /// empty queue) because the DB is shared across the whole "E2E" collection — other test
    /// classes' seeded torrents/movies coexist here, so "the queue is empty" isn't a safe
    /// assumption for this test alone.</summary>
    [Fact]
    public async Task Queue_RendersSeededRowWithActiveNavLink()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-QUEUE-1");
        await DbSeeding.SeedTorrentDownloadAsync(fixture.DbFactory, movie);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/activity/queue");

        await Expect(page).ToHaveTitleAsync("Queue · Javbuddy");
        await Expect(page.Locator("h1.activity-title")).ToHaveTextAsync("Queue");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = movie.Code })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Activity" })).ToHaveClassAsync(new Regex("active"));
    }
}
