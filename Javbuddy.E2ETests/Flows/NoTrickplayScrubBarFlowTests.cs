using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>A movie without any trickplay in the real player: Javbuddy's scrub bar
/// replaces the browser's seek bar instead of sitting under it, its hover shows the time and scene
/// with no thumbnail, and a click seeks the video. A movie whose length was never probed uses the
/// video's own. The Review and Cleanup cards' player does the same.</summary>
[Collection(E2ECollection.Name)]
public class NoTrickplayScrubBarFlowTests
{
    private readonly E2EFixture fixture;

    public NoTrickplayScrubBarFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Theory]
    [InlineData("E2E-NO-TP-1", true)]
    [InlineData("E2E-NO-TP-2", false)]
    public async Task WithoutTrickplay_TheScrubBarReplacesTheNativeSeekBar(string code, bool storedDuration)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == movie.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.MediaDurationSeconds, storedDuration ? 3 : null));
            db.Scenes.Add(new Scene { MovieId = movie.Id, StartSeconds = 0, Title = "Opening" });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-detail-badge-play").ClickAsync();
            var track = page.Locator(".video-player-column .scrub-bar-track");
            await Expect(track).ToBeVisibleAsync();
            await Expect(page.Locator("video.video-player-video")).ToHaveClassAsync(new Regex(@"\bvideo-player-scrub\b"));
            await Expect(page.Locator(".scrub-bar-thumb")).ToHaveCountAsync(0);
            await page.EvaluateAsync("() => document.querySelector('video.video-player-video').pause()");

            // The bar's own controls stand in for the browser's.
            await Expect(page.Locator("video.video-player-video")).Not.ToHaveAttributeAsync("controls", new Regex(".*"));
            await page.Locator(".scrub-bar-play").ClickAsync();
            await page.WaitForFunctionAsync("() => !document.querySelector('video.video-player-video').paused");
            await page.Locator(".scrub-bar-play").ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').paused");
            await page.Locator(".scrub-bar-volume").FillAsync("0.5");
            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').volume === 0.5");
            await page.Locator(".scrub-bar-mute").ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').muted");
            await Expect(page.Locator(".scrub-bar-clock")).ToHaveTextAsync(new Regex(@"^\d+:\d\d / \d+:\d\d$"));

            // The preview only shows once the JS module has initialised, so hover until it does.
            var preview = page.Locator(".scrub-bar-preview-visible");
            for (var attempt = 0; attempt < 20 && await preview.CountAsync() == 0; attempt++)
            {
                await page.Mouse.MoveAsync(0, 0);
                await track.HoverAsync();
                await page.WaitForTimeoutAsync(100);
            }
            await Expect(page.Locator(".scrub-bar-time")).ToHaveTextAsync(new Regex(@"^0:0\d · Opening$"));

            var box = (await track.BoundingBoxAsync())!;
            await page.Mouse.ClickAsync(box.X + (box.Width * 0.8f), box.Y + (box.Height / 2));
            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').currentTime >= 2");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Theory]
    [InlineData("/movies/review", true)]
    [InlineData("/movies/review", false)]
    [InlineData("/movies/cleanup", true)]
    public async Task OnTheReviewAndCleanupCards_WithoutTrickplay_TheScrubBarReplacesTheNativeSeekBar(string path, bool storedDuration)
    {
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "E2E NoTp Review " + Guid.NewGuid().ToString("N")[..6], isFavorite: true);
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-NO-TP-R-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            await db.Movies.Where(m => m.Id == movie.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.MediaDurationSeconds, storedDuration ? 3 : null));
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.Scenes.Add(new Scene { MovieId = movie.Id, StartSeconds = 0, Title = "Opening" });
            await db.SaveChangesAsync();
        }
        fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>().ClearSession();

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"{path}?actorId={actor.Id}");
            var track = page.Locator(".cleanup-player-column .scrub-bar-track");
            await Expect(track).ToBeVisibleAsync();
            var video = page.Locator("video.cleanup-video");
            await Expect(video).ToHaveClassAsync(new Regex(@"\bvideo-player-scrub\b"));
            await Expect(video).ToHaveAttributeAsync("controlslist", "nofullscreen");
            await Expect(page.Locator(".scrub-bar-thumb")).ToHaveCountAsync(0);
            await page.EvaluateAsync("() => document.querySelector('video.cleanup-video').pause()");

            var box = (await track.BoundingBoxAsync())!;
            await page.Mouse.ClickAsync(box.X + (box.Width * 0.8f), box.Y + (box.Height / 2));
            await page.WaitForFunctionAsync("() => document.querySelector('video.cleanup-video').currentTime >= 2");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
