using System.Net;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>A movie's locally generated trickplay in the real player: the scrub bar's
/// hover preview loads its tile sheets from the app's own tile endpoint, ahead of the Jellyfin
/// trickplay the same movie also has.</summary>
[Collection(E2ECollection.Name)]
public class LocalTrickplayFlowTests
{
    private readonly E2EFixture fixture;

    public LocalTrickplayFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task ScrubBarPreview_UsesTheLocalTiles_OverJellyfins()
    {
        const string itemId = "fake-jf-item-local-tp-1";
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
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-LOCAL-TP-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = await db.Movies.FindAsync(movie.Id);
            m!.JellyfinItemId = itemId;
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var identity = await DbSeeding.SeedLocalTrickplayAsync(fixture.DbFactory, movie);
        var tileUrl = $"/trickplay/{movie.Id}/{identity}/0.webp";

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-detail-badge-play").ClickAsync();
            var track = page.Locator(".video-player-column .scrub-bar-track");
            await Expect(track).ToBeVisibleAsync();
            // The ScrubBar's JS module sizes the thumb once it has initialised.
            await page.WaitForFunctionAsync("() => document.querySelector('.scrub-bar-thumb')?.style.width !== ''");

            await track.HoverAsync();

            await page.WaitForFunctionAsync(
                $"() => document.querySelector('.scrub-bar-thumb').dataset.sheet === '{tileUrl}'");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }

        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var tile = await client.GetAsync(tileUrl);
        Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        Assert.Equal("image/webp", tile.Content.Headers.ContentType?.MediaType);
        using var missing = await client.GetAsync($"/trickplay/{movie.Id}/{identity}/1.webp");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
