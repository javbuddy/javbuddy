using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Scrubbing onto a trickplay tile sheet that hadn't loaded yet pointed the
/// thumb at it straight away, so the preview showed its black background until the sheet arrived.
/// The thumb must keep the previous thumbnail until the new sheet is ready. The thumb records the
/// sheet it last drew in data-sheet.</summary>
[Collection(E2ECollection.Name)]
public class TrickplaySheetSwitchFlowTests
{
    private static readonly byte[] TinyWebp = Convert.FromBase64String("UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAwA0JaQAA3AA/vuUAAA=");

    private readonly E2EFixture fixture;

    public TrickplaySheetSwitchFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task ScrubBarPreview_KeepsThePreviousThumbnail_UntilTheNextSheetLoads()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-TP-SHEET-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        // One thumbnail per sheet, one per second: each second of the 3 s video is its own sheet.
        var identity = await DbSeeding.SeedLocalTrickplayAsync(fixture.DbFactory, movie, tileWidth: 1, tileHeight: 1);

        var page = await fixture.NewPageAsync();
        var sheet1Requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSheet1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"/trickplay/\d+/[^/]+/\d+\.webp"), async route =>
            {
                if (route.Request.Url.EndsWith($"/{identity}/1.webp", StringComparison.Ordinal))
                {
                    sheet1Requested.TrySetResult();
                    await releaseSheet1.Task;
                }
                await route.FulfillAsync(new() { ContentType = "image/webp", BodyBytes = TinyWebp });
            });

            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            var track = page.Locator(".video-player-column .scrub-bar-track");
            await page.Locator("button.movie-detail-badge-play").ClickUntilVisibleAsync(track);
            await Expect(track).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.scrub-bar-thumb')?.style.width !== ''");
            var box = (await track.BoundingBoxAsync())!;

            await track.HoverAsync(new() { Position = new Position { X = box.Width * 0.1f, Y = box.Height / 2 } });
            await page.WaitForFunctionAsync($"() => document.querySelector('.scrub-bar-thumb').dataset.sheet?.endsWith('/{identity}/0.webp')");

            await track.HoverAsync(new() { Position = new Position { X = box.Width * 0.5f, Y = box.Height / 2 } });
            await sheet1Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.EndsWith($"/{identity}/0.webp", await page.EvalOnSelectorAsync<string>(".scrub-bar-thumb", "t => t.dataset.sheet"));

            releaseSheet1.TrySetResult();
            await page.WaitForFunctionAsync($"() => document.querySelector('.scrub-bar-thumb').dataset.sheet?.endsWith('/{identity}/1.webp')");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            releaseSheet1.TrySetResult();
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ScrubBarHover_PreloadsEverySheet()
    {
        // The first hover downloads every sheet ahead of the pointer.
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-TP-PRELOAD-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var identity = await DbSeeding.SeedLocalTrickplayAsync(fixture.DbFactory, movie, tileWidth: 1, tileHeight: 1);

        var page = await fixture.NewPageAsync();
        var requested = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        try
        {
            var sheetUrl = new System.Text.RegularExpressions.Regex(@"/trickplay/\d+/[^/]+/(\d+)\.webp");
            await page.RouteAsync(sheetUrl, async route =>
            {
                requested[sheetUrl.Match(route.Request.Url).Groups[1].Value] = true;
                await route.FulfillAsync(new() { ContentType = "image/webp", BodyBytes = TinyWebp });
            });

            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            var track = page.Locator(".video-player-column .scrub-bar-track");
            await page.Locator("button.movie-detail-badge-play").ClickUntilVisibleAsync(track);
            await Expect(track).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.scrub-bar-thumb')?.style.width !== ''");
            Assert.Empty(requested);

            var box = (await track.BoundingBoxAsync())!;
            await track.HoverAsync(new() { Position = new Position { X = box.Width * 0.1f, Y = box.Height / 2 } });

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (requested.Count < 3 && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.Equal(["0", "1", "2"], requested.Keys.Order());
            await page.WaitForFunctionAsync($"() => document.querySelector('.scrub-bar-thumb').dataset.sheet?.endsWith('/{identity}/0.webp')");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
