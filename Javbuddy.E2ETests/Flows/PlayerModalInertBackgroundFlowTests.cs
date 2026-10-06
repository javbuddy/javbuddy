using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The page behind the player modal is inert while it's open. Tab order,
/// focus and the inert attribute only exist in a real browser — bUnit can't check them.</summary>
[Collection(E2ECollection.Name)]
public class PlayerModalInertBackgroundFlowTests
{
    private readonly E2EFixture fixture;

    public PlayerModalInertBackgroundFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    // Where focus is: null inside the player (or nowhere on the page, e.g. the browser's own UI),
    // otherwise a description of the page element that has it.
    private const string FocusOutsidePlayerJs = """
        () => {
            const active = document.activeElement;
            if (!active || active === document.body) return null;
            if (active.closest('.video-player-modal-backdrop')) return null;
            return active.tagName + '.' + active.className;
        }
        """;

    [Fact]
    public async Task SceneEditor_TabAndShiftTabStayInThePlayer_AndThePageComesBackOnClose()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-INERT-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        var play = page.Locator("button.movie-detail-badge-play");
        await Expect(play).ToBeVisibleAsync();
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        var player = page.Locator(".video-player-modal-backdrop");
        await Expect(page.Locator(".video-player-side-panel")).ToBeVisibleAsync();
        await Expect(page.Locator(".app-header")).ToHaveAttributeAsync("inert", "");

        // More presses than the player has stops, so focus would have reached the page behind it.
        for (var i = 0; i < 80; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.Null(await page.EvaluateAsync<string?>(FocusOutsidePlayerJs));
        }
        await player.FocusAsync();
        for (var i = 0; i < 10; i++)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            Assert.Null(await page.EvaluateAsync<string?>(FocusOutsidePlayerJs));
        }

        // Even focused from script, the page's Play button can't take focus to be pressed.
        await play.EvaluateAsync("b => b.focus()");
        await Expect(play).Not.ToBeFocusedAsync();

        await player.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(player).ToHaveCountAsync(0);
        await Expect(page.Locator("[inert]")).ToHaveCountAsync(0);
        await play.FocusAsync();
        await Expect(play).ToBeFocusedAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    // Edit in the clip player swaps it for the editor's player in one render: the clip player's
    // InertBackground goes as the editor's comes, and the page behind must stay inert.
    [Fact]
    public async Task ClipPlayerEdit_LeavesThePageBehindTheEditorInert()
    {
        var movie = await SeedMovieWithSceneAsync("E2E-INERT-2");

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator(".movie-scenes-card button.movie-scenes-play").ClickAsync();
        await page.Locator(".clip-player-edit-btn").ClickAsync();
        await Expect(page.Locator(".video-player-side-panel")).ToBeVisibleAsync();
        await Expect(page.Locator(".video-player-modal-backdrop")).ToHaveCountAsync(1);

        // Long enough for the clip player's teardown to have landed.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator(".app-header")).ToHaveAttributeAsync("inert", "");
        var sceneCard = page.Locator(".movie-scenes-card button.movie-scenes-play");
        await sceneCard.EvaluateAsync("b => b.focus()");
        await Expect(sceneCard).Not.ToBeFocusedAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    // Space plays and pauses the editor's player even after a click on one of its buttons, rather than
    // pressing that button again.
    [Fact]
    public async Task SceneEditor_SpaceAfterClickingAnEditorButton_TogglesPlaybackNotTheButton()
    {
        var movie = await SeedMovieWithSceneAsync("E2E-INERT-3");

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.video-player-video'); return v && v.readyState >= 2 && !v.error; }");
        var favorite = page.Locator(".video-player-side-panel .scene-editor-fav-btn").First;
        await favorite.ClickAsync();
        await Expect(favorite).ToHaveAttributeAsync("aria-pressed", "true");

        var paused = await page.EvaluateAsync<bool>("() => document.querySelector('video.video-player-video').paused");
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync($"() => document.querySelector('video.video-player-video').paused === {(!paused).ToString().ToLowerInvariant()}");
        await page.WaitForTimeoutAsync(300);
        await Expect(favorite).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    private async Task<Movie> SeedMovieWithSceneAsync(string code)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            // The fixture video is a 3-second clip.
            m.MediaDurationSeconds = 3;
            db.Scenes.Add(new Scene { MovieId = m.Id, StartSeconds = 0, EndSeconds = 3, Title = "Inert scene" });
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
            return m;
        }
    }
}
