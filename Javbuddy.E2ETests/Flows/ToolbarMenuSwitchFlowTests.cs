using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The hit-testing between a toolbar menu's backdrop and a sibling menu's button is CSS, which bUnit
/// can't see, so this runs in a real browser.</summary>
[Collection(E2ECollection.Name)]
public class ToolbarMenuSwitchFlowTests
{
    private readonly E2EFixture fixture;

    public ToolbarMenuSwitchFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task ClickingAnotherToolbarMenu_SwitchesMenusInOneClick_AndOutsideClickStillCloses()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-MENU-1", MovieStatus.Got);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        var refresh = page.Locator("button.toolbar-menu-btn", new() { HasText = "Refresh" });
        var tools = page.Locator("button.toolbar-menu-btn", new() { HasText = "Tools" });

        await refresh.ClickAsync();
        await Expect(refresh).ToHaveAttributeAsync("aria-expanded", "true");

        await tools.ClickAsync();
        await Expect(tools).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(refresh).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(page.Locator(".toolbar-menu-list")).ToHaveCountAsync(1);

        await page.Mouse.ClickAsync(5, 400);
        await Expect(page.Locator(".toolbar-menu-list")).ToHaveCountAsync(0);

        await tools.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(".toolbar-menu-list")).ToHaveCountAsync(0);
    }
}
