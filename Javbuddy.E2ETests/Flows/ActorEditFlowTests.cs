using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActorEditFlowTests
{
    private readonly E2EFixture fixture;

    public ActorEditFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task EditActor_ModifiesMetadataAndReturnsToProfile()
    {
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Ichika",
                LastName = "Matsumoto"
            });
            await db.SaveChangesAsync();
        }

        const string initialDisplayName = "Matsumoto Ichika";
        var page = await fixture.NewPageAsync();

        // Navigate to profile
        await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(initialDisplayName)}");
        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(initialDisplayName);

        // Click Edit in toolbar
        await page.Locator(".actor-toolbar").GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"/actors/{Regex.Escape(Uri.EscapeDataString(initialDisplayName))}/edit$"));

        // Update fields
        await page.Locator("#actor-japanese-kanji").FillAsync("松本いちか");
        await page.Locator("#actor-r18dev-id").FillAsync("8888");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

        // Redirected back to profile
        await Expect(page).ToHaveURLAsync(new Regex($"/actors/{Regex.Escape(Uri.EscapeDataString(initialDisplayName))}$"));
        await Expect(page.Locator(".actor-hero-japanese")).ToContainTextAsync("松本いちか");
    }
}
