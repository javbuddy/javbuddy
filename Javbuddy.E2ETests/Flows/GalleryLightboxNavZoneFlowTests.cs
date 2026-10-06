using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The prev/next targets are the full-height strips beside the image, not
/// just the round arrow icons — a click anywhere beside the image navigates, and only a click
/// above/below it closes the lightbox. Needs the real CSS layout, so it can't be a bUnit test.</summary>
[Collection(E2ECollection.Name)]
public class GalleryLightboxNavZoneFlowTests(E2EFixture fixture)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ClickingBesideTheImage_Navigates_AndClickingBelowIt_Closes(int viewportWidth)
    {
        fixture.ResetFakes();
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, $"NavZone{viewportWidth}");
        var cache = fixture.App.Services.GetRequiredService<ILocalImageCache>();
        var storageId = Guid.NewGuid();
        var path = cache.GetStoragePath(storageId);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var bitmap = new SKBitmap(200, 300))
            {
                bitmap.Erase(SKColors.CornflowerBlue);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
                await File.WriteAllBytesAsync(path, encoded.ToArray());
            }
            await DbSeeding.SeedActorPhotosAsync(fixture.DbFactory, actor.Id, 3, [(storageId, 200 / 300d)]);

            var page = await fixture.NewPageAsync();
            await page.SetViewportSizeAsync(viewportWidth, 900);
            await page.GotoInteractiveAsync("/actors/photos");
            await page.GetByPlaceholder("Filter by actor or album…").FillAsync(actor.FirstName!);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(3);
            await page.Locator(".actor-photo-tile").First.ClickAsync();
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("1 / 3");
            await page.WaitForFunctionAsync("() => document.querySelector('.gallery-image')?.naturalWidth > 0");
            var box = (await page.Locator(".gallery-image").BoundingBoxAsync())!;

            // Just beside the image edge and well above the vertically-centred arrow icons — this
            // used to land on the backdrop and close the lightbox.
            var highY = box.Y + 10;
            await page.Mouse.ClickAsync(box.X + box.Width + 8, highY);
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("2 / 3");
            await page.Mouse.ClickAsync(box.X + box.Width + 8, box.Y + box.Height - 10);
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("3 / 3");
            await page.Mouse.ClickAsync(box.X - 8, highY);
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("2 / 3");

            // Clicking the image itself does nothing.
            await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("2 / 3");

            // Below the image is still backdrop — dismisses the lightbox.
            await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height + 20);
            await Expect(page.Locator(".gallery-lightbox")).ToHaveCountAsync(0);
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        }
        finally
        {
            await fixture.CloseOpenPagesAsync();
            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            await db.ActorPhotos.Where(p => p.ActorId == actor.Id).ExecuteDeleteAsync();
            await db.Actors.Where(a => a.Id == actor.Id).ExecuteDeleteAsync();
            File.Delete(path);
        }
    }
}
