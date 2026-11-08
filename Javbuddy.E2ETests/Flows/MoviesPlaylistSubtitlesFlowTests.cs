using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The Movies page's Play all: going to the next / previous movie (N / P) swaps the subtitle
/// cues with the movie, instead of the last movie's line staying on screen — whether the next movie
/// has its own subtitles or none.</summary>
[Collection(E2ECollection.Name)]
public class MoviesPlaylistSubtitlesFlowTests
{
    private readonly E2EFixture fixture;

    public MoviesPlaylistSubtitlesFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    private async Task<Movie> SeedAsync(string code, string? vtt)
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code, MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.MovieFiles.Add(new MovieFile { MovieId = movie.Id, FileName = code + ".webm", IsPrimary = true });
            await db.SaveChangesAsync();
        }
        if (vtt is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(DbSeeding.PlayableLibraryRoot, code, code + ".en.vtt"), vtt);
        }
        return movie;
    }

    // How many places in the page's DOM, the browser's own (user-agent) shadow DOM that draws the cue
    // included, show this text: the rendered line, which the cue lists don't tell.
    private static async Task<int> RenderedCountAsync(Microsoft.Playwright.ICDPSession cdp, string text)
    {
        await cdp.SendAsync("DOM.getDocument", new Dictionary<string, object> { ["depth"] = -1, ["pierce"] = true });
        var search = await cdp.SendAsync("DOM.performSearch", new Dictionary<string, object> { ["query"] = text, ["includeUserAgentShadowDOM"] = true });
        var count = search!.Value.GetProperty("resultCount").GetInt32();
        await cdp.SendAsync("DOM.discardSearchResults", new Dictionary<string, object> { ["searchId"] = search.Value.GetProperty("searchId").GetString()! });
        return count;
    }

    private const string CuesExpression = "Array.from(document.querySelector('video.video-player-video').textTracks)"
        + ".flatMap(t => Array.from(t.cues ?? []).map(c => c.text)).join('|')";

    [Fact]
    public async Task NextAndPrevious_SwapTheSubtitleCuesWithTheMovie()
    {
        // A cue that stays active for the whole (3 s) clip, so it is on screen when the movie is switched.
        await SeedAsync("E2E-PLSUB-1", "WEBVTT\n\n00:00:00.000 --> 01:00:00.000\nFIRST LINE\n");
        await SeedAsync("E2E-PLSUB-2", "WEBVTT\n\n00:00:00.000 --> 01:00:00.000\nSECOND LINE\n");
        await SeedAsync("E2E-PLSUB-3", null);

        var page = await fixture.NewPageAsync();
        try
        {
            var cdp = await page.Context.NewCDPSessionAsync(page);
            await page.GotoInteractiveAsync("/");
            await page.Locator("main input[type=search]").First.FillAsync("E2E-PLSUB");
            await Expect(page.Locator(".poster-card")).ToHaveCountAsync(3);
            await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Play all" }).ClickAsync();
            await Expect(page.Locator("video.video-player-video")).ToBeVisibleAsync();

            // Whatever the order is, each movie must show only its own line.
            for (var step = 0; step < 4; step++)
            {
                var code = (await page.Locator(".video-player-modal-code").TextContentAsync())!;
                var expected = code switch { "E2E-PLSUB-1" => "FIRST LINE", "E2E-PLSUB-2" => "SECOND LINE", _ => "" };
                await page.WaitForFunctionAsync(
                    $"() => (document.querySelector('video.video-player-video').textTracks.length === {(expected == "" ? 0 : 1)})");
                if (expected != "")
                {
                    await page.WaitForFunctionAsync($"() => ({CuesExpression}) === '{expected}'");
                    await page.WaitForFunctionAsync("() => Array.from(document.querySelector('video.video-player-video').textTracks).every(t => t.mode === 'showing')");
                }
                else
                {
                    Assert.Equal("", await page.EvaluateAsync<string>("() => " + CuesExpression));
                }
                // Only this movie's own line is drawn, and only once.
                foreach (var other in new[] { "FIRST LINE", "SECOND LINE" }.Where(l => l != expected))
                {
                    Assert.Equal(0, await RenderedCountAsync(cdp, other));
                }
                if (expected != "") Assert.Equal(1, await RenderedCountAsync(cdp, expected));
                await page.Locator(".video-player-modal-backdrop").PressAsync(step < 2 ? "n" : "p");
                await Expect(page.Locator(".video-player-modal-code")).Not.ToHaveTextAsync(code);
            }
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
