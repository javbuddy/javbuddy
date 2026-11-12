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

    [Fact]
    public async Task DraggingATextSelectionOutOfAnInput_OntoTheBackdrop_KeepsTheDialogOpen_ButAPlainBackdropClickCloses()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create Tag", Exact = true }).First.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        var input = dialog.Locator("#new-tag-name");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync("Drag select me");

        // Press inside the input, release over the backdrop (a click lands on their common ancestor, the backdrop).
        var box = (await input.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(2, 2);
        await page.Mouse.UpAsync();

        await Expect(dialog).ToBeVisibleAsync();
        await Expect(input).ToHaveValueAsync("Drag select me");

        // A press and release on the backdrop itself still closes it.
        await page.Mouse.ClickAsync(2, 2);

        await Expect(dialog).ToBeHiddenAsync();
    }
}
