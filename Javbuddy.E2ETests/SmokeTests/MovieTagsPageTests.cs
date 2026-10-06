using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class MovieTagsPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task TagsPage_RendersAllTagsPanel_WithSeededTag()
    {
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "E2E Seeded Tag" });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");

        await Expect(page).ToHaveTitleAsync("Tags · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tags", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("E2E Seeded Tag")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TagsPage_AddIgnoredTag_AppearsInList()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ignored Tags" }).ClickAsync();
        await page.GetByPlaceholder("e.g. Sample").FillAsync("E2E Ignored Value");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await Expect(page.GetByText("E2E Ignored Value")).ToBeVisibleAsync();

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.True(await db.IgnoredTags.AnyAsync(i => i.Value == "E2E Ignored Value"));
    }

    [Fact]
    public async Task TagsPage_BulkApprove_ClearsNeedsReviewForSelectedTags()
    {
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Tags.AddRange(
                new Tag { Name = "E2E Bulk Tag One", NeedsReview = true },
                new Tag { Name = "E2E Bulk Tag Two", NeedsReview = true });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");

        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Select E2E Bulk Tag One" }).CheckAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Select E2E Bulk Tag Two" }).CheckAsync();

        await Expect(page.GetByText("2 selected")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve all" }).ClickAsync();

        await Expect(page.GetByText("Approved 2 tags.")).ToBeVisibleAsync();

        await using var db2 = await fixture.DbFactory.CreateDbContextAsync();
        Assert.False(await db2.Tags.AnyAsync(t => (t.Name == "E2E Bulk Tag One" || t.Name == "E2E Bulk Tag Two") && t.NeedsReview));
    }

    [Fact]
    public async Task NavMenu_TagsLink_NavigatesToTagsPage()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");

        await page.GetByRole(AriaRole.Link, new() { Name = "Tags" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/movies/tags$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tags", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TagsPage_CreateNewTag_AppearsInListAndDatabase()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create Tag" }).ClickAsync();
        await page.GetByPlaceholder("e.g. Cosplay").FillAsync("E2E Custom Tag");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();

        await Expect(page.GetByText("Tag \"E2E Custom Tag\" created.")).ToBeVisibleAsync();
        await Expect(page.GetByText("E2E Custom Tag", new() { Exact = true })).ToBeVisibleAsync();

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == "E2E Custom Tag");
        Assert.NotNull(tag);
        Assert.False(tag.NeedsReview);
    }
}
