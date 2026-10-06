using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActorMergeFlowTests
{
    private readonly E2EFixture fixture;

    public ActorMergeFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    private static byte[] CreateTestPng(SKColor color)
    {
        using var bitmap = new SKBitmap(140, 140);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var circlePaint = new SKPaint { Color = SKColors.White.WithAlpha(180), IsAntialias = true };
        canvas.DrawCircle(70, 70, 45, circlePaint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task MergeActor_MergesSourceIntoTarget_AndNavigatesToTargetWithAlias()
    {
        Actor source = new() { FirstName = "E2E Merge Source", LastName = "Actor" };
        Actor target = new() { FirstName = "E2E Merge Target", LastName = "Actor", JapaneseNameKanji = "結合対象", JapaneseNameKana = "けつごう たいしょう" };

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.App.Services.CreateScope())
        {
            var imageCache = scope.ServiceProvider.GetRequiredService<IActorImageCacheService>();
            await imageCache.SaveCustomImageAsync(source.Id, CreateTestPng(new SKColor(217, 83, 79)));
            await imageCache.SaveCustomImageAsync(target.Id, CreateTestPng(new SKColor(46, 139, 87)));
        }

        const string sourceDisplayName = "Actor E2E Merge Source";
        const string targetDisplayName = "Actor E2E Merge Target";
        var page = await fixture.NewPageAsync();

        // Navigate to source actor profile
        await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(sourceDisplayName)}");
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(sourceDisplayName);

        // Click Merge in toolbar
        await page.Locator(".actor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Merge..." }).ClickAsync();
        await Expect(page.Locator(".merge-modal-backdrop")).ToBeVisibleAsync();
        await Expect(page.Locator(".merge-modal-title")).ToContainTextAsync($"Merge Actor — {sourceDisplayName}");

        // Select target actor from candidate list
        var candidate = page.Locator(".candidate-item", new() { HasText = targetDisplayName });
        await candidate.ClickAsync();

        // Click confirm merge button
        var mergeBtn = page.Locator(".merge-modal-footer").GetByRole(AriaRole.Button, new() { Name = $"Merge into {targetDisplayName}" });
        await mergeBtn.ClickAsync();

        // Browser navigates to target canonical profile
        await Expect(page).ToHaveURLAsync(new Regex($"/actors/{Regex.Escape(Uri.EscapeDataString(targetDisplayName))}$"));
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(targetDisplayName);

        // Aliases are displayed on target profile
        await Expect(page.Locator(".actor-hero-aliases")).ToContainTextAsync(sourceDisplayName);
    }
}
