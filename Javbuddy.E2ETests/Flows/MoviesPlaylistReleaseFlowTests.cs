using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The Movies page's Play all: going to the next movie (the Next button or N), or closing the
/// player, lets go of the <c>&lt;video&gt;</c> that was playing. A <c>&lt;video&gt;</c> taken out of the page
/// only pauses, and kept its stream's connection open; after six movies the browser's connections to the
/// server were all taken, and every stream, image and request after that stalled.</summary>
[Collection(E2ECollection.Name)]
public class MoviesPlaylistReleaseFlowTests
{
    private readonly E2EFixture fixture;

    public MoviesPlaylistReleaseFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    // Each movie's <video> is kept in window.__playedVideos before moving on, so it can be checked after it
    // has left the page (and isn't garbage-collected meanwhile).
    private const string RememberVideo = "() => (window.__playedVideos ??= []).push(document.querySelector('video.video-player-video'))";

    // Every remembered <video> that's out of the page holds no stream: no src, nothing loaded (NETWORK_EMPTY).
    private const string AllReleased = "() => window.__playedVideos.filter(v => !v.isConnected)"
        + ".every(v => !v.hasAttribute('src') && v.networkState === HTMLMediaElement.NETWORK_EMPTY)";

    private const string Describe = "() => window.__playedVideos.map(v => `${v.isConnected}|${v.getAttribute('src') ?? ''}|${v.networkState}`).join(', ')";

    private static async Task AssertAllReleasedAsync(IPage page)
    {
        try
        {
            // The release runs once the render that took the <video> out has reached the page.
            await page.WaitForFunctionAsync(AllReleased, null, new() { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            Assert.Fail("A <video> no longer in the page still holds its stream: " + await page.EvaluateAsync<string>(Describe));
        }
    }

    [Fact]
    public async Task NextAndClose_ReleaseThePreviousVideosStream()
    {
        for (var i = 1; i <= 4; i++)
        {
            var code = $"E2E-PLREL-{i}";
            var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            db.MovieFiles.Add(new MovieFile { MovieId = movie.Id, FileName = code + ".webm", IsPrimary = true });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync("/");
            await page.Locator("main input[type=search]").First.FillAsync("E2E-PLREL");
            await Expect(page.Locator(".poster-card")).ToHaveCountAsync(4);
            await page.GetByRole(AriaRole.Button, new() { Name = "Play all" }).ClickAsync();
            await Expect(page.Locator("video.video-player-video")).ToBeVisibleAsync();

            for (var step = 0; step < 3; step++)
            {
                // The movie's stream has loaded before moving on, so its <video> has something to let go of.
                await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video')?.readyState >= 2");
                await page.EvaluateAsync(RememberVideo);
                var code = (await page.Locator(".video-player-modal-code").TextContentAsync())!;
                // The scrub bar's Next movie button and the N key both move on.
                if (step % 2 == 0) await page.GetByRole(AriaRole.Button, new() { Name = "Next movie" }).ClickAsync();
                else await page.Locator(".video-player-modal-backdrop").PressAsync("n");
                await Expect(page.Locator(".video-player-modal-code")).Not.ToHaveTextAsync(code);
            }
            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video')?.readyState >= 2");
            await AssertAllReleasedAsync(page);
            // The one on screen is still playing its stream.
            Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('video.video-player-video').hasAttribute('src')"));

            await page.EvaluateAsync(RememberVideo);
            await page.Locator(".video-player-modal-backdrop").PressAsync("Escape");
            await Expect(page.Locator("video.video-player-video")).ToHaveCountAsync(0);
            await AssertAllReleasedAsync(page);
            Assert.Equal(4, await page.EvaluateAsync<int>("() => window.__playedVideos.filter(v => !v.isConnected).length"));
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
