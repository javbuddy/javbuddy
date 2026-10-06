using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class ActorAddFlowTests
{
    private readonly E2EFixture fixture;

    public ActorAddFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task AddActor_LandsOnDetailPageAndFollowsToMissing()
    {
        const string firstName = "Flow Actress";
        const string lastName = "E2E";
        const string displayName = "E2E Flow Actress";
        await DbSeeding.SeedR18DevEnabledAsync(fixture.DbFactory);
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/actors/add/new");
        await page.GetByPlaceholder("First name, e.g. Yua").FillAsync(firstName);
        await page.GetByPlaceholder("Last name (optional), e.g. Mikami").FillAsync(lastName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

        var encodedName = Uri.EscapeDataString(displayName);
        await Expect(page).ToHaveURLAsync(new Regex($"/actors/{Regex.Escape(encodedName)}$"));
        await Expect(page.Locator("h1.actor-hero-title")).ToHaveTextAsync(displayName);

        // Scoped to the actor's own toolbar — the sidebar nav also has a "Missing" link (its
        // top-level section, renamed to match the /missing route) that would otherwise ambiguously
        // match the same accessible name.
        await page.Locator(".actor-toolbar").GetByRole(AriaRole.Link, new() { Name = "Missing" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/actors/{Regex.Escape(encodedName)}/missing$"));
        await Expect(page).ToHaveTitleAsync($"{displayName} · Missing · Javbuddy");
    }
}
