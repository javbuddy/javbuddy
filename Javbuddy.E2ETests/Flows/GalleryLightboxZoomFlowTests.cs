using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The lightbox image zooms (wheel, double-click, top-bar buttons) and pans
/// by dragging, a drag doesn't close the viewer, and navigating resets the zoom. All of it is
/// client-side pointer/wheel handling in ImageZoomControls.razor.js, so it needs a real browser.</summary>
[Collection(E2ECollection.Name)]
public class GalleryLightboxZoomFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task WheelDoubleClickAndButtons_Zoom_DragPans_AndNavigatingResets()
    {
        fixture.ResetFakes();
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "ZoomFlow");
        var cache = fixture.App.Services.GetRequiredService<ILocalImageCache>();
        var storageId = Guid.NewGuid();
        var path = cache.GetStoragePath(storageId);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var bitmap = new SKBitmap(1000, 1500))
            {
                bitmap.Erase(SKColors.CornflowerBlue);
                using var skImage = SKImage.FromBitmap(bitmap);
                using var encoded = skImage.Encode(SKEncodedImageFormat.Webp, 80);
                await File.WriteAllBytesAsync(path, encoded.ToArray());
            }
            await DbSeeding.SeedActorPhotosAsync(fixture.DbFactory, actor.Id, 3, [(storageId, 1000 / 1500d)]);

            var page = await fixture.NewPageAsync();
            await page.SetViewportSizeAsync(1440, 900);
            await page.GotoInteractiveAsync("/actors/photos");
            await page.GetByPlaceholder("Filter by actor or album…").FillAsync(actor.FirstName!);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(3);
            await page.Locator(".actor-photo-tile").First.ClickAsync();
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("1 / 3");
            await page.WaitForFunctionAsync("() => document.querySelector('.gallery-image')?.naturalWidth > 0");

            var image = page.Locator(".gallery-image");
            var zoomOut = page.GetByRole(AriaRole.Button, new() { Name = "Zoom out" });
            var reset = page.GetByRole(AriaRole.Button, new() { Name = "Reset zoom" });
            await Expect(zoomOut).ToBeDisabledAsync();
            var box = (await image.BoundingBoxAsync())!;
            var centreX = box.X + box.Width / 2;
            var centreY = box.Y + box.Height / 2;

            // Wheel up over the image zooms in, and tells .NET (zoom out / reset enable).
            await page.Mouse.MoveAsync(centreX, centreY);
            await page.Mouse.WheelAsync(0, -300);
            await Expect(image).ToHaveClassAsync(IsZoomed);
            await Expect(zoomOut).ToBeEnabledAsync();
            var scaleAfterWheel = await ScaleAsync(image);
            Assert.True(scaleAfterWheel > 1.5, $"scale {scaleAfterWheel}");

            // Dragging pans, and the click that ends the drag neither closes nor navigates. Only
            // vertically here: zoomed, this portrait image is still narrower than the viewport, so
            // it stays centred horizontally.
            var before = await TranslateYAsync(image);
            await page.Mouse.MoveAsync(centreX, centreY);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(centreX, centreY + 80, new() { Steps = 5 });
            await page.Mouse.UpAsync();
            Assert.Equal(before + 80, await TranslateYAsync(image), 0.5);
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("1 / 3");

            // The top-bar buttons zoom further and reset.
            await page.GetByRole(AriaRole.Button, new() { Name = "Zoom in" }).ClickAsync();
            await page.WaitForFunctionAsync($"() => new DOMMatrix(getComputedStyle(document.querySelector('.gallery-image')).transform).a > {scaleAfterWheel + 0.1}");
            await reset.ClickAsync();
            await Expect(image).Not.ToHaveClassAsync(IsZoomed);
            await Expect(reset).ToBeDisabledAsync();

            // Double-click toggles fit ↔ 1:1 (the image's own pixels), and back.
            await image.DblClickAsync();
            await Expect(image).ToHaveClassAsync(IsZoomed);
            await page.WaitForFunctionAsync("() => { const el = document.querySelector('.gallery-image'); return Math.abs(new DOMMatrix(getComputedStyle(el).transform).a - el.naturalWidth / el.offsetWidth) < 0.01; }");
            await image.DblClickAsync();
            await Expect(image).Not.ToHaveClassAsync(IsZoomed);
            await image.DblClickAsync();
            await Expect(image).ToHaveClassAsync(IsZoomed);

            // Navigating to the next image resets the zoom.
            await page.Keyboard.PressAsync("ArrowRight");
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("2 / 3");
            await Expect(image).Not.ToHaveClassAsync(IsZoomed);
            Assert.Equal(1, await ScaleAsync(image));
            await Expect(zoomOut).ToBeDisabledAsync();
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

    private static readonly Regex IsZoomed = new("is-zoomed");

    private static Task<double> ScaleAsync(ILocator image) =>
        image.EvaluateAsync<double>("el => new DOMMatrix(getComputedStyle(el).transform).a");

    private static Task<double> TranslateYAsync(ILocator image) =>
        image.EvaluateAsync<double>("el => new DOMMatrix(getComputedStyle(el).transform).f");
}
