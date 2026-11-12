using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>ModalShell focuses itself when it opens, so Escape closes a dialog without the user having to
/// click or tab into it first. Focus only exists in a real browser; bUnit can only check that the focus call
/// is made, not that it lands.</summary>
[Collection(E2ECollection.Name)]
public class ModalEscapeFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task Escape_ClosesPosterOptions_WithoutClickingIntoIt()
    {
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ESC-001");
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Options", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        // Focus lands a moment after the dialog renders (a JS call), so wait for it rather than racing it.
        await Expect(page.Locator(".modal-shell-backdrop")).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Escape");

        await Expect(dialog).ToBeHiddenAsync();
    }
}
