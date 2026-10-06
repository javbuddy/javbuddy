using System.Collections.Concurrent;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActorPhotosLoadingTests(E2EFixture fixture)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, false)]
    [InlineData(1440, true)]
    public async Task LargeGallery_LoadsBatches_AndReconnectsLoadingAfterFiltering(int viewportWidth, bool withoutObserver)
    {
        fixture.ResetFakes();
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, $"GalleryScale{viewportWidth}");
        var cache = fixture.App.Services.GetRequiredService<ILocalImageCache>();
        var images = new List<(Guid StorageId, double AspectRatio)>();
        var imagePaths = new List<string>();
        try
        {
            foreach (var width in new[] { 200, 300, 450 })
            {
                var storageId = Guid.NewGuid();
                var path = cache.GetStoragePath(storageId);
                imagePaths.Add(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var bitmap = new SKBitmap(width, 300);
                bitmap.Erase(SKColors.CornflowerBlue);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
                await File.WriteAllBytesAsync(path, encoded.ToArray());
                images.Add((storageId, width / 300d));
            }
            await DbSeeding.SeedActorPhotosAsync(fixture.DbFactory, actor.Id, 10020, images);

            var page = await fixture.NewPageAsync();
            await page.SetViewportSizeAsync(viewportWidth, 900);
            if (withoutObserver) await page.AddInitScriptAsync("delete window.IntersectionObserver;");
            var errors = new ConcurrentQueue<string>();
            var photoRequests = new ConcurrentQueue<string>();
            page.PageError += (_, error) => errors.Enqueue(error);
            page.Request += (_, request) =>
            {
                if (request.Url.Contains("/actor-photo/", StringComparison.Ordinal)) photoRequests.Enqueue(request.Url);
            };
            await page.GotoInteractiveAsync("/actors/photos");
            // Filtering also makes this independent of other tests' photos in the shared fixture.
            var search = page.GetByPlaceholder("Filter by actor or album…");
            await search.FillAsync(actor.FirstName!);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(120);
            await page.WaitForFunctionAsync("() => document.querySelector('.actor-photo-tile-img')?.naturalWidth > 0");
            Assert.InRange(photoRequests.Count, 1, withoutObserver ? 250 : 100);

            // The scrollbar should already reflect the full 10,020-photo collection
            // via the reserved placeholder, so loading another batch shouldn't grow the page height.
            await page.WaitForFunctionAsync("() => document.querySelector('.actor-photos-placeholder') !== null");
            var scrollHeightBeforeBatch = await page.EvaluateAsync<double>("document.documentElement.scrollHeight");
            await LoadNextBatchAsync(page, withoutObserver);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(240);
            var scrollHeightAfterBatch = await page.EvaluateAsync<double>("document.documentElement.scrollHeight");
            Assert.InRange(scrollHeightAfterBatch, scrollHeightBeforeBatch * 0.9, scrollHeightBeforeBatch * 1.1);
            await page.Locator(".actor-photo-tile").Nth(120).ScrollIntoViewIfNeededAsync();
            await page.WaitForFunctionAsync("() => document.querySelectorAll('.actor-photo-tile-img')[120]?.naturalWidth > 0");
            await page.Locator(".actor-photo-tile").Nth(120).ClickAsync();
            await Expect(page.Locator(".gallery-lightbox")).ToBeVisibleAsync();
            await Expect(page.Locator(".gallery-index")).ToHaveTextAsync("121 / 10020");
            await page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

            await page.EvaluateAsync("window.scrollTo(0, 0)");
            await search.FillAsync("no-matching-actor-303");
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(0);
            await search.FillAsync(actor.FirstName!);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(120);
            await LoadNextBatchAsync(page, withoutObserver);
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(240);

            await page.EvaluateAsync("Blazor.navigateTo('/actors')");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Actors");
            await page.EvaluateAsync("Blazor.navigateTo('/actors/photos')");
            await Expect(page.Locator(".actor-photo-tile")).ToHaveCountAsync(120);
            await page.WaitForFunctionAsync("() => document.querySelector('.actor-photo-tile-img')?.naturalWidth > 0");
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
            Assert.Empty(errors);
        }
        finally
        {
            await fixture.CloseOpenPagesAsync();
            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            await db.ActorPhotos.Where(p => p.ActorId == actor.Id).ExecuteDeleteAsync();
            await db.Actors.Where(a => a.Id == actor.Id).ExecuteDeleteAsync();
            foreach (var path in imagePaths) File.Delete(path);
        }
    }

    // Scrolls to the end of the rendered batch rather than the page bottom: the page
    // bottom lies inside the placeholder, which keeps triggering batch after batch while it stays
    // in view, so the exact 240-tile count would only be observed by luck.
    private static Task LoadNextBatchAsync(IPage page, bool withoutObserver)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Load more photos" });
        return withoutObserver ? button.ClickAsync() : button.ScrollIntoViewIfNeededAsync();
    }
}
