using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.SmokeTests;

/// <summary>The Actors grid plus its two thin "add" sub-pages — grouped in one file the same way
/// SettingsPageTests groups the Settings sub-pages.</summary>
[Collection(E2ECollection.Name)]
public class ActorsPageTests(E2EFixture fixture)
{
    [Fact]
    public async Task ActorsGrid_RendersWithActiveNavLink()
    {
        await DbSeeding.SeedActorAsync(fixture.DbFactory, "E2E Test Actress");
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/actors");

        await Expect(page).ToHaveTitleAsync("Actors · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Actors", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.nav a.nav-link", new() { HasText = "Actors" })).ToHaveClassAsync(new Regex("active"));
    }

    /// <summary>Regression test: changing the sort, then navigating to an actor's
    /// detail page and back (a real browser/SignalR circuit needed to catch this — bUnit's
    /// in-process JSInterop mock resolves everything synchronously, so it can't reproduce it), used
    /// to silently revert to the previous sort. `Actors.razor`'s OnInitializedAsync runs twice per
    /// in-app navigation: once as the prerendered SSR pass the enhanced-nav fetch triggers (which
    /// correctly sees the just-written cookie) and once more when the already-connected circuit
    /// resumes the newly routed-to component using the HttpContext it captured when the circuit
    /// itself first connected — which never sees a cookie written since. The stale second read was
    /// clobbering the correct first one on every in-app navigation, not just a fast back-and-forth;
    /// a plain page refresh always worked because that's a single fresh request.</summary>
    [Fact]
    public async Task ActorsGrid_SortPersists_AfterNavigatingToActorAndBack()
    {
        // A unique text-filter prefix scopes every assertion to just these two, regardless of
        // whatever other actors other tests sharing this fixture's database have seeded — the grid
        // isn't reset between tests.
        var favorite = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Regr275 Zulu Favorite", isFavorite: true);
        await DbSeeding.SeedActorAsync(fixture.DbFactory, "Regr275 Alpha Actress");
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/actors");
        await page.Locator("input[placeholder='Filter by name…']").FillAsync("Regr275");
        await Expect(page.Locator(".actor-card")).ToHaveCountAsync(2);
        // Default sort (Name ascending) puts Alpha Actress first.
        await Expect(page.Locator(".actor-card .actor-name").First).ToHaveTextAsync("Regr275 Alpha Actress");

        await page.Locator(".sort-dropdown-wrapper button", new() { HasText = "Sort:" }).ClickAsync();
        await page.Locator(".sort-dropdown-item", new() { HasText = "Favorites" }).ClickAsync();
        // "Favorites" sort defaults to favorites-first — wait for the reorder to actually land
        // (matching a real user seeing the change before clicking further) rather than racing the
        // sort click's own round-trip, which is a separate, unrelated race from the one this test
        // targets.
        await Expect(page.Locator(".actor-card .actor-name").First).ToHaveTextAsync(favorite.DisplayName);
        await page.Locator(".actor-card-link", new() { HasText = favorite.DisplayName }).ClickAsync();

        await Expect(page.Locator("h1.actor-hero-title")).ToContainTextAsync(favorite.DisplayName);

        await page.GoBackAsync();

        await Expect(page).ToHaveTitleAsync("Actors · Javbuddy");
        await Expect(page.Locator(".actor-card")).ToHaveCountAsync(2);
        await Expect(page.Locator(".actor-card .actor-name").First).ToHaveTextAsync(favorite.DisplayName);
    }

    [Fact]
    public async Task ActorAdd_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/actors/add/new");

        await Expect(page).ToHaveTitleAsync("Add an actor · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add an actor" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ActorImport_Renders()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/actors/add/import");

        await Expect(page).ToHaveTitleAsync("Import actors · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Import actors from your movies" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ActorsGrid_RetiredBadgeRemainsVisible_WhenActorNameOverflows()
    {
        await DbSeeding.SeedActorAsync(
            fixture.DbFactory,
            "Regr352 Super Long Overflowing Actress Name Retired Star",
            isRetired: true);

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/actors");
        await page.Locator("input[placeholder='Filter by name…']").FillAsync("Regr352");

        var card = page.Locator(".actor-card", new() { HasText = "Regr352" });
        // The card is in the unfiltered grid too, so wait for the debounced filter to have narrowed
        // the whole grid down to it; measuring before that races the grid's reflow.
        await Expect(page.Locator(".actor-card")).ToHaveCountAsync(1);

        var pill = card.Locator(".actor-retired-pill");
        await Expect(pill).ToBeVisibleAsync();

        var cardBox = await card.BoundingBoxAsync();
        var pillBox = await pill.BoundingBoxAsync();

        Assert.NotNull(cardBox);
        Assert.NotNull(pillBox);

        Assert.True(pillBox.X >= cardBox.X, $"Pill X ({pillBox.X}) was left of Card X ({cardBox.X})");
        Assert.True(pillBox.X + pillBox.Width <= cardBox.X + cardBox.Width,
            $"Pill right ({pillBox.X + pillBox.Width}) was right of Card right ({cardBox.X + cardBox.Width})");
    }
}
