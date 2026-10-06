using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>Smoke coverage only — the wizard's Scan/Review/Organize steps drive javinizer-go's
/// batch endpoints, which the fake javinizer server doesn't implement yet (see the E2E test
/// plan's "explicitly out of scope" section). This just proves the page loads for a real torrent
/// row and its first step (Scan) renders.</summary>
[Collection(E2ECollection.Name)]
public class TorrentSortPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task ScanStep_RendersWithActiveNavLink()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SORT-1");
        var download = await DbSeeding.SeedTorrentDownloadAsync(fixture.DbFactory, movie);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/activity/sort/{download.Id}");

        await Expect(page).ToHaveTitleAsync($"Sort {movie.Code} · Javbuddy");
        await Expect(page.Locator("h1.sort-title")).ToContainTextAsync(movie.Code!);

        // The seeded torrent has no real folder on disk, so if ffmpeg is available the wizard
        // opens on the Merge step first, letting the user type an override path. Skip past it
        // to reach Scan, which is what this test actually covers.
        var skipMergeButton = page.GetByRole(AriaRole.Button, new() { Name = "Skip Merge" });
        if (await skipMergeButton.IsVisibleAsync())
        {
            await skipMergeButton.ClickAsync();
        }

        await Expect(page.GetByPlaceholder("e.g. /scratch/torrent/javinizarr/SNOS-100")).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Activity" })).ToHaveClassAsync(new Regex("active"));
    }
}
