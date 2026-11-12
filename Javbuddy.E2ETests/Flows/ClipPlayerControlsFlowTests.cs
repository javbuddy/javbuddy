using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The clip player's control bar matches the movie player's scrub bar: the CC button sits in it beside the
/// volume rather than floating over the video (the player column hands it to ClipControls), and the volume slider
/// pops up over the speaker icon on hover instead of showing permanently.</summary>
[Collection(E2ECollection.Name)]
public class ClipPlayerControlsFlowTests(E2EFixture fixture)
{
    private async Task<Microsoft.Playwright.IPage> OpenClipAsync(string code)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.MediaDurationSeconds = 3;
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }
        await File.WriteAllTextAsync(Path.Combine(DbSeeding.PlayableLibraryRoot, movie.Code!, movie.Code + ".en.srt"),
            "1\n00:00:00,000 --> 00:00:02,000\nHello\n");
        using (var scope = fixture.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMovieHighlightService>().AddHighlightAsync(movie.Id, 0.5, 2.5, code);
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/scenes?view=highlights");
        // Loop on, so the 2 s clip doesn't close itself under the assertions.
        await page.EvaluateAsync("() => localStorage.setItem('javbuddy-clip-loop', '1')");
        await page.Locator(".scene-wall-card-highlight", new() { HasText = code }).ClickAsync();
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video.video-player-video'); return v && v.readyState >= 2 && !v.error; }");
        await Expect(page.Locator(".clip-controls-time")).Not.ToBeEmptyAsync();
        return page;
    }

    [Fact]
    public async Task ClipPlayer_ShowsTheCcButtonInItsControlBar()
    {
        var page = await OpenClipAsync("E2E-CLIPCC-1");
        var cc = page.Locator(".clip-controls .video-player-subtitles-in-bar .video-player-subtitles-button");
        await Expect(cc).ToBeVisibleAsync();
        await Expect(page.Locator(".video-player-subtitles-overlay")).ToHaveCountAsync(0);

        // Beside the volume: on the controls' row, before the mute button.
        var ccBox = (await cc.BoundingBoxAsync())!;
        var muteBox = (await page.Locator(".clip-controls-mute").BoundingBoxAsync())!;
        Assert.True(ccBox.X + ccBox.Width <= muteBox.X + 1, "The CC button should sit before the mute button.");

        await cc.ClickAsync();
        await Expect(page.Locator(".video-player-subtitles-menu")).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClipPlayer_VolumeSliderPopsUpOverTheSpeakerIconOnHover()
    {
        var page = await OpenClipAsync("E2E-CLIPVOL-1");
        var slider = page.Locator(".clip-controls-volume");
        await Expect(slider).ToBeHiddenAsync();

        await page.Locator(".clip-controls-mute").HoverAsync();

        await Expect(slider).ToBeVisibleAsync();
        var sliderBox = (await slider.BoundingBoxAsync())!;
        var muteBox = (await page.Locator(".clip-controls-mute").BoundingBoxAsync())!;
        Assert.True(sliderBox.Y + sliderBox.Height <= muteBox.Y + 1, "The slider should float above the speaker icon.");

        await page.Mouse.MoveAsync(5, 5);
        await Expect(slider).ToBeHiddenAsync();
    }
}
