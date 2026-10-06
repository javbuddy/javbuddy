using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActorPhotoUploadFlowTests
{
    private readonly E2EFixture fixture;

    public ActorPhotoUploadFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task UploadActorPhoto_ShowsCropper_SavesAndReverts()
    {
        int actorId;
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Rena",
                LastName = "Takeda"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        const string displayName = "Takeda Rena";
        var page = await fixture.NewPageAsync();

        // 1. Navigate to profile
        await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(displayName)}");
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(displayName);

        // 2. Open Change Photo modal
        var changePhotoBtn = page.Locator(".actor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Change Photo" });
        await Expect(changePhotoBtn).ToBeVisibleAsync();
        await changePhotoBtn.ClickAsync();

        var modal = page.Locator(".crop-modal-panel");
        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal.Locator(".crop-modal-title")).ToContainTextAsync($"Change Portrait — {displayName}");

        // 3. Upload a sample image file via file input
        var tempImagePath = Path.Combine(Path.GetTempPath(), $"test-actor-{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new SkiaSharp.SKBitmap(200, 200))
            {
                using (var canvas = new SkiaSharp.SKCanvas(bmp))
                {
                    canvas.Clear(SkiaSharp.SKColors.Coral);
                }
                using var img = SkiaSharp.SKImage.FromBitmap(bmp);
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(tempImagePath, data.ToArray());
            }

            var fileInput = modal.Locator("input[type='file']");
            await fileInput.SetInputFilesAsync(tempImagePath);

            // 4. Cropper toolbar and previews should appear
            await Expect(modal.Locator(".cropper-toolbar")).ToBeVisibleAsync();
            await Expect(modal.GetByRole(AriaRole.Button, new() { Name = "1:1 Square" })).ToBeVisibleAsync();
            await Expect(modal.GetByRole(AriaRole.Button, new() { Name = "Free-form" })).ToBeVisibleAsync();
            await Expect(modal.GetByRole(AriaRole.Button, new() { Name = "2:3 Portrait" })).ToBeVisibleAsync();
            await Expect(modal.Locator(".preview-canvas").First).ToBeVisibleAsync();

            // 5. Save photo
            var saveBtn = modal.GetByRole(AriaRole.Button, new() { Name = "Save Photo" });
            await saveBtn.ClickAsync();

            // Modal closes and photo displays
            await Expect(modal).Not.ToBeVisibleAsync();
            var heroImg = page.Locator(".actor-hero-avatar-photo img");
            await Expect(heroImg).ToBeVisibleAsync();
            var src = await heroImg.GetAttributeAsync("src");
            Assert.NotNull(src);
            Assert.Contains($"/actor-image/{actorId}/full", src);

            // 6. Reopen modal and verify Active Custom Photo is indicated
            await changePhotoBtn.ClickAsync();
            await Expect(modal).ToBeVisibleAsync();
            await Expect(modal).ToContainTextAsync("Custom Portrait Active");

            // 7. Click Revert to Library Photo
            var revertBtn = modal.GetByRole(AriaRole.Button, new() { Name = "Revert to Library Photo" });
            await revertBtn.ClickAsync();

            await Expect(modal).Not.ToBeVisibleAsync();
            // Reverted back to initials avatar since no .actors/ or ThumbnailUrl is present
            await Expect(page.Locator(".actor-hero-avatar")).ToContainTextAsync("TR");
        }
        finally
        {
            if (File.Exists(tempImagePath))
            {
                try { File.Delete(tempImagePath); } catch { }
            }
        }
    }

    [Fact]
    public async Task UploadActorPhoto_NavigatingBackToActorsGrid_DisplaysUpdatedThumbnail()
    {
        int actorId;
        const string firstName = "TestActress";
        const string lastName = "BackNav";
        const string displayName = $"{lastName} {firstName}";

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = firstName,
                LastName = lastName
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var page = await fixture.NewPageAsync();

        // 1. Navigate to /actors first
        await page.GotoInteractiveAsync("/actors");
        var actorCard = page.Locator(".actor-card", new() { HasText = displayName });
        await Expect(actorCard).ToBeVisibleAsync();
        // Initially has initials avatar
        await Expect(actorCard.Locator(".actor-avatar")).ToContainTextAsync("BT");

        // 2. Click actor card to navigate to profile
        await actorCard.Locator(".actor-card-link").ClickAsync();
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(displayName);

        // 3. Upload initial photo
        var changePhotoBtn = page.Locator(".actor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Change Photo" });
        await changePhotoBtn.ClickAsync();
        var modal = page.Locator(".crop-modal-panel");
        await Expect(modal).ToBeVisibleAsync();

        var tempImagePath1 = Path.Combine(Path.GetTempPath(), $"test-actor-1-{Guid.NewGuid():N}.png");
        var tempImagePath2 = Path.Combine(Path.GetTempPath(), $"test-actor-2-{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new SkiaSharp.SKBitmap(200, 200))
            {
                using (var canvas = new SkiaSharp.SKCanvas(bmp))
                {
                    canvas.Clear(SkiaSharp.SKColors.AliceBlue);
                }
                using var img = SkiaSharp.SKImage.FromBitmap(bmp);
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(tempImagePath1, data.ToArray());
            }

            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath1);
            await Expect(modal.Locator(".cropper-toolbar")).ToBeVisibleAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Save Photo" }).ClickAsync();
            await Expect(modal).Not.ToBeVisibleAsync();

            // 4. Navigate back to /actors — should fetch thumbnail with no-cache and an ETag
            var thumbResponse1 = await page.RunAndWaitForResponseAsync(
                () => page.GoBackAsync(),
                r => r.Url.Contains($"/actor-image/{actorId}/thumb"));

            Assert.Equal(200, thumbResponse1.Status);
            var cacheControl = thumbResponse1.Headers.GetValueOrDefault("cache-control");
            Assert.Contains("no-cache", cacheControl);
            var etag1 = thumbResponse1.Headers.GetValueOrDefault("etag");
            Assert.NotNull(etag1);

            var updatedCard = page.Locator(".actor-card", new() { HasText = displayName });
            await Expect(updatedCard.Locator(".actor-avatar-photo img")).ToBeVisibleAsync();

            // 5. Navigate back to profile and upload a REPLACEMENT photo
            await updatedCard.Locator(".actor-card-link").ClickAsync();
            await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(displayName);

            await changePhotoBtn.ClickAsync();
            await Expect(modal).ToBeVisibleAsync();

            using (var bmp = new SkiaSharp.SKBitmap(200, 200))
            {
                using (var canvas = new SkiaSharp.SKCanvas(bmp))
                {
                    canvas.Clear(SkiaSharp.SKColors.DarkOrange);
                }
                using var img = SkiaSharp.SKImage.FromBitmap(bmp);
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(tempImagePath2, data.ToArray());
            }

            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath2);
            await Expect(modal.Locator(".cropper-toolbar")).ToBeVisibleAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Save Photo" }).ClickAsync();
            await Expect(modal).Not.ToBeVisibleAsync();

            // 6. Navigate back to /actors again via browser back
            var thumbResponse2 = await page.RunAndWaitForResponseAsync(
                () => page.GoBackAsync(),
                r => r.Url.Contains($"/actor-image/{actorId}/thumb"));

            Assert.Equal(200, thumbResponse2.Status);
            var etag2 = thumbResponse2.Headers.GetValueOrDefault("etag");
            Assert.NotNull(etag2);
            Assert.NotEqual(etag1, etag2);

            await Expect(page.Locator(".actor-card", new() { HasText = displayName }).Locator(".actor-avatar-photo img")).ToBeVisibleAsync();
        }
        finally
        {
            try { if (File.Exists(tempImagePath1)) File.Delete(tempImagePath1); } catch { }
            try { if (File.Exists(tempImagePath2)) File.Delete(tempImagePath2); } catch { }
        }
    }

    [Fact]
    public async Task ActorDetail_ClickingHeadshot_OpensLightbox_AndDismissesOnEscape()
    {
        int actorId;
        const string firstName = "Lightbox";
        const string lastName = "Actress";
        const string displayName = $"{lastName} {firstName}";

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = firstName,
                LastName = lastName
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var page = await fixture.NewPageAsync();

        // 1. Navigate to actor detail
        await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(displayName)}");
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(displayName);

        // 2. Initially no photo: clicking avatar opens upload modal
        var avatarInitials = page.Locator(".actor-hero-avatar:not(.actor-hero-avatar-photo)");
        await Expect(avatarInitials).ToHaveAttributeAsync("title", "Click to upload photo");
        await avatarInitials.ClickAsync();
        await Expect(page.Locator(".crop-modal-panel")).ToBeVisibleAsync();
        await page.Locator(".crop-modal-close-btn").ClickAsync();
        await Expect(page.Locator(".crop-modal-panel")).Not.ToBeVisibleAsync();

        // 3. Upload photo
        var changePhotoBtn = page.Locator(".actor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Change Photo" });
        await changePhotoBtn.ClickAsync();
        var modal = page.Locator(".crop-modal-panel");
        await Expect(modal).ToBeVisibleAsync();

        var tempImagePath = Path.Combine(Path.GetTempPath(), $"test-actor-lightbox-{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new SkiaSharp.SKBitmap(200, 200))
            {
                using (var canvas = new SkiaSharp.SKCanvas(bmp))
                {
                    canvas.Clear(SkiaSharp.SKColors.LimeGreen);
                }
                using var img = SkiaSharp.SKImage.FromBitmap(bmp);
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(tempImagePath, data.ToArray());
            }

            await modal.Locator("input[type='file']").SetInputFilesAsync(tempImagePath);
            await Expect(modal.Locator(".cropper-toolbar")).ToBeVisibleAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Save Photo" }).ClickAsync();
            await Expect(modal).Not.ToBeVisibleAsync();

            // 4. Hero avatar now has photo and updated tooltip
            var avatarPhoto = page.Locator(".actor-hero-avatar-photo");
            await Expect(avatarPhoto).ToBeVisibleAsync();
            await Expect(avatarPhoto).ToHaveAttributeAsync("title", "Click to view large photo");

            // 5. Click headshot opens lightbox
            await avatarPhoto.ClickAsync();
            var lightbox = page.Locator(".image-lightbox-backdrop");
            await Expect(lightbox).ToBeVisibleAsync();
            await Expect(page.Locator(".image-lightbox-image")).ToBeVisibleAsync();
            await Expect(page.Locator(".image-lightbox-name")).ToContainTextAsync(displayName);

            // 6. Dismiss via Escape key
            await page.Keyboard.PressAsync("Escape");
            await Expect(lightbox).Not.ToBeVisibleAsync();

            // 6b. Escape works even before the backdrop has received focus: the focus
            // call is a server round-trip after the lightbox is inserted, so fire Escape in the very
            // same DOM mutation, while focus is still on <body>. A real key press racing that
            // window made this test flaky.
            await page.EvaluateAsync("""
                () => {
                    const observer = new MutationObserver(() => {
                        if (!document.querySelector('.image-lightbox-backdrop')) return;
                        observer.disconnect();
                        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                    });
                    observer.observe(document.body, { childList: true, subtree: true });
                }
                """);
            await avatarPhoto.ClickAsync();
            await Expect(lightbox).Not.ToBeVisibleAsync();

            // 7. Click again and dismiss via close button
            await avatarPhoto.ClickAsync();
            await Expect(lightbox).ToBeVisibleAsync();
            await page.Locator(".image-lightbox-close-btn").ClickAsync();
            await Expect(lightbox).Not.ToBeVisibleAsync();
        }
        finally
        {
            try { if (File.Exists(tempImagePath)) File.Delete(tempImagePath); } catch { }
        }
    }
}

