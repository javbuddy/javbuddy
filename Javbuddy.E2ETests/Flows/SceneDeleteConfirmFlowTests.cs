using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The scene editor's delete confirmation. Focus, Esc and the Review
/// shortcuts' document-level key handling only exist in a real browser, and the dialog has to sit
/// above the player modal it opens from — none of which bUnit can check.</summary>
[Collection(E2ECollection.Name)]
public class SceneDeleteConfirmFlowTests
{
    private readonly E2EFixture fixture;

    public SceneDeleteConfirmFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task PlayerSidePanel_EscapeAndBackdropCancelWithoutClosingThePlayer_DeleteRemovesTheScene()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SCNDEL-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = await db.Movies.FindAsync(movie.Id);
            m!.JellyfinItemId = "fake-jf-item-scndel-1";
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieSceneService>().AddSceneAsync(movie.Id, 60, null, "Interview");
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        var player = page.Locator(".video-player-modal-backdrop");
        var panel = page.Locator(".video-player-side-panel");
        var dialog = page.Locator(".delete-confirm-dialog");
        await Expect(panel.Locator(".scene-editor-row")).ToHaveCountAsync(1);

        // Esc cancels the dialog only; the player modal around it stays open.
        await panel.Locator(".scene-editor-delete-btn").ClickAsync();
        await Expect(dialog.Locator("h2")).ToHaveTextAsync("Delete \"Interview\"?");
        await Expect(page.Locator(".delete-confirm-cancel-btn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(player).ToBeVisibleAsync();

        // So does a click on the dialog's backdrop.
        await panel.Locator(".scene-editor-delete-btn").ClickAsync();
        await page.Locator(".delete-confirm-backdrop").ClickAsync(new() { Position = new() { X = 5, Y = 5 } });
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(player).ToBeVisibleAsync();
        await Expect(panel.Locator(".scene-editor-row")).ToHaveCountAsync(1);

        // A real click (hit-tested, so the dialog must be on top of the player) deletes it.
        await panel.Locator(".scene-editor-delete-btn").ClickAsync();
        await page.Locator(".delete-confirm-confirm-btn").ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(panel.Locator(".scene-editor-empty")).ToBeVisibleAsync();
        await Expect(player).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ReviewMode_ShortcutsAreIgnoredWhileTheDialogIsOpen()
    {
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Scene Delete Confirm", isFavorite: true);
        var prefix = "E2E-SCNDEL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var first = await DbSeeding.SeedMovieAsync(fixture.DbFactory, prefix + "-1", MovieStatus.Got);
        var second = await DbSeeding.SeedMovieAsync(fixture.DbFactory, prefix + "-2", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == first.Id || m.Id == second.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.JellyfinItemId, m => "scndel-item-" + m.Id));
            db.MovieActors.AddRange(
                new MovieActor { MovieId = first.Id, ActorId = actor.Id },
                new MovieActor { MovieId = second.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, first);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, second);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var scenes = scope.ServiceProvider.GetRequiredService<IMovieSceneService>();
            await scenes.AddSceneAsync(first.Id, 1, null, null);
            await scenes.AddSceneAsync(second.Id, 1, null, null);
        }
        fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>().ClearSession();

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/review?actorId={actor.Id}");
        await Expect(page.Locator(".scene-editor-row")).ToHaveCountAsync(1);
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.cleanup-video'); return v && v.readyState >= 2 && !v.error; }");
        var code = (await page.Locator(".cleanup-code").TextContentAsync())!;

        await page.Locator(".scene-editor-delete-btn").ClickAsync();
        await Expect(page.Locator(".delete-confirm-dialog")).ToBeVisibleAsync();

        // N would move to the next movie, and M, H and A would add a scene, start a highlight or
        // mark an apex behind the dialog.
        await page.Keyboard.PressAsync("n");
        await page.Keyboard.PressAsync("m");
        await page.Keyboard.PressAsync("h");
        await page.Keyboard.PressAsync("a");
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);
        await Expect(page.Locator(".scene-editor-row")).ToHaveCountAsync(1);
        await Expect(page.Locator(".highlight-editor-form")).ToHaveCountAsync(0);
        await Expect(page.Locator(".apex-editor-row")).ToHaveCountAsync(0);

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(".delete-confirm-dialog")).ToHaveCountAsync(0);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }
}
