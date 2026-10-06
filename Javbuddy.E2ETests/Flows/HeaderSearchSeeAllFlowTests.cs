using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Regression: the header search dropdown's "See all matches" link
/// didn't take you to the filtered Movies grid; and the query lingering in the
/// header after a result was picked. It depends on real focus/mousedown/click ordering
/// in a browser and on Blazor's enhanced navigation to `/?q=…`, neither of which bUnit
/// exercises.</summary>
[Collection(E2ECollection.Name)]
public class HeaderSearchSeeAllFlowTests(E2EFixture fixture)
{
    [Theory]
    [InlineData("/actors", "SEEALLA")]
    [InlineData("/", "SEEALLM")]
    public async Task ClickingSeeAllMatches_NavigatesToTheFilteredMoviesGrid(string startPath, string prefix)
    {
        var (page, seeAll) = await OpenDropdownAsync(startPath, prefix);

        // A real click holds the button down for a moment; the input's focusout fires on mousedown,
        // so the dropdown must still be there when the click lands on mouseup.
        var box = (await seeAll.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.UpAsync();

        await Expect(page).ToHaveURLAsync(new Regex($@"/\?q={prefix}$"));
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(2);
        await Expect(page.Locator(".search-dropdown")).ToHaveCountAsync(0);
        await Expect(page.Locator("input.search-input")).ToHaveValueAsync("");
    }

    [Theory]
    [InlineData("/actors", "SEEALLB")]
    [InlineData("/", "SEEALLN")]
    public async Task PressingEnter_NavigatesToTheFilteredMoviesGrid(string startPath, string prefix)
    {
        var (page, _) = await OpenDropdownAsync(startPath, prefix);

        await page.Locator("input.search-input").PressAsync("Enter");

        await Expect(page).ToHaveURLAsync(new Regex($@"/\?q={prefix}$"));
        await Expect(page.Locator(".poster-card")).ToHaveCountAsync(2);
        await Expect(page.Locator(".search-dropdown")).ToHaveCountAsync(0);
        await Expect(page.Locator("input.search-input")).ToHaveValueAsync("");
    }

    // The query is cleared once a result is picked, without the clear on focusout
    // swallowing the click that picks it.
    [Fact]
    public async Task ClickingAMovieResult_OpensItAndClearsTheQuery()
    {
        const string prefix = "SEEALLR";
        var (page, _) = await OpenDropdownAsync("/actors", prefix);

        var result = page.Locator(".search-dropdown a.search-result", new() { HasText = $"{prefix}-001" });
        var box = (await result.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.UpAsync();

        await Expect(page).ToHaveURLAsync(new Regex($@"/movies/{prefix}-001$"));
        await Expect(page.Locator(".search-dropdown")).ToHaveCountAsync(0);
        await Expect(page.Locator("input.search-input")).ToHaveValueAsync("");
    }

    private async Task<(IPage Page, ILocator SeeAll)> OpenDropdownAsync(string startPath, string prefix)
    {
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, $"{prefix}-001");
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, $"{prefix}-002");

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync(startPath);

        var input = page.Locator("input.search-input");
        var seeAll = page.Locator(".search-dropdown a", new() { HasText = "See all matches" });
        // Typing before the circuit is interactive is silently dropped — retype until the dropdown shows.
        for (var attempt = 0; attempt < 5 && !await seeAll.IsVisibleAsync(); attempt++)
        {
            await input.FillAsync(prefix);
            try { await seeAll.WaitForAsync(new() { Timeout = 2000 }); } catch (TimeoutException) { }
        }
        return (page, seeAll);
    }
}
