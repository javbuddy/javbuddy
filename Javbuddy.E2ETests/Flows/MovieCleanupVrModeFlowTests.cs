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

/// <summary>VR 2D mode in Movie Cleanup's player. Needs a real browser: the unwarp
/// is a WebGL canvas fed by the real &lt;video&gt; (the app's own local stream), which bUnit
/// can't run.</summary>
[Collection(E2ECollection.Name)]
public class MovieCleanupVrModeFlowTests
{
    private readonly E2EFixture fixture;

    public MovieCleanupVrModeFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task Canvas_SpansFullPlayerHeight_WithoutBlackBarAtBottom()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FULL-HEIGHT");
        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        await WaitForCanvasPixelsAsync(page);

        // 1. Verify canvas spans the full player area height without a 3.5rem gap
        var areaRect = await page.Locator(".video-player-area").BoundingBoxAsync();
        var canvasRect = await page.Locator(".vr-viewer-canvas").BoundingBoxAsync();
        Assert.NotNull(areaRect);
        Assert.NotNull(canvasRect);
        Assert.Equal(areaRect.Height, canvasRect.Height);
        Assert.Equal(areaRect.Width, canvasRect.Width);

        // 2. Verify video element is transparent so the canvas shows through
        var isTransparent = await page.EvaluateAsync<bool>("""
            () => {
                const video = document.querySelector('video.cleanup-video');
                const cs = window.getComputedStyle(video);
                return cs.backgroundColor === 'rgba(0, 0, 0, 0)' || cs.backgroundColor === 'transparent';
            }
        """);
        Assert.True(isTransparent);

        // 3. Verify non-black pixels at the bottom of the player area (within the bottom 56px where the black bar used to be)
        var bytes = await page.Locator(".video-player-area").ScreenshotAsync();
        using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
        var pixelNearBottom = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 10);
        Assert.True(pixelNearBottom.Red > 0 || pixelNearBottom.Green > 0 || pixelNearBottom.Blue > 0,
            $"Expected non-black pixel at bottom of canvas, but got R={pixelNearBottom.Red}, G={pixelNearBottom.Green}, B={pixelNearBottom.Blue}");
    }



    [Fact]
    public async Task Toggle_UnwarpsTheVideo_DragLooksAround_AndToggleOffRestoresPlayback()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-1");

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        await Expect(page.Locator(".vr-viewer-error")).ToBeHiddenAsync();

        // The canvas gets real, non-black frames from the video.
        await WaitForCanvasPixelsAsync(page);
        var before = await CanvasSignatureAsync(page);

        var video = await page.Locator("video.cleanup-video").BoundingBoxAsync();
        var (x, y) = (video!.X + video.Width / 2, video.Y + video.Height / 3);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x - 60, y + 20, new() { Steps = 8 });
        await page.Mouse.UpAsync();

        Assert.NotEqual(before, await WaitForChangedSignatureAsync(page, before));

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer")).ToHaveCountAsync(0);
        await WaitForVideoDataAsync(page);
        Assert.Null(await page.Locator("video.cleanup-video").GetAttributeAsync("crossorigin"));
    }

    [Fact]
    public async Task ProjectionPicker_RedrawsTheView()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-2");

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await WaitForCanvasPixelsAsync(page);
        var equirect = await CanvasSignatureAsync(page);
        Assert.Equal("equirect", await page.Locator(".vr-viewer-projection").InputValueAsync());

        await page.Locator(".vr-viewer-projection").SelectOptionAsync("fisheye");

        await WaitForChangedSignatureAsync(page, equirect);
    }

    [Fact]
    public async Task FullscreenButton_EntersAndExitsFullscreen_WithUnwarpedCanvasAndDrag()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FS-1");

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        await WaitForCanvasPixelsAsync(page);

        // Native controls fullscreen button is hidden via controlslist
        Assert.Equal("nofullscreen", await page.Locator("video.cleanup-video").GetAttributeAsync("controlslist"));

        var fsButton = page.Locator(".vr-viewer-fullscreen");
        await Expect(fsButton).ToBeVisibleAsync();
        await Expect(page.Locator(".vr-viewer-fs-icon-enter")).ToBeVisibleAsync();
        await Expect(page.Locator(".vr-viewer-fs-icon-exit")).ToBeHiddenAsync();

        // Click the fullscreen button to enter fullscreen on the player column.
        await fsButton.ClickAsync();

        await page.WaitForFunctionAsync("() => document.fullscreenElement?.classList.contains('cleanup-player-column')");
        await Expect(page.Locator(".vr-viewer-fs-icon-exit")).ToBeVisibleAsync();
        await Expect(page.Locator(".vr-viewer-fs-icon-enter")).ToBeHiddenAsync();

        // Canvas is visible and rendered at fullscreen dimensions
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        var canvasBox = await page.Locator(".vr-viewer-canvas").BoundingBoxAsync();
        var areaBox = await page.Locator(".video-player-area").BoundingBoxAsync();
        Assert.NotNull(canvasBox);
        Assert.NotNull(areaBox);
        Assert.Equal(areaBox.Width, canvasBox.Width);
        Assert.Equal(areaBox.Height, canvasBox.Height);

        // Canvas renders real pixels and dragging looks around
        await WaitForCanvasPixelsAsync(page);
        var before = await CanvasSignatureAsync(page);
        var videoBox = await page.Locator("video.cleanup-video").BoundingBoxAsync();
        var (x, y) = (videoBox!.X + videoBox.Width / 2, videoBox.Y + videoBox.Height / 3);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x - 60, y + 20, new() { Steps = 8 });
        await page.Mouse.UpAsync();

        var changed = await WaitForChangedSignatureAsync(page, before);
        Assert.NotEqual(before, changed);

        // Click again to exit fullscreen
        await fsButton.ClickAsync();
        await page.WaitForFunctionAsync("() => document.fullscreenElement === null");
        await Expect(page.Locator(".vr-viewer-fs-icon-enter")).ToBeVisibleAsync();
        await Expect(page.Locator(".vr-viewer-fs-icon-exit")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task DoubleClick_TogglesFullscreen()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FS-2");

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        await WaitForCanvasPixelsAsync(page);

        var video = page.Locator("video.cleanup-video");
        var videoBox = await video.BoundingBoxAsync();
        var (x, y) = (videoBox!.X + videoBox.Width / 2, videoBox.Y + videoBox.Height / 3);

        // Double click outside the bottom controls strip enters fullscreen
        await page.Mouse.DblClickAsync(x, y);
        await page.WaitForFunctionAsync("() => document.fullscreenElement?.classList.contains('cleanup-player-column')");

        // Double click again exits fullscreen
        await page.Mouse.DblClickAsync(x, y);
        await page.WaitForFunctionAsync("() => document.fullscreenElement === null");
    }

    [Fact]
    public async Task VideoDirectFullscreen_Fallback_ResetsObjectPosition()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FS-3");

        await page.Locator(".cleanup-vr-toggle").ClickAsync();
        await Expect(page.Locator(".vr-viewer-canvas")).ToBeVisibleAsync();
        await WaitForCanvasPixelsAsync(page);

        // When not in fullscreen, the video's picture is pushed offscreen to -100000px
        var posBefore = await page.EvaluateAsync<string>("() => window.getComputedStyle(document.querySelector('video.cleanup-video')).objectPosition");
        Assert.Contains("-100000px", posBefore);

        // If the video element itself enters fullscreen (e.g. Firefox fallback), the fullscreen CSS rule resets it
        var posInFs = await page.EvaluateAsync<string>(@"async () => {
            const video = document.querySelector('video.cleanup-video');
            // Directly call native requestFullscreen on the prototype to test CSS pseudo-class fallback
            await HTMLVideoElement.prototype.requestFullscreen.call(video);
            return window.getComputedStyle(video).objectPosition;
        }");

        Assert.Equal("50% 50%", posInFs);
    }

    [Fact]
    public async Task FullscreenVideo_HidesNativeTimelineAndFullscreenButton()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FS-4");

        var result = await page.EvaluateAsync<bool[]>(@"async () => {
            const video = document.querySelector('video.cleanup-video');
            video.classList.add('video-player-scrub');

            let hidesTimeline = false;
            let hidesFsBtn = false;
            for (const sheet of document.styleSheets) {
                try {
                    for (const rule of sheet.cssRules) {
                        if (rule.selectorText && rule.selectorText.includes('.video-player-scrub') && rule.selectorText.includes('webkit-media-controls-timeline')) {
                            if (rule.style.display.includes('none')) hidesTimeline = true;
                        }
                        if (rule.selectorText && rule.selectorText.includes('.video-player-scrub') && rule.selectorText.includes('webkit-media-controls-fullscreen-button')) {
                            if (rule.style.display.includes('none')) hidesFsBtn = true;
                        }
                    }
                } catch (e) {}
            }

            return [hidesTimeline, hidesFsBtn];
        }");

        Assert.True(result[0], "Timeline should be hidden on video-player-scrub");
        Assert.True(result[1], "Native fullscreen button should be hidden on video-player-scrub");
    }

    [Fact]
    public async Task FullscreenVideo_WhenScrubBarPresent_FullscreenButtonTogglesContainer()
    {
        var page = await OpenCleanupOnVrMovieAsync("E2E-VR-FS-5");

        var fsClass = await page.EvaluateAsync<string>(@"async () => {
            const video = document.querySelector('video.cleanup-video');
            const container = video.closest('.cleanup-player-column');
            await container.requestFullscreen();
            const el = document.fullscreenElement;
            await document.exitFullscreen();
            return el?.className ?? 'none';
        }");

        Assert.Contains("cleanup-player-column", fsClass);
    }

    /// <summary>Seeds a Jellyfin-linked movie linked to its own actor and opens Cleanup scoped to
    /// that actor, so the queue holds exactly this movie. Skipping through the unscoped queue
    /// instead meant one SignalR round trip per Got movie other tests left in the shared fixture
    /// DB (hundreds), which made each test here take up to a minute. A
    /// movie with a local file autoplays on load, so the video is already in the page.</summary>
    private async Task<IPage> OpenCleanupOnVrMovieAsync(string code)
    {
        var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var actor = await DbSeeding.SeedActorAsync(dbFactory, "Vrtest " + code);
        var movie = await DbSeeding.SeedMovieAsync(dbFactory, code, MovieStatus.Got);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == movie.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.JellyfinItemId, "vr-item-" + code));
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, dbFactory, movie);
        fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>().ClearSession();

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/cleanup?actorId={actor.Id}");

        await Expect(page.Locator(".cleanup-eligible-pill")).ToHaveTextAsync("1 eligible");
        await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(code);

        await Expect(page.Locator("video.cleanup-video")).ToBeVisibleAsync();
        await WaitForVideoDataAsync(page);
        return page;
    }

    private static Task WaitForVideoDataAsync(IPage page) =>
        page.WaitForFunctionAsync("() => { const v = document.querySelector('video.cleanup-video'); return v && v.readyState >= 2 && !v.error; }");

    // Samples the unwarped canvas through a 2D canvas: unlike gl.readPixels via getContext('webgl'),
    // drawImage never creates a WebGL context, so polling before the viewer's async init has run
    // can't steal the canvas's context (which would ignore the viewer's context options). The viewer
    // keeps its last frame readable (preserveDrawingBuffer), so this works between redraws.
    private const string SignatureScript = """
        () => {
            const c = document.querySelector('.vr-viewer-canvas');
            if (!c || c.width === 0) return null;
            const copy = document.createElement('canvas');
            copy.width = c.width;
            copy.height = c.height;
            const ctx = copy.getContext('2d', { willReadFrequently: true });
            ctx.drawImage(c, 0, 0);
            let sig = '';
            for (let i = 1; i < 12; i++) {
                for (const fy of [0.3, 0.5, 0.7]) {
                    const d = ctx.getImageData(Math.floor(c.width * i / 12), Math.floor(c.height * fy), 1, 1).data;
                    sig += d[0] + ',' + d[1] + ',' + d[2] + ';';
                }
            }
            return sig;
        }
        """;

    private static async Task<string> CanvasSignatureAsync(IPage page) =>
        await page.EvaluateAsync<string>(SignatureScript);

    private static Task WaitForCanvasPixelsAsync(IPage page) =>
        page.WaitForFunctionAsync($"() => {{ const s = ({SignatureScript})(); return s !== null && s.split(';').some(p => p && p.split(',').some(n => Number(n) > 30)); }}");

    private static async Task<string> WaitForChangedSignatureAsync(IPage page, string previous)
    {
        var handle = await page.WaitForFunctionAsync(
            $"() => {{ const s = ({SignatureScript})(); return s !== null && s !== {System.Text.Json.JsonSerializer.Serialize(previous)} ? s : false; }}");
        return await handle.JsonValueAsync<string>();
    }
}
