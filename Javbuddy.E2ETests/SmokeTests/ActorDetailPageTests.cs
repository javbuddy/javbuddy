using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class ActorDetailPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task ActorDetail_RendersHeroWithActiveNavLink()
    {
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "E2E Detail Actress");
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(actor.DisplayName)}");

        await Expect(page).ToHaveTitleAsync($"{actor.DisplayName} · Javbuddy");
        await Expect(page.Locator("h1.actor-hero-title")).ToHaveTextAsync(actor.DisplayName);
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Actors" })).ToHaveClassAsync(new Regex("active"));
    }
}
