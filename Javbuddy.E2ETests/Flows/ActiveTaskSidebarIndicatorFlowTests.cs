using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActiveTaskSidebarIndicatorFlowTests
{
    private readonly E2EFixture fixture;

    public ActiveTaskSidebarIndicatorFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>Holds Jellyfin Link Sync's per-item lookups on FakeJellyfinServer.ItemLookupGate
    /// so the run's ScheduledTaskRun.EndedAt stays null for as long as the assertions need — an
    /// instant/empty run would finish before any assertion could catch it. The one
    /// seeded movie guarantees at least one lookup to hold. This replaces seeding 600 movies to keep
    /// the run busy, which depended on machine speed and left 600 more Got movies in the shared
    /// fixture DB for every later test.</summary>
    [Fact]
    public async Task RunningTask_ShowsInSidebarOnAnyPage_AndClearsWhenDone()
    {
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SIDEBAR-TASK");
        var lookupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FakeJellyfin.ItemLookupGate = lookupGate.Task;

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync("/system/tasks");
            var scheduledRow = page.Locator(".tasks-row-scheduled", new() { HasText = "Jellyfin Link Sync" });
            await scheduledRow.GetByRole(AriaRole.Button, new() { Name = "Run now" }).ClickAsync();

            // A different page than the one that triggered the run — proves this is a sidebar-wide
            // status (kept live across enhanced navigation via ScheduledTaskChangeNotifier), not
            // something scoped to System > Tasks' own polling.
            await page.GetByRole(AriaRole.Link, new() { Name = "Movies", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();
            var indicator = page.Locator(".active-task-status");
            await Expect(indicator).ToBeVisibleAsync();
            await Expect(indicator).ToContainTextAsync("Jellyfin Link Sync");

            // Check real CSS geometry: progress must touch the collapse button at the bottom,
            // including when the viewport is short enough to make navigation scroll.
            foreach (var height in new[] { 900, 400 })
            {
                await page.SetViewportSizeAsync(1280, height);
                var statusBounds = await indicator.BoundingBoxAsync();
                var buttonBounds = await page.Locator(".sidebar-collapse-toggle").BoundingBoxAsync();
                Assert.NotNull(statusBounds);
                Assert.NotNull(buttonBounds);
                Assert.InRange(Math.Abs(statusBounds.Y + statusBounds.Height - buttonBounds.Y), 0, 1);
                Assert.InRange(Math.Abs(buttonBounds.Y + buttonBounds.Height - height), 0, 1);
            }

            await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "javbuddy-115-sidebar.png") });
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

            lookupGate.SetResult();
            await Expect(indicator).ToBeHiddenAsync(new() { Timeout = 30_000 });
        }
        finally
        {
            // Released on failure too, so a held lookup can't keep the run open into later tests.
            lookupGate.TrySetResult();
            await page.CloseAsync();
        }
    }
}
