using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>External subtitles in the real player: the .srt / .vtt beside the video load as tracks (the first
/// showing), the endpoint serves them as WebVTT, and the CC button, clickable over the player's header
/// overlay, steps through the tracks and off.</summary>
[Collection(E2ECollection.Name)]
public class PlayerSubtitlesFlowTests
{
    private readonly E2EFixture fixture;

    public PlayerSubtitlesFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task SidecarSubtitles_LoadAsTracks_AndTheCcButtonStepsThroughThemAndOff()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SUBS-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var folder = Path.Combine(DbSeeding.PlayableLibraryRoot, movie.Code!);
        await File.WriteAllTextAsync(Path.Combine(folder, "E2E-SUBS-1.en.srt"), "1\n00:00:00,500 --> 00:00:02,000\nHello & <welcome>\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "E2E-SUBS-1.ja.vtt"), "WEBVTT\n\n00:00:00.500 --> 00:00:02.000\nこんにちは\n");

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-detail-badge-play").ClickAsync();
            await Expect(page.Locator("video.video-player-video track")).ToHaveCountAsync(2);

            var vtt = await page.APIRequest.GetAsync(new Uri(new Uri(page.Url), $"/api/movies/{movie.Id}/subtitles/0").ToString());
            Assert.Equal(200, vtt.Status);
            Assert.StartsWith("text/vtt", vtt.Headers["content-type"]);
            Assert.Equal("WEBVTT\n\n1\n00:00:00.500 --> 00:00:02.000\nHello &amp; &lt;welcome>\n", await vtt.TextAsync());
            Assert.Equal(404, (await page.APIRequest.GetAsync(new Uri(new Uri(page.Url), $"/api/movies/{movie.Id}/subtitles/9").ToString())).Status);

            await page.WaitForFunctionAsync("() => document.querySelector('video.video-player-video').textTracks.length === 2");
            await page.WaitForFunctionAsync("() => Array.from(document.querySelector('video.video-player-video').textTracks).map(t => t.mode).join(',') === 'showing,disabled'");

            // Real clicks: they fail if the player's header overlay covers the button.
            var cc = page.Locator(".video-player-subtitles-button");
            var modes = "() => Array.from(document.querySelector('video.video-player-video').textTracks).map(t => t.mode).join(',')";
            await cc.ClickAsync();
            await page.Locator(".video-player-subtitles-item", new() { HasText = "Japanese" }).ClickAsync();
            await page.WaitForFunctionAsync($"() => ({modes[6..]}) === 'disabled,showing'");
            await cc.ClickAsync();
            await page.Locator(".video-player-subtitles-item", new() { HasText = "Off" }).ClickAsync();
            await page.WaitForFunctionAsync("() => Array.from(document.querySelector('video.video-player-video').textTracks).every(t => t.mode === 'disabled')");
            await Expect(cc).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("video-player-subtitles-on"));
            await cc.ClickAsync();
            await page.Locator(".video-player-subtitles-item", new() { HasText = "English" }).ClickAsync();
            await page.WaitForFunctionAsync($"() => ({modes[6..]}) === 'showing,disabled'");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task WithoutSubtitleFiles_NoCcButtonIsShown()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SUBS-2", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-detail-badge-play").ClickAsync();
            await Expect(page.Locator("video.video-player-video")).ToBeVisibleAsync();
            await Expect(page.Locator(".video-player-subtitles-button")).ToHaveCountAsync(0);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
