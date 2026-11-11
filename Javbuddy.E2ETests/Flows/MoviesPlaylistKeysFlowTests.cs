using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The Movies page's Play all in fullscreen: N and P still go to the next / previous movie. The
/// player's keys are picked out in the browser (VideoPlayerShell.razor.js) on the backdrop, and entering
/// fullscreen moves focus into the fullscreen column, inside it. The keys are pressed on the page, not on
/// the backdrop, so the focus the fullscreen handling set up is what carries them.</summary>
[Collection(E2ECollection.Name)]
public class MoviesPlaylistKeysFlowTests
{
    private readonly E2EFixture fixture;

    public MoviesPlaylistKeysFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    private async Task SeedAsync(string code)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        await DbSeeding.SeedLocalTrickplayAsync(fixture.DbFactory, movie);
    }

    [Fact]
    public async Task NextAndPrevious_WorkWhileFullscreen_AndEscapeStillCloses()
    {
        await SeedAsync("E2E-PLKEY-1");
        await SeedAsync("E2E-PLKEY-2");
        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync("/");
            await page.Locator("main input[type=search]").First.FillAsync("E2E-PLKEY");
            await Expect(page.Locator(".poster-card")).ToHaveCountAsync(2);
            await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Play all" }).ClickAsync();
            var code = page.Locator(".video-player-modal-code");
            await Expect(code).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.scrub-bar-thumb')?.style.width !== ''");
            var first = (await code.TextContentAsync())!;
            await page.WaitForSelectorAsync(".video-player-modal-backdrop[data-keys-bound]", new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });

            await page.Keyboard.PressAsync("f");
            await page.WaitForFunctionAsync("() => document.fullscreenElement?.classList.contains('video-player-column')");
            await page.Keyboard.PressAsync("n");
            await Expect(code).Not.ToHaveTextAsync(first);

            await page.WaitForFunctionAsync("() => document.fullscreenElement !== null");
            await page.Keyboard.PressAsync("p");
            await Expect(code).ToHaveTextAsync(first);

            // In fullscreen Esc only leaves it; the next one closes the player.
            await page.Keyboard.PressAsync("Escape");
            await page.Keyboard.PressAsync("Escape");
            await Expect(page.Locator(".video-player-modal-backdrop")).ToHaveCountAsync(0);
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
