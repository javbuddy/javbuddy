using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

[Collection(E2ECollection.Name)]
public class ActorMissingPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task ActorMissing_RendersWithActiveNavLink()
    {
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "E2E Missing Actress");
        var page = await fixture.NewPageAsync();

        await page.GotoAsync($"/actors/{Uri.EscapeDataString(actor.DisplayName)}/missing");

        await Expect(page).ToHaveTitleAsync($"{actor.DisplayName} · Missing · Javbuddy");
        await Expect(page.Locator("h1.missing-title")).ToHaveTextAsync("Missing");
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Actors" })).ToHaveClassAsync(new Regex("active"));
    }
}
