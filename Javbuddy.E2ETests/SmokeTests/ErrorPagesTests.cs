using Javbuddy.E2ETests.Fixtures;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class ErrorPagesTests(E2EFixture fixture)
{
    [Fact]
    public async Task UnknownRoute_ReExecutesToNotFoundPage()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/this-route-does-not-exist-e2e");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Not Found" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Sorry, the content you are looking for does not exist.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ErrorPage_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/Error");

        await Expect(page).ToHaveTitleAsync("Error · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Error." })).ToBeVisibleAsync();
        await Expect(page.GetByText("Request ID:")).ToBeVisibleAsync();
    }
}
