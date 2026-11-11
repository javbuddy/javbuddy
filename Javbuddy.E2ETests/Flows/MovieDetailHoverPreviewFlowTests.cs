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

/// <summary>Movie Detail's Scenes section plays a hovered card's or apex marker's preview in the browser
/// (ClipCard.razor.js's bindHoverPreviews), not through a Blazor mouseenter/mouseleave handler: each hover
/// was a server round trip that re-rendered the whole section. bUnit can't see this, since the playing is
/// plain DOM script and the popover's showing is CSS.</summary>
[Collection(E2ECollection.Name)]
public class MovieDetailHoverPreviewFlowTests(E2EFixture fixture)
{
    private static int seedings;

    private async Task<string> SeedAsync()
    {
        var code = $"E2E-MDHOVER-{Interlocked.Increment(ref seedings)}";
        var movie = new Movie
        {
            Code = code,
            Status = MovieStatus.Got,
            MetaTitle = "Hover movie",
            MediaDurationSeconds = 3600,
            MetaFetchedAt = DateTime.UtcNow,
        };
        movie.Scenes.Add(new Scene { StartSeconds = 0, EndSeconds = 50, Title = "Hover scene" });
        movie.Apexes.Add(new MovieApex { Seconds = 900 });
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var cache = fixture.App.Services.GetRequiredService<ILocalImageCache>();
        var scene = await db.Scenes.SingleAsync(s => s.MovieId == movie.Id);
        var apex = await db.MovieApexes.SingleAsync(a => a.MovieId == movie.Id);
        var rows = new List<(string Role, int Index, SceneMediaWindow Window)>
        {
            (SceneMediaService.Role, scene.Id, SceneMediaService.Window(scene.StartSeconds, scene.EndSeconds)),
            (ApexMediaService.Role, apex.Id, ApexMediaService.Window(apex.Seconds)),
        };
        foreach (var (role, index, window) in rows)
        {
            foreach (var variant in new[] { SceneMediaService.VariantThumb, SceneMediaService.VariantPreview })
            {
                var row = new CachedImage
                {
                    Code = movie.Code,
                    Role = role,
                    Index = index,
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
        return code;
    }

    [Fact]
    public async Task HoveringACard_PlaysItsPreview_AndLeavingRemovesIt()
    {
        var code = await SeedAsync();
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoInteractiveAsync($"/movies/{code}");
        var card = page.Locator(".movie-scenes-grid .movie-scenes-card").First;
        await card.ScrollIntoViewIfNeededAsync();
        await page.WaitForSelectorAsync("[data-hover-bound]", new() { State = WaitForSelectorState.Attached });
        Assert.Equal(0, await page.Locator(".movie-scenes video").CountAsync());

        await card.HoverAsync();

        var video = card.Locator("video");
        await Expect(video).ToHaveCountAsync(1);
        await Expect(video).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex(@"^/scene-image/\d+/preview\?v=\d+$"));

        await page.Mouse.MoveAsync(5, 5);

        await Expect(page.Locator(".movie-scenes video")).ToHaveCountAsync(0);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task HoveringAnApexMarker_ShowsItsPopoverAndPlaysItsClip_UntilThePointerLeaves()
    {
        var code = await SeedAsync();
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoInteractiveAsync($"/movies/{code}");
        var marker = page.Locator(".movie-scenes-timeline-apex");
        await marker.ScrollIntoViewIfNeededAsync();
        await page.WaitForSelectorAsync("[data-hover-bound]", new() { State = WaitForSelectorState.Attached });
        var popover = page.Locator(".movie-scenes-timeline-apex-popover");
        // The popover is zero-height (its card is positioned out of it), so what is shown is the card.
        var card = popover.Locator(".apex-preview");
        await Expect(card).ToBeHiddenAsync();

        await marker.HoverAsync(new() { Force = true });

        await Expect(card).ToBeVisibleAsync();
        await Expect(popover.Locator(".apex-preview-time")).ToHaveTextAsync("15:00");
        var video = popover.Locator("video");
        await Expect(video).ToHaveCountAsync(1);
        await Expect(video).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex(@"^/apex-image/\d+/preview\?v=\d+$"));

        await page.Mouse.MoveAsync(5, 5);

        await Expect(card).ToBeHiddenAsync();
        await Expect(page.Locator(".movie-scenes-timeline-apex-popover video")).ToHaveCountAsync(0);
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }
}
