using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActivityHistoryPagingTests(E2EFixture fixture)
{
    [Fact]
    public async Task History_PagesThroughEveryEntry_OldestOnesReachableOnTheLastPage()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-PAGING-1");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var oldest = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 1; i <= 120; i++)
            {
                db.TorrentDownloads.Add(new TorrentDownload
                {
                    MovieId = movie.Id,
                    MovieCode = movie.Code!,
                    ReleaseTitle = $"E2E-PAGING-{i:000} Release",
                    Status = TorrentDownloadStatus.Completed,
                    GrabbedAt = oldest.AddMinutes(i),
                    RemovedFromClientAt = oldest.AddMinutes(i),
                });
            }
            await db.SaveChangesAsync();
        }
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/activity/history");
        var rows = page.Locator(".activity-row-history:not(.activity-header)");
        await Expect(page.Locator(".activity-pager-status")).ToContainTextAsync("Page 1 of ");
        await Expect(rows).ToHaveCountAsync(50);
        await Expect(page.GetByText("E2E-PAGING-001 Release")).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Link, new() { Name = "Older ›" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/activity/history\?page=2$"));
        await Expect(page.Locator(".activity-pager-status")).ToContainTextAsync("Page 2 of ");

        await page.GotoInteractiveAsync("/activity/history?page=1000");
        await Expect(page.Locator(".activity-pager-status")).ToContainTextAsync(new Regex(@"Page (\d+) of \1 "));
        await Expect(page.GetByText("E2E-PAGING-001 Release")).ToBeVisibleAsync();
        await Expect(page.Locator("span.activity-pager-disabled", new() { HasText = "Older ›" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "‹ Newer" }).ClickAsync();
        await Expect(page.GetByText("E2E-PAGING-001 Release")).ToHaveCountAsync(0);
    }
}
