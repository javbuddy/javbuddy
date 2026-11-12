using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Movie Detail's Images section shows a translucent delete button in the top-right corner of a
/// hovered image. Its styles are scoped to MovieImagesSection and reach the button through ::deep, so they
/// only apply while an element of that component sits above the gallery; bUnit has no CSS to notice them lost.</summary>
[Collection(E2ECollection.Name)]
public class MovieImagesDeleteButtonFlowTests(E2EFixture fixture)
{
    // A 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task HoveringAnImage_ShowsATranslucentDeleteButtonInItsTopRightCorner()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-IMGDEL-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        var folder = Directory.CreateDirectory(Path.Combine(DbSeeding.PlayableLibraryRoot, movie.Code!, "extrafanart")).FullName;
        foreach (var n in new[] { "fanart1.png", "fanart2.png" })
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, n), Png);
        }

        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        var cell = page.Locator(".fanart-thumb-cell").First;
        await cell.ScrollIntoViewIfNeededAsync();
        await cell.HoverAsync();
        var button = cell.Locator(".movie-image-delete-btn");
        await Expect(button).ToBeVisibleAsync();

        var cellBox = (await cell.BoundingBoxAsync())!;
        var box = (await button.BoundingBoxAsync())!;
        Assert.True(box.X + box.Width > cellBox.X + cellBox.Width - 20 && box.X + box.Width <= cellBox.X + cellBox.Width,
            $"Delete button's right edge {box.X + box.Width} isn't at the image's right edge {cellBox.X + cellBox.Width}.");
        Assert.True(box.Y >= cellBox.Y && box.Y < cellBox.Y + 20,
            $"Delete button's top {box.Y} isn't at the image's top {cellBox.Y}.");
        var background = await button.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Matches(@"^rgba\(0, 0, 0, 0\.\d+\)$", background);
    }
}
