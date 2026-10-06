using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class MovieDetailPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task OwnedMovie_RendersHeroWithActiveNavLink()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-DETAIL-1");
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/movies/{movie.Code}");

        await Expect(page).ToHaveTitleAsync($"{movie.MetaTitle} · Javbuddy");
        await Expect(page.Locator("h1.movie-detail-title")).ToHaveTextAsync(movie.MetaTitle!);
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Movies" })).ToHaveClassAsync(new Regex("active"));
    }
}
