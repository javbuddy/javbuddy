using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Crop Cover's Straighten slider rotates the image inside the cropper in a real browser:
/// the crop box has to stay inside the rotated image (so no empty corner can be saved) and
/// the preview has to follow it. Needs the real layout/CSS transform, which bUnit can't give.</summary>
[Collection(E2ECollection.Name)]
public class CoverCropStraightenFlowTests
{
    private readonly E2EFixture fixture;

    public CoverCropStraightenFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task StraightenSlider_RotatesTheImage_AndKeepsTheCropBoxInsideIt()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STRAIGHTEN-1", MovieStatus.Missing);

        var tempImagePath = Path.Combine(Path.GetTempPath(), $"test-cover-{Guid.NewGuid():N}.png");
        var page = await fixture.NewPageAsync();
        try
        {
            using (var bmp = new SKBitmap(800, 500))
            {
                using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.SteelBlue);
                using var img = SKImage.FromBitmap(bmp);
                await File.WriteAllBytesAsync(tempImagePath, img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
            }

            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.GetByRole(AriaRole.Button, new() { Name = "Tools" }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Crop Cover" }).ClickAsync();

            var modal = page.Locator(".modal-shell-panel");
            await Expect(modal).ToBeVisibleAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Upload File" }).ClickAsync();
            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath);

            var slider = modal.Locator(".straighten-slider");
            await Expect(slider).ToBeVisibleAsync();
            var image = modal.Locator(".cropper-viewport img");
            await Expect(image).ToHaveJSPropertyAsync("complete", true);

            // Straighten by 20° (the Blazor oninput handler lays the cropper out again).
            await slider.FillAsync("20");
            await Expect(modal.Locator(".straighten-value")).ToContainTextAsync("20.0");
            await Expect(image).ToHaveCSSAsync("transform", new System.Text.RegularExpressions.Regex("^matrix\\("));

            // Every corner of the crop box must lie inside the rotated image (2px inset tolerance).
            var inside = await page.EvaluateAsync<bool>("""
                () => {
                    const img = document.querySelector('.cropper-viewport img');
                    const box = document.querySelector('.cropper-crop-box');
                    const left = parseFloat(img.style.left), top = parseFloat(img.style.top);
                    const w = parseFloat(img.style.width), h = parseFloat(img.style.height);
                    const cx = left + w / 2, cy = top + h / 2;
                    const rad = 20 * Math.PI / 180, c = Math.cos(rad), s = Math.sin(rad);
                    const bl = parseFloat(box.style.left), bt = parseFloat(box.style.top);
                    const bw = parseFloat(box.style.width), bh = parseFloat(box.style.height);
                    return [[bl, bt], [bl + bw, bt], [bl, bt + bh], [bl + bw, bt + bh]].every(([x, y]) => {
                        const dx = x - cx, dy = y - cy;
                        return Math.abs(dx * c + dy * s) <= w / 2 + 0.5 && Math.abs(-dx * s + dy * c) <= h / 2 + 0.5;
                    });
                }
                """);
            Assert.True(inside, "the crop box left the rotated image");

            // The 0° button restores the unrotated layout.
            await modal.Locator(".straighten-reset-btn").ClickAsync();
            await Expect(image).ToHaveCSSAsync("transform", "none");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            File.Delete(tempImagePath);
            await page.CloseAsync();
        }
    }
}
