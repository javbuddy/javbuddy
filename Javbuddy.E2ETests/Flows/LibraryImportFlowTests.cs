using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class LibraryImportFlowTests
{
    private readonly E2EFixture fixture;

    public LibraryImportFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>Points LocalLibrary:RootPaths at Fixtures/LocalLibraryFixtures (copied to the
    /// build output — see the .csproj), which contains one fixture movie folder, "E2E-IMPORT-1".</summary>
    [Fact]
    public async Task Discover_ImportsFixtureFolderAsGotMovie()
    {
        var rootPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocalLibraryFixtures");
        await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, rootPath);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/add/import");
        var runButton = page.Locator(".import-btn");
        await runButton.ClickAsync();

        // Assert the click's disabled transition before waiting for it to re-enable. Without
        // this, the enabled assertion can pass on the pre-click DOM while Blazor is still
        // dispatching the event, and navigation below races the background import.
        await Expect(runButton).ToBeDisabledAsync();
        await Expect(runButton).ToBeEnabledAsync(new() { Timeout = 30_000 });

        await page.GotoInteractiveAsync("/movies/E2E-IMPORT-1");
        await Expect(page.Locator(".movie-detail-badge-status-got")).ToBeVisibleAsync();
    }
}
