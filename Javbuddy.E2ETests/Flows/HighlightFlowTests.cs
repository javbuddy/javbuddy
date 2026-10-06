using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Movie highlights in the player modal. Pausing at a clip's end and the
/// ScrubBar's highlight hover/click run entirely in the browser's JS, which bUnit can't execute.</summary>
[Collection(E2ECollection.Name)]
public class HighlightFlowTests
{
    private const string Video = "video.video-player-video";

    private readonly E2EFixture fixture;

    public HighlightFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    private async Task<Movie> SeedPlayableMovieAsync(string code, string itemId)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var m = (await db.Movies.FindAsync(movie.Id))!;
        m.JellyfinItemId = itemId;
        m.JellyfinServerId = FakeJellyfinServer.ServerId;
        // The fixture video is a 3-second clip.
        m.MediaDurationSeconds = 3;
        await db.SaveChangesAsync();
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        return m;
    }

    private async Task<IPage> OpenPlayerAsync(Movie movie)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}");
        // Every position the video seeks to, to tell a seek to a highlight's start from one to
        // wherever the pointer was.
        await page.EvaluateAsync($"() => {{ window.__seeks = []; const v = document.querySelector('{Video}'); v.addEventListener('seeking', () => window.__seeks.push(v.currentTime)); }}");
        return page;
    }

    private static Task<double> LastSeekAsync(IPage page) => page.EvaluateAsync<double>("() => window.__seeks.at(-1)");

    [Fact]
    public async Task AddHighlight_ShowsOnTheTimeline_AndPlayingItPausesAtItsEnd()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-1", "fake-jf-item-hilite-1");
        var page = await OpenPlayerAsync(movie);
        var panel = page.Locator(".video-player-side-panel");

        await panel.Locator(".highlight-editor-add-btn").ClickAsync();
        await panel.Locator(".highlight-editor-title-input").FillAsync("Climax");
        await panel.Locator(".highlight-editor-start-input").FillAsync("0.5");
        await panel.Locator(".highlight-editor-end-input").FillAsync("1.5");
        await panel.Locator(".highlight-editor-save-btn").ClickAsync();
        await Expect(panel.Locator(".highlight-editor-row")).ToContainTextAsync("Climax");
        // No trickplay: the highlight track sits on the thumbnail-less scrub bar, and its span seeks.
        var segment = page.Locator(".video-player-column .scrub-bar-track-wrap .highlight-segment");
        await Expect(segment).ToHaveCountAsync(1);
        await segment.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__seeks.length > 0");
        Assert.Equal(0.5, await LastSeekAsync(page), 2);

        await page.EvaluateAsync($"() => document.querySelector('{Video}').pause()");
        await panel.Locator(".highlight-editor-play-btn").ClickAsync();

        // Pauses at the clip's end, well before the video's own end at 3s.
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v.paused && v.currentTime >= 1.5; }}");
        Assert.InRange(await page.EvaluateAsync<double>($"() => document.querySelector('{Video}').currentTime"), 1.5, 2.2);
        Assert.Equal(0.5, await LastSeekAsync(page), 2);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    private async Task<IPage> OpenWallClipAsync(Movie movie, double start, double end, string title, bool loop, string? initScript = null)
    {
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, start, end, title);
        }
        var page = await fixture.NewPageAsync();
        if (initScript is not null) await page.AddInitScriptAsync(initScript);
        await page.GotoInteractiveAsync("/movies/scenes?view=highlights");
        await page.EvaluateAsync(loop ? "() => localStorage.setItem('javbuddy-clip-loop', '1')" : "() => localStorage.removeItem('javbuddy-clip-loop')");
        await page.Locator(".scene-wall-card-highlight", new() { HasText = title }).ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}",
            null, new PageWaitForFunctionOptions { PollingInterval = 50 });
        // The clip controls' JS is attached once it has filled in the time readout.
        await Expect(page.Locator(".clip-controls-time")).Not.ToBeEmptyAsync();
        return page;
    }

    [Fact]
    public async Task HighlightWall_CardPlaysJustTheClip_AndClosesAtItsEnd()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-4", "fake-jf-item-hilite-4");
        var page = await OpenWallClipAsync(movie, 0.5, 2.5, "Wall clip", loop: false);

        // The wall's own clip player: no navigation, no editor, and only the clip's
        // range on its controls, 0:00 to the clip's 2 s rather than the fake stream's 3 s.
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/movies/scenes\\?view=highlights$"));
        await Expect(page.Locator(".video-player-side-panel")).ToHaveCountAsync(0);
        Assert.False(await page.Locator(Video).EvaluateAsync<bool>("v => v.hasAttribute('controls')"));
        await Expect(page.Locator(".clip-controls-time")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^0:0[0-2] / 0:02$"));
        await Expect(page.Locator(".clip-controls-loop")).ToHaveAttributeAsync("aria-pressed", "false");

        // At the clip's end the player closes, back on the wall.
        await Expect(page.Locator(".video-player-modal-backdrop")).ToHaveCountAsync(0, new() { Timeout = 10000 });
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/movies/scenes\\?view=highlights$"));
        await Expect(page.Locator(".scene-wall-card-highlight", new() { HasText = "Wall clip" })).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_RememberedLoop_RestartsTheClip_UntilTurnedOff()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-6", "fake-jf-item-hilite-6");
        var page = await OpenWallClipAsync(movie, 0.5, 1.5, "Loop clip", loop: true);
        var loop = page.Locator(".clip-controls-loop");
        await Expect(loop).ToHaveAttributeAsync("aria-pressed", "true");

        // Past the clip's end it jumps back to the clip's start and keeps playing, player still open.
        await page.WaitForFunctionAsync($"() => document.querySelector('{Video}').currentTime >= 1.3");
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return !v.paused && v.currentTime < 1.0; }}");
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();

        // Turning Loop off is remembered, and the clip then closes at its end.
        await loop.ClickAsync();
        await Expect(loop).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('javbuddy-clip-loop')"));
        await Expect(page.Locator(".video-player-modal-backdrop")).ToHaveCountAsync(0, new() { Timeout = 10000 });
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_Loop_StillRestartsTheClip_WithoutAnimationFrames()
    {
        // A background tab pauses requestAnimationFrame while the video plays on; the clip's end
        // must still be caught (by timeupdate), not only once the tab is visible again.
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-8", "fake-jf-item-hilite-8");
        var page = await OpenWallClipAsync(movie, 0.5, 1.5, "Hidden clip", loop: true,
            initScript: "window.requestAnimationFrame = () => 0; window.cancelAnimationFrame = () => {};");
        await page.EvaluateAsync($"() => {{ window.__max = 0; const v = document.querySelector('{Video}'); v.addEventListener('timeupdate', () => window.__max = Math.max(window.__max, v.currentTime)); }}");

        // Polled on an interval: Playwright's default polling uses the stubbed-out animation frames.
        var poll = new PageWaitForFunctionOptions { PollingInterval = 50 };
        await page.WaitForFunctionAsync($"() => document.querySelector('{Video}').currentTime >= 1.3", null, poll);
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return !v.paused && v.currentTime < 1.0; }}", null, poll);
        // Restarted from near the clip's end (1.5 s), not from the 3 s stream's own end. The upper bound is
        // what counts: the timeupdate that crosses the end is already reset to the start when this
        // listener, added after the player's own, reads it.
        Assert.InRange(await page.EvaluateAsync<double>("() => window.__max"), 1.0, 2.0);
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_SpaceAfterClickingLoop_PausesInsteadOfTogglingLoop()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-9", "fake-jf-item-hilite-9");
        var page = await OpenWallClipAsync(movie, 0.3, 2.8, "Space clip", loop: false);
        var loop = page.Locator(".clip-controls-loop");

        await loop.ClickAsync();
        await Expect(loop).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Keyboard.PressAsync(" ");

        await page.WaitForFunctionAsync($"() => document.querySelector('{Video}').paused");
        await Expect(loop).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal("1", await page.EvaluateAsync<string?>("() => localStorage.getItem('javbuddy-clip-loop')"));
    }

    [Fact]
    public async Task HighlightWall_PlayingFromTheClipsEnd_RestartsItInsteadOfClosing()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-10", "fake-jf-item-hilite-10");
        var page = await OpenWallClipAsync(movie, 0.3, 2.8, "End clip", loop: false);

        // Paused at the clip's end, e.g. after → clamped a skip there.
        await page.EvaluateAsync($"() => {{ const v = document.querySelector('{Video}'); v.pause(); v.currentTime = 2.8; }}");
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return !v.seeking && v.currentTime >= 2.75; }}");

        await page.EvaluateAsync($"() => document.querySelector('{Video}').play()");

        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return !v.paused && v.currentTime < 1.0; }}");
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();
    }

    // Two highlights in two movies, found by a search token no other test uses, then Play all.
    private async Task<IPage> PlayAllAsync(string token, string codePrefix, double firstEnd, double secondEnd)
    {
        foreach (var (n, end) in new[] { (1, firstEnd), (2, secondEnd) })
        {
            var movie = await SeedPlayableMovieAsync($"{codePrefix}-{n}", $"fake-jf-item-{codePrefix.ToLowerInvariant()}-{n}");
            using var scope = fixture.App.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, 0.3, end, $"{token} {n}");
        }
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/scenes?view=highlights");
        await page.EvaluateAsync("() => localStorage.removeItem('javbuddy-clip-loop')");
        await page.Locator(".scene-wall-search").FillAsync(token);
        await Expect(page.Locator(".scene-wall-count")).ToHaveTextAsync("2 highlights");
        await page.Locator(".scene-wall-play-btn", new() { HasText = "Play all" }).ClickAsync();
        await Expect(page.Locator(".clip-player-position")).ToHaveTextAsync("1 / 2");
        await Expect(page.Locator(".clip-controls-time")).Not.ToBeEmptyAsync();
        return page;
    }

    [Fact]
    public async Task HighlightWall_PlayAll_MovesToTheNextClipAtEachEnd_ThenCloses()
    {
        var page = await PlayAllAsync("Zqxplay", "E2E-PLAYALL", 1.0, 1.0);
        var first = await page.Locator(".video-player-modal-name").TextContentAsync();

        // The first clip's end moves on to the second, in the same player, from another movie.
        await Expect(page.Locator(".clip-player-position")).ToHaveTextAsync("2 / 2", new() { Timeout = 10000 });
        Assert.NotEqual(first, await page.Locator(".video-player-modal-name").TextContentAsync());
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && !v.paused && v.currentTime >= 0.3; }}");

        // The last clip's end closes the player.
        await Expect(page.Locator(".video-player-modal-backdrop")).ToHaveCountAsync(0, new() { Timeout = 10000 });
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_PlayAll_NAndP_MoveBetweenClips()
    {
        // Lengths 2.5 s and 1.9 s: the readout ("/ 0:02" or "/ 0:01") tells the clips' controls apart.
        var page = await PlayAllAsync("Zqxkeys", "E2E-PLAYKEYS", 2.8, 2.2);
        var time = page.Locator(".clip-controls-time");
        var paused = $"() => document.querySelector('{Video}').paused";
        // Actually playing: while a new src loads the video reports paused, and Space would start it.
        var playing = $"() => {{ const v = document.querySelector('{Video}'); return v && !v.paused && v.readyState >= 3 && v.currentTime >= 0.3; }}";
        var quick = new LocatorAssertionsToHaveTextOptions { Timeout = 1500 };

        // Paused, so only N can move on (a clip's end would too).
        await page.WaitForFunctionAsync(playing);
        await page.Keyboard.PressAsync(" ");
        await page.WaitForFunctionAsync(paused);
        var firstLength = (await time.TextContentAsync())![^4..];
        await page.Keyboard.PressAsync("n");
        await Expect(page.Locator(".clip-player-position")).ToHaveTextAsync("2 / 2", quick);

        // Once the second clip's controls are live, pause it and go back: just started, so P goes back
        // a clip rather than restarting this one.
        await Expect(time).Not.ToHaveTextAsync(new System.Text.RegularExpressions.Regex($"{firstLength}$"));
        await page.WaitForFunctionAsync(playing);
        await page.Keyboard.PressAsync(" ");
        await page.WaitForFunctionAsync(paused);
        await page.Keyboard.PressAsync("p");
        await Expect(page.Locator(".clip-player-position")).ToHaveTextAsync("1 / 2", quick);
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_EditInMovie_OpensMovieDetailsEditorAtTheHighlight()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-7", "fake-jf-item-hilite-7");
        // Looping, so the player is still open to click in.
        var page = await OpenWallClipAsync(movie, 0.5, 1.5, "Edit clip", loop: true);

        await page.Locator("a.clip-player-edit-btn").ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/movies/{movie.Code}\\?highlight=\\d+$"));
        await Expect(page.Locator(".video-player-side-panel .highlight-editor-row")).ToHaveCountAsync(1);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HighlightWall_BackAndTheNavLink_ShowTheViewTheUrlNames()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-5", "fake-jf-item-hilite-5");
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieSceneService>().AddSceneAsync(movie.Id, 0, 2, "Zqxback scene");
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, 0.5, 1.5, "Zqxback clip");
        }
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/scenes");
        // Other tests' clips can push these past the virtualized wall's first window; the search (kept in
        // the wall's cookie across navigations) narrows both views to just these.
        await page.Locator(".scene-wall-search").FillAsync("Zqxback");
        var sceneCard = page.Locator(".scene-wall-card", new() { HasText = "Zqxback scene" });
        var highlightCard = page.Locator(".scene-wall-card-highlight", new() { HasText = "Zqxback clip" });
        await Expect(sceneCard).ToBeVisibleAsync();

        await page.Locator(".scene-wall-view-btn[data-view=highlights]").ClickAsync();
        await Expect(highlightCard).ToBeVisibleAsync();

        // Back re-supplies ?view to the same page instance (the router doesn't recreate it).
        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/movies/scenes$"));
        await Expect(sceneCard).ToBeVisibleAsync();
        await Expect(page.Locator(".scene-wall-view-btn[data-view=scenes]")).ToHaveAttributeAsync("aria-pressed", "true");

        await page.GoForwardAsync();
        await Expect(highlightCard).ToBeVisibleAsync();

        // The sidebar's Scenes link from the highlight view.
        await page.Locator("nav a[href='movies/scenes']").ClickAsync();
        await Expect(sceneCard).ToBeVisibleAsync();
        await Expect(page.Locator(".scene-wall-count")).ToContainTextAsync("scene");
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteHighlight_AsksFirst_EscapeCancelsWithoutClosingThePlayer()
    {
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-3", "fake-jf-item-hilite-3");
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, 1, 2, "Climax");
        }
        var page = await OpenPlayerAsync(movie);
        var panel = page.Locator(".video-player-side-panel");
        var dialog = page.Locator(".delete-confirm-dialog");

        await panel.Locator(".highlight-editor-delete-btn").ClickAsync();
        await Expect(dialog.Locator("h2")).ToHaveTextAsync("Delete \"Climax\"?");
        await Expect(page.Locator(".delete-confirm-cancel-btn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();
        await Expect(panel.Locator(".highlight-editor-row")).ToHaveCountAsync(1);

        // A real, hit-tested click: the dialog has to sit above the player modal.
        await panel.Locator(".highlight-editor-delete-btn").ClickAsync();
        await page.Locator(".delete-confirm-confirm-btn").ClickAsync();
        await Expect(panel.Locator(".highlight-editor-row")).ToHaveCountAsync(0);
        await Expect(page.Locator(".video-player-modal-backdrop")).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ScrubBar_HoveringAHighlightPreviewsItsStart_AndClickingSeeksThere()
    {
        const string itemId = "fake-jf-item-hilite-2";
        fixture.FakeJellyfin.Trickplay = new JellyfinTrickplayItemDto
        {
            Id = itemId,
            RunTimeTicks = TimeSpan.FromSeconds(3).Ticks,
            Trickplay = new()
            {
                [itemId] = new()
                {
                    ["320"] = new JellyfinTrickplayInfoDto { Width = 320, Height = 180, TileWidth = 10, TileHeight = 10, ThumbnailCount = 3, Interval = 1000 },
                },
            },
        };
        var movie = await SeedPlayableMovieAsync("E2E-HILITE-2", itemId);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var highlights = scope.ServiceProvider.GetRequiredService<IMovieHighlightService>();
            await highlights.AddHighlightAsync(movie.Id, 1, 2.5, "Climax");
            await highlights.AddHighlightAsync(movie.Id, 1.5, 2, null);
        }
        var page = await OpenPlayerAsync(movie);

        // Overlapping highlights stack in two lanes above the scene track.
        var segments = page.Locator(".scrub-bar .highlight-segment");
        await Expect(segments).ToHaveCountAsync(2);
        var first = (await segments.Nth(0).BoundingBoxAsync())!;
        var second = (await segments.Nth(1).BoundingBoxAsync())!;
        var track = (await page.Locator(".scrub-bar-track").BoundingBoxAsync())!;
        Assert.True(second.Y > first.Y, "the overlapping highlight should sit in a lower lane");
        Assert.True(track.Y >= second.Y + second.Height, "the highlight track should sit above the scene track");

        await segments.Nth(0).HoverAsync(new() { Position = new() { X = first.Width * 0.8f, Y = 2 } });
        await Expect(page.Locator(".scrub-bar-time")).ToHaveTextAsync("0:01 · Climax");

        await segments.Nth(0).ClickAsync(new() { Position = new() { X = first.Width * 0.8f, Y = 2 } });
        await page.WaitForFunctionAsync("() => window.__seeks.length > 0");
        Assert.Equal(1, await LastSeekAsync(page), 2);

        // Elsewhere on the bar, clicking still seeks to the pointer's position.
        await page.Locator(".scrub-bar-track").ClickAsync(new() { Position = new() { X = track.Width * 0.1f, Y = track.Height / 2 } });
        await page.WaitForFunctionAsync("() => window.__seeks.at(-1) < 0.9");
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }
}
