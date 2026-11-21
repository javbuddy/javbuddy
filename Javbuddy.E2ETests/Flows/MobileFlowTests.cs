using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Phone layouts and touch input, in a real touch-enabled phone viewport: what bUnit can't render (CSS
/// isolation, media and container queries) or drive (touch gestures, the player's idle chrome).</summary>
[Collection(E2ECollection.Name)]
public class MobileFlowTests(E2EFixture fixture)
{
    private static Task<float> WidthAsync(IPage page, string selector) =>
        page.EvaluateAsync<float>($"() => document.querySelector('{selector}').getBoundingClientRect().width");

    private static Task<float> LeftAsync(IPage page, string selector) =>
        page.EvaluateAsync<float>($"() => document.querySelector('{selector}').getBoundingClientRect().left");

    [Fact]
    public async Task MovieDetail_OnAPhone_ShowsItsDetails_BelowTheCoverInPortraitAndBesideItInLandscape()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-MOBILE-DETAIL-1");

        // Portrait: one column, the details as wide as the cover, below it.
        var page = await fixture.NewPhonePageAsync(390, 844);
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await Expect(page.Locator(".movie-detail-title")).ToBeVisibleAsync();
        Assert.True(await WidthAsync(page, ".movie-detail-main") > 250, "The details should get the page's width in portrait.");
        Assert.Equal(await LeftAsync(page, ".movie-detail-sidebar"), await LeftAsync(page, ".movie-detail-main"));

        // Landscape: the sidebar menu is back, and the cover shrinks so the details stay beside it.
        page = await fixture.NewPhonePageAsync(667, 375);
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        Assert.True(await WidthAsync(page, ".movie-detail-main") > 150, "The details shouldn't be squeezed out beside the cover in landscape.");
        Assert.True(await LeftAsync(page, ".movie-detail-main") > await LeftAsync(page, ".movie-detail-sidebar") + 100, "The details should sit beside the cover.");
        Assert.Equal(667, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
    }

    [Fact]
    public async Task PhoneMenu_OpensUnderTheHeader_WhenScrolledDown_WithItsLabels_EvenWithTheSidebarCollapsed()
    {
        var page = await fixture.NewPhonePageAsync();
        await page.GotoInteractiveAsync("/system/status");
        // The collapsed, icons-only sidebar is a desktop preference; the phone menu has room for the labels.
        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-sidebar-collapsed', 'true')");
        await page.EvaluateAsync("() => window.scrollTo(0, 600)");
        await page.WaitForFunctionAsync("() => window.scrollY > 0");

        await page.Locator(".navbar-toggler").ClickAsync();

        var menu = page.Locator(".nav-scrollable");
        await Expect(menu).ToBeVisibleAsync();
        var header = (await page.Locator(".app-header").BoundingBoxAsync())!;
        var box = (await menu.BoundingBoxAsync())!;
        Assert.InRange(box.Y, header.Y + header.Height - 1, header.Y + header.Height + 1);
        await Expect(page.Locator(".nav-label", new() { HasText = "Movies" }).First).ToBeVisibleAsync();
    }

    private async Task<IPage> OpenLoopingClipOnAPhoneAsync(string code)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.MediaDurationSeconds = 3;
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, 0.5, 2.5, code);
        }

        var page = await fixture.NewPhonePageAsync();
        await page.GotoInteractiveAsync("/movies/scenes?view=highlights");
        // Loop on, so the 2 s clip doesn't close itself under the assertions.
        await page.EvaluateAsync("() => localStorage.setItem('javbuddy-clip-loop', '1')");
        // A tap, as on a phone: a mouse click would leave the pointer hovering where the player's controls
        // open, which (rightly, for a mouse) keeps them on screen.
        await page.Locator(".scene-wall-card-highlight", new() { HasText = code }).TapAsync();
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.video-player-video'); return v && v.readyState >= 2 && !v.error; }");
        // The controls' module is running (it fills the time readout), so it won't start the clip again later.
        await Expect(page.Locator(".clip-controls-time")).Not.ToBeEmptyAsync();
        return page;
    }

    [Fact]
    public async Task ClipPlayer_DraggingAFingerAlongTheTrack_PreviewsAndSeeksThere_WithoutStartingPlayback()
    {
        var page = await OpenLoopingClipOnAPhoneAsync("E2E-MOBILE-SCRUB-1");
        await page.EvaluateAsync("() => document.querySelector('video.video-player-video').pause()");
        await page.EvaluateAsync("() => { const v = document.querySelector('video.video-player-video'); v.currentTime = 0.6; }");
        await page.WaitForFunctionAsync("() => !document.querySelector('video.video-player-video').seeking");

        var track = (await page.Locator(".clip-controls-track-wrap").BoundingBoxAsync())!;
        var y = track.Y + track.Height / 2;
        var from = track.X + track.Width * 0.1f;
        var to = track.X + track.Width * 0.9f;
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from, ["y"] = y } },
        });
        for (var i = 1; i <= 10; i++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from + (to - from) * i / 10, ["y"] = y } },
            });
        }

        // The preview follows the finger; the video doesn't move until it's let go.
        await Expect(page.Locator(".clip-controls-preview-visible")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<double>("() => document.querySelector('video.video-player-video').currentTime") < 1);

        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });

        // 90% of the 0.5 → 2.5 s clip: 2.3 s into the video, still paused.
        await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').currentTime > 2");
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('video.video-player-video').paused"));
        await Expect(page.Locator(".clip-controls-preview-visible")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ClipPlayer_ATapOnTheVideo_WhileItsControlsAreHidden_OnlyBringsThemBack()
    {
        var page = await OpenLoopingClipOnAPhoneAsync("E2E-MOBILE-REVEAL-1");
        // A quarter speed, so the 2 s clip plays 8 s before it loops: a loop's seek restarts the idle timer.
        await page.EvaluateAsync("() => { document.querySelector('video.video-player-video').playbackRate = 0.25; }");
        var panel = page.Locator(".video-player-modal-panel");
        // Idle-hidden after 3 s of playing untouched.
        await Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("player-idle"), new() { Timeout = 10_000 });

        var video = (await page.Locator("video.video-player-video").BoundingBoxAsync())!;
        await page.Touchscreen.TapAsync(video.X + video.Width / 2, video.Y + video.Height / 3);

        await Expect(panel).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("player-idle"));
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('video.video-player-video').paused"));
    }
}
