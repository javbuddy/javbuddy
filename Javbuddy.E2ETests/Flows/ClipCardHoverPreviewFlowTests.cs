using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>A hovered clip card plays its preview in the browser (ClipCard.razor.js's bindHoverPreviews),
/// not through a Blazor mouseenter/mouseleave handler: that cost a server round trip and a re-render for
/// every card the pointer crossed. bUnit can't see this, since the playing is plain DOM script.</summary>
[Collection(E2ECollection.Name)]
public class ClipCardHoverPreviewFlowTests(E2EFixture fixture)
{
    private const int SceneCount = 12;

    // The app and its database are shared by every test in the collection: each seeding gets its own movie, released
    // after the previous one's (the wall lists newest first), so its scenes are the first SceneCount cards.
    private static int seedings;

    /// <summary>One movie with scenes that each have a screenshot and a preview already generated (the files'
    /// bytes don't matter: the test looks at the DOM, not at the picture).</summary>
    private async Task SeedScenesWithMediaAsync()
    {
        var seeding = Interlocked.Increment(ref seedings);
        var movie = new Movie
        {
            Code = $"E2E-HOVER-{seeding}",
            Status = MovieStatus.Got,
            MetaTitle = "Hover movie",
            MediaDurationSeconds = 3600,
            MetaFetchedAt = DateTime.UtcNow,
            MetaReleaseDate = new DateTime(2099, 1, 1).AddDays(seeding),
        };
        for (var i = 0; i < SceneCount; i++)
        {
            movie.Scenes.Add(new Scene { StartSeconds = i * 100, EndSeconds = i * 100 + 50, Title = $"Scene {i + 1}" });
        }
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var cache = fixture.App.Services.GetRequiredService<ILocalImageCache>();
        foreach (var scene in await db.Scenes.Where(s => s.MovieId == movie.Id).ToListAsync())
        {
            var window = SceneMediaService.Window(scene.StartSeconds, scene.EndSeconds);
            foreach (var variant in new[] { SceneMediaService.VariantThumb, SceneMediaService.VariantPreview })
            {
                var row = new CachedImage
                {
                    Code = movie.Code,
                    Role = SceneMediaService.Role,
                    Index = scene.Id,
                    Variant = variant,
                    StorageId = Guid.NewGuid(),
                    SceneStartMs = window.StartMs,
                    SceneEndMs = window.EndMs,
                    UpdatedAt = DateTime.UtcNow,
                };
                var path = cache.GetStoragePath(row);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "x");
                db.CachedImages.Add(row);
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task<(float X, float Y)> CenterOfAsync(IPage page, int cardIndex)
    {
        var box = (await page.Locator(".scene-wall-card").Nth(cardIndex).BoundingBoxAsync())!;
        return (box.X + box.Width / 2, box.Y + 40);
    }

    [Fact]
    public async Task ARestingPointer_PlaysThePreview_AndLeavingRemovesIt()
    {
        await SeedScenesWithMediaAsync();
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoInteractiveAsync("/movies/scenes");
        await Expect(page.Locator(".scene-wall-card").Nth(SceneCount - 1)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!window.Blazor");
        Assert.Equal(0, await page.Locator(".scene-wall-card video").CountAsync());

        var (x, y) = await CenterOfAsync(page, 0);
        await page.Mouse.MoveAsync(x, y);

        var video = page.Locator(".scene-wall-card video");
        await Expect(video).ToHaveCountAsync(1);
        await Expect(video).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex(@"^/scene-image/\d+/preview\?v=\d+$"));
        Assert.True(await video.EvaluateAsync<bool>("v => v.muted && v.loop && v.autoplay"));
        // It plays inside the card the pointer is on, over that card's screenshot.
        Assert.Equal(1, await page.Locator(".scene-wall-card").Nth(0).Locator("video").CountAsync());

        await page.Mouse.MoveAsync(5, 5);

        await Expect(page.Locator(".scene-wall-card video")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task SweepingAcrossCards_StartsNoPreview_AndSendsNothingToTheServer()
    {
        await SeedScenesWithMediaAsync();
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        var framesSent = 0;
        page.WebSocket += (_, ws) => ws.FrameSent += (_, _) => Interlocked.Increment(ref framesSent);
        await page.GotoInteractiveAsync("/movies/scenes");
        await Expect(page.Locator(".scene-wall-card").Nth(SceneCount - 1)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!window.Blazor");
        await page.EvaluateAsync(
            """
            () => {
                window.__videosAdded = 0;
                new MutationObserver(ms => ms.forEach(m => m.addedNodes.forEach(n => { if (n.nodeName === 'VIDEO') window.__videosAdded++; })))
                    .observe(document.body, { childList: true, subtree: true });
            }
            """);
        await page.WaitForTimeoutAsync(300);
        var framesBefore = framesSent;

        // Across every card in turn, well inside the dwell time on each.
        for (var i = 0; i < SceneCount; i++)
        {
            var (x, y) = await CenterOfAsync(page, i);
            await page.Mouse.MoveAsync(x, y);
            await page.WaitForTimeoutAsync(40);
        }
        await page.Mouse.MoveAsync(5, 5);
        await page.WaitForTimeoutAsync(400);

        Assert.Equal(0, await page.EvaluateAsync<int>("window.__videosAdded"));

        // Then rest on one card for each of a few cards: every one plays, and none of it reaches the server.
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = await CenterOfAsync(page, i);
            await page.Mouse.MoveAsync(x, y);
            await Expect(page.Locator(".scene-wall-card video")).ToHaveCountAsync(1);
        }
        await page.Mouse.MoveAsync(5, 5);
        await Expect(page.Locator(".scene-wall-card video")).ToHaveCountAsync(0);
        Assert.Equal(4, await page.EvaluateAsync<int>("window.__videosAdded"));

        // A Blazor mouseenter/mouseleave handler sent a few frames for every card crossed (about 3.4 each
        // when measured, 16 cards here); only the connection's own keep-alive can show up now.
        Assert.True(framesSent - framesBefore <= 2, $"Hovering sent {framesSent - framesBefore} frames to the server.");
    }
}
