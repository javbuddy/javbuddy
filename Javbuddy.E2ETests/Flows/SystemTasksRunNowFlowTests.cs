using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class SystemTasksRunNowFlowTests
{
    private readonly E2EFixture fixture;

    public SystemTasksRunNowFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>Uses "Jellyfin Link Sync" specifically — it only talks to the fake Jellyfin
    /// server (already configured healthy), unlike "r18.dev Dump Import" which would hit the
    /// real internet if some other test in this shared-DB collection left R18DevSettings.Enabled
    /// on without a DumpSourceOverride.</summary>
    [Fact]
    public async Task RunNow_QueuesAndCompletesTheTask()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/system/tasks");

        var scheduledRow = page.Locator(".tasks-row-scheduled", new() { HasText = "Jellyfin Link Sync" });
        await scheduledRow.GetByRole(AriaRole.Button, new() { Name = "Run now" }).ClickAsync();

        await Expect(page.GetByText("Jellyfin Link Sync queued.")).ToBeVisibleAsync();

        var queueRow = page.Locator(".tasks-row-queue", new() { HasText = "Jellyfin Link Sync" });
        await Expect(queueRow.Locator(".tasks-icon-ok")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
