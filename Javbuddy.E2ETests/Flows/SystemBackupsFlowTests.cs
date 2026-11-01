using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class SystemBackupsFlowTests(E2EFixture fixture)
{
    /// <summary>Backs up the real (throwaway) database in the browser, then downloads the archive the Export link
    /// points at: it has to be a zip holding the database and its manifest.</summary>
    [Fact]
    public async Task BackupNow_ListsTheBackup_AndExportDownloadsAZip()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/system/backups");

        await page.GetByRole(AriaRole.Button, new() { Name = "Backup Now" }).ClickAsync();

        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Backup created");
        var row = page.Locator(".backups-row").First;
        await Expect(row).ToContainTextAsync("Manual");
        await row.Locator("input[type=checkbox]").CheckAsync();
        var href = await page.Locator("#backup-export").GetAttributeAsync("href");

        // The page's <base href="/"> is what makes the relative link resolve to the site root.
        var response = await page.APIRequest.GetAsync(new Uri(new Uri(page.Url), "/" + href).ToString());
        Assert.True(response.Ok, $"{response.Status} for {href}");
        Assert.Equal("application/zip", response.Headers["content-type"]);
        var body = await response.BodyAsync();
        Assert.Equal((byte)'P', body[0]);
        Assert.Equal((byte)'K', body[1]);
    }
}
