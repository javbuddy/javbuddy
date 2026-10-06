using System.Text.RegularExpressions;
using Javbuddy.Data;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Review mode's keyboard shortcuts: Space toggles the video, N clicks Next
/// (it was Right arrow before the arrows became seek keys), and ←/→ seek. Key handling, focus and default actions (page scroll, a focused button's activation,
/// the video's native controls) only exist in a real browser, so bUnit can't cover them. Each test
/// scopes the queue to its own actor's two Jellyfin-linked movies, so it doesn't depend on (or
/// stamp as reviewed) the other Got movies in the shared fixture DB.</summary>
[Collection(E2ECollection.Name)]
public class MovieCleanupReviewShortcutsFlowTests
{
    private const string VideoPausedScript = "() => document.querySelector('video.cleanup-video').paused";
    private const string VideoTimeScript = "() => document.querySelector('video.cleanup-video').currentTime";

    private readonly E2EFixture fixture;

    public MovieCleanupReviewShortcutsFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task Space_TogglesPlayback_WithoutScrollingOrDoubleToggling()
    {
        var page = await OpenReviewAsync("Shortcut Space");
        await Expect(page.Locator(".cleanup-shortcuts .shortcuts-info-popover")).ToContainTextAsync("Space");
        await page.SetViewportSizeAsync(1280, 360);

        await AssertSpaceTogglesAsync(page);
        Assert.Equal(0, await page.EvaluateAsync<double>("() => window.scrollY"));

        // The player draws its own controls, so its <video> has no native controls
        // and isn't focusable — nothing on the video toggles on Space by itself any more. Only the
        // shortcut (and the scrub bar's own Space handling, which AssertSpaceTogglesAsync's
        // double-toggle check above would catch) may act on it.

        // A focused button would otherwise be activated by Space — here Edit Tags, which would open
        // the tag search.
        await page.Locator(".cleanup-edit-tags-btn").FocusAsync();
        await AssertSpaceTogglesAsync(page);
        await Expect(page.Locator(".tag-search-input")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.EvaluateAsync<double>("() => window.scrollY"));
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task N_ClicksNext()
    {
        var page = await OpenReviewAsync("Shortcut Next");
        var firstCode = await page.Locator(".cleanup-code").TextContentAsync();

        await page.Keyboard.PressAsync("n");

        await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 1 reviewed");
        await Expect(page.Locator(".cleanup-code")).Not.ToHaveTextAsync(firstCode!);
        await Expect(page.Locator(".cleanup-next-btn")).ToHaveAttributeAsync("title", new Regex(@"\(N\)"));
    }

    [Fact]
    public async Task Arrows_SeekTheVideo_ClampedToItsLength_WithoutAdvancing()
    {
        var page = await OpenReviewAsync("Shortcut Seek");
        var code = (await page.Locator(".cleanup-code").TextContentAsync())!;
        // The fixture video is 3 s long, so every skip here is clamped to its start or end.
        await page.EvaluateAsync("() => { const v = document.querySelector('video.cleanup-video'); v.pause(); v.currentTime = 1.5; }");

        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForFunctionAsync("() => document.querySelector('video.cleanup-video').currentTime === 0");

        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.cleanup-video'); return v.currentTime === v.duration; }");

        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);
        await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 0 reviewed");
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Shortcuts_AreIgnored_WhileTypingOrWithTheCoverLightboxOpen()
    {
        var page = await OpenReviewAsync("Shortcut Ignored");
        var code = (await page.Locator(".cleanup-code").TextContentAsync())!;

        await page.Locator(".cleanup-edit-tags-btn").ClickAsync();
        var search = page.Locator(".tag-search-input");
        await search.FocusAsync();
        await page.EvaluateAsync("() => { const v = document.querySelector('video.cleanup-video'); v.pause(); v.currentTime = 1.5; }");
        var paused = await page.EvaluateAsync<bool>(VideoPausedScript);
        await page.Keyboard.PressAsync("Space");
        await page.Keyboard.PressAsync("n");
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(search).ToHaveValueAsync(" n");
        Assert.Equal(paused, await page.EvaluateAsync<bool>(VideoPausedScript));
        Assert.Equal(1.5, await page.EvaluateAsync<double>(VideoTimeScript));
        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);

        await page.Locator(".cleanup-cover").ClickAsync();
        await Expect(page.Locator(".image-lightbox-backdrop")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Space");
        await page.Keyboard.PressAsync("n");
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(paused, await page.EvaluateAsync<bool>(VideoPausedScript));
        Assert.Equal(1.5, await page.EvaluateAsync<double>(VideoTimeScript));
        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);
        await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 0 reviewed");
    }

    [Fact]
    public async Task CleanupMode_HasNoShortcuts()
    {
        var page = await OpenReviewAsync("Shortcut Cleanup", "/movies/cleanup");
        var code = (await page.Locator(".cleanup-code").TextContentAsync())!;
        await Expect(page.Locator(".cleanup-shortcuts")).ToHaveCountAsync(0);

        await page.Keyboard.PressAsync("n");

        // Give a (wrongly) triggered Skip time to round-trip before asserting nothing changed.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);
        await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 0 reviewed");
    }

    private static async Task AssertSpaceTogglesAsync(IPage page)
    {
        var paused = await page.EvaluateAsync<bool>(VideoPausedScript);
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync($"() => document.querySelector('video.cleanup-video').paused === {(!paused).ToString().ToLowerInvariant()}");
        // A second toggle (the native controls') would flip it back shortly after.
        await page.WaitForTimeoutAsync(300);
        Assert.Equal(!paused, await page.EvaluateAsync<bool>(VideoPausedScript));
    }

    /// <summary>Seeds a favorited actor linked to two playable, Jellyfin-linked Got movies and opens the page
    /// scoped to that actor, waiting until the card's video has loaded.</summary>
    private async Task<IPage> OpenReviewAsync(string actorName, string path = "/movies/review")
    {
        var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var actor = await DbSeeding.SeedActorAsync(dbFactory, actorName, isFavorite: true);
        var prefix = "E2E-KEYS-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var first = await DbSeeding.SeedMovieAsync(dbFactory, prefix + "-1", MovieStatus.Got);
        var second = await DbSeeding.SeedMovieAsync(dbFactory, prefix + "-2", MovieStatus.Got);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == first.Id || m.Id == second.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.JellyfinItemId, m => "keys-item-" + m.Id));
            db.MovieActors.AddRange(
                new MovieActor { MovieId = first.Id, ActorId = actor.Id },
                new MovieActor { MovieId = second.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, dbFactory, first);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, dbFactory, second);
        fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>().ClearSession();

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"{path}?actorId={actor.Id}");

        await Expect(page.Locator(".cleanup-eligible-pill")).ToHaveTextAsync("2 eligible");
        await Expect(page.Locator("video.cleanup-video")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.cleanup-video'); return v && v.readyState >= 2 && !v.error; }");
        return page;
    }
}
