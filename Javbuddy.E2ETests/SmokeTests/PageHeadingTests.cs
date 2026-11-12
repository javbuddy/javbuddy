using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>Every routable page has exactly one h1: it is what Routes.razor's FocusOnNavigate focuses after an
/// in-app navigation (no h1 means focus is left behind on the link that was clicked) and the heading a screen
/// reader lands on. A page without a visible title carries a visually hidden one.</summary>
[Collection(E2ECollection.Name)]
public class PageHeadingTests(E2EFixture fixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/missing")]
    [InlineData("/add/new")]
    [InlineData("/add/import")]
    [InlineData("/movies/discover")]
    [InlineData("/movies/tags")]
    [InlineData("/movies/scenes")]
    [InlineData("/movies/cleanup")]
    [InlineData("/movies/review")]
    [InlineData("/actors")]
    [InlineData("/actors/photos")]
    [InlineData("/actors/add/new")]
    [InlineData("/actors/add/import")]
    [InlineData("/activity/queue")]
    [InlineData("/activity/history")]
    [InlineData("/settings")]
    [InlineData("/settings/connections")]
    [InlineData("/settings/metadata")]
    [InlineData("/settings/deovr")]
    [InlineData("/settings/ui")]
    [InlineData("/system/status")]
    [InlineData("/system/tasks")]
    [InlineData("/system/backups")]
    [InlineData("/system/statistics")]
    [InlineData("/not-found")]
    public async Task StaticPage_HasExactlyOneH1(string route) => await AssertOneH1Async(route);

    [Fact]
    public async Task SeededPages_HaveExactlyOneH1()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-H1-001");
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "E2E Heading Actress");
        var download = await DbSeeding.SeedTorrentDownloadAsync(fixture.DbFactory, movie);
        var actorRoute = $"/actors/{Uri.EscapeDataString(actor.DisplayName)}";

        foreach (var route in new[]
        {
            $"/movies/{Uri.EscapeDataString(movie.Code!)}",
            actorRoute,
            $"{actorRoute}/edit",
            $"{actorRoute}/missing",
            $"/activity/sort/{download.Id}",
        })
        {
            await AssertOneH1Async(route);
        }
    }

    private async Task AssertOneH1Async(string route)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(route);
        var h1 = page.Locator("h1");

        // Auto-retries while the page loads its data; the pause and second check catch a second h1 that renders late.
        await Expect(h1).ToHaveCountAsync(1);
        await page.WaitForTimeoutAsync(500);
        await Expect(h1).ToHaveCountAsync(1);
        await Expect(h1).Not.ToHaveTextAsync("");
    }
}
