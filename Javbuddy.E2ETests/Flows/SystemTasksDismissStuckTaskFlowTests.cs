using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.EntityFrameworkCore;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class SystemTasksDismissStuckTaskFlowTests
{
    private readonly E2EFixture fixture;

    public SystemTasksDismissStuckTaskFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task StuckTask_ShowsDismissButton_ClickingMarksFailedAndClearsSidebar()
    {
        var run = await DbSeeding.SeedScheduledTaskRunAsync(
            fixture.DbFactory,
            taskName: "Image Cache",
            startedAt: DateTime.UtcNow.AddMinutes(-10),
            endedAt: null,
            progressStage: "Scanning",
            progressCurrent: 42,
            progressTotal: 100);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync("/system/tasks");

            // Sidebar indicator should be visible for the active stuck task
            var sidebarIndicator = page.Locator(".active-task-status");
            await Expect(sidebarIndicator).ToBeVisibleAsync();
            await Expect(sidebarIndicator).ToContainTextAsync("Image Cache");

            // In the Queue table, the stuck task has a running icon and the dismiss button
            var queueRow = page.Locator(".tasks-row-queue", new() { HasText = "Image Cache" }).First;
            await Expect(queueRow.Locator(".tasks-icon-running")).ToBeVisibleAsync();

            var dismissButton = queueRow.Locator(".tasks-dismiss-btn");
            await Expect(dismissButton).ToBeVisibleAsync();
            await Expect(dismissButton).ToHaveAttributeAsync("title", "Mark as Failed");

            // Click the dismiss button
            await dismissButton.ClickAsync();

            // After dismiss, the dismiss button is gone and the icon transitions to warning
            await Expect(dismissButton).ToBeHiddenAsync();
            await Expect(queueRow.Locator(".tasks-icon-warning")).ToBeVisibleAsync();

            // The sidebar active-task status line clears
            await Expect(sidebarIndicator).ToBeHiddenAsync();

            // No Blazor unhandled circuit errors
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

            // In the database, verify the run record is marked as failed
            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            var updatedRun = await db.ScheduledTaskRuns.SingleAsync(r => r.Id == run.Id);
            Assert.NotNull(updatedRun.EndedAt);
            Assert.False(updatedRun.Success);
            Assert.Equal("Manually marked as failed", updatedRun.ErrorMessage);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
