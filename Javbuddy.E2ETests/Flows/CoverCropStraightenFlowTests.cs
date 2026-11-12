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
            await modal.GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();
            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath);

            var slider = modal.Locator(".straighten-slider");
            await Expect(slider).ToBeVisibleAsync();
            var image = modal.Locator(".cropper-viewport img");
            await Expect(image).ToHaveJSPropertyAsync("complete", true);

            // Straighten by 20° (the slider drives the cropper directly in the browser).
            await slider.FillAsync("20");
            await Expect(modal.Locator(".straighten-controls .slider-value")).ToContainTextAsync("20.0");
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

            // Reset restores the unrotated layout.
            await modal.Locator(".cropper-toolbar > .btn-tool", new() { HasTextString = "Reset" }).ClickAsync();
            await Expect(image).ToHaveCSSAsync("transform", "none");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            File.Delete(tempImagePath);
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ZoomAndStraightenSliders_DriveTheCropperInTheBrowser_AndSaveStillWorks()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SLIDERS-1", MovieStatus.Missing);

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
            await modal.GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();
            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath);

            var straighten = modal.Locator(".straighten-slider");
            var zoom = modal.Locator(".zoom-slider");
            var straightenReadout = modal.Locator(".straighten-controls .slider-value");
            var zoomReadout = modal.Locator(".zoom-controls .slider-value");
            var image = modal.Locator(".cropper-viewport img");
            await Expect(image).ToHaveJSPropertyAsync("complete", true);
            await Expect(straightenReadout).ToHaveTextAsync("0.0°");
            await Expect(zoomReadout).ToHaveTextAsync("1.0×");

            // Zoom slider: the readout and the cropper's image size follow the slider.
            var widthBefore = await image.EvaluateAsync<double>("e => parseFloat(e.style.width)");
            await zoom.FillAsync("2");
            await Expect(zoomReadout).ToHaveTextAsync("2.0×");
            var widthAfter = await image.EvaluateAsync<double>("e => parseFloat(e.style.width)");
            Assert.True(widthAfter > widthBefore * 1.9, $"zoom 2 should about double the image width ({widthBefore} -> {widthAfter})");

            // Keyboard operation of the native sliders still works (and reaches the readout).
            await zoom.FocusAsync();
            await zoom.PressAsync("ArrowRight");
            await zoom.PressAsync("ArrowRight");
            await Expect(zoomReadout).ToHaveTextAsync("2.1×");

            // Wheel zoom moves the slider and the readout.
            await modal.Locator(".cropper-viewport").HoverAsync();
            await page.Mouse.WheelAsync(0, -100);
            await Expect(zoomReadout).ToHaveTextAsync("2.2×");
            Assert.Equal(2.2, double.Parse(await zoom.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture), 2);

            // Straighten slider: the readout follows, the rotation is applied, the zoom is kept.
            await straighten.FillAsync("-7.5");
            await Expect(straightenReadout).ToHaveTextAsync("-7.5°");
            await Expect(zoomReadout).ToHaveTextAsync("2.2×");
            await Expect(image).ToHaveCSSAsync("transform", new System.Text.RegularExpressions.Regex("^matrix\\("));

            // Reset puts both sliders and readouts back.
            await modal.Locator(".cropper-toolbar > .btn-tool", new() { HasTextString = "Reset" }).ClickAsync();
            await Expect(straightenReadout).ToHaveTextAsync("0.0°");
            await Expect(zoomReadout).ToHaveTextAsync("1.0×");
            Assert.Equal(0.0, double.Parse(await straighten.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture), 2);
            Assert.Equal(1.0, double.Parse(await zoom.InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture), 2);

            // Saving still gets the angle (read from the browser) and closes the dialog without an error.
            await straighten.FillAsync("4.5");
            await zoom.FillAsync("1.5");
            await modal.Locator(".crop-save-btn").ClickAsync();
            await Expect(modal).Not.ToBeVisibleAsync();
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            File.Delete(tempImagePath);
            await page.CloseAsync();
        }
    }
}
