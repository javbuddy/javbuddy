using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The "All releases (r18.dev)" toggle swaps the local grid for the r18.dev
/// catalog — all of it, or what matches the page's query: a code prefix (clicking it on a movie's detail
/// page), or whatever is typed in the search box. Needs a real browser because the local grid is hidden, not removed —
/// Movies.razor.js keeps references to it and must not misread the hidden grid — and the card
/// quick actions only appear on hover through the component's own scoped CSS.</summary>
[Collection(E2ECollection.Name)]
public class MoviesBrowseAllReleasesFlowTests
{
    private readonly E2EFixture fixture;

    public MoviesBrowseAllReleasesFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task DeletedMovie_IsRememberedInCatalog_CanBeHiddenReAddedAndPurged()
    {
        await using var catalog = await CatalogScope.CreateAsync(fixture, "HIST");
        var page = await fixture.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);

        // "Hide previously deleted" is a toggle in the Movies toolbar's Filter dropdown; the
        // dropdown closes via its full-page backdrop, which would otherwise cover the cards.
        async Task ToggleHideDeletedAsync()
        {
            var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Hide previously deleted" });
            await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Filter") }).ClickUntilVisibleAsync(toggle);
            await toggle.ClickAsync();
            // Off to the right: the catalog's Filter menu is tall enough to cover the backdrop's middle.
            await page.Locator(".dropdown-backdrop").ClickAsync(new() { Position = new() { X = 1000, Y = 300 } });
        }

        await page.GotoInteractiveAsync("/movies/HIST-901");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickUntilVisibleAsync(page.GetByRole(AriaRole.Dialog));
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "All releases (r18.dev)" }).ClickUntilVisibleAsync(page.Locator(".missing-poster-card").First);
        await page.GetByPlaceholder("Filter by code or title…").FillAsync("HIST");
        var deletedCard = page.Locator(".missing-poster-card", new() { HasText = "HIST-901" });
        await Expect(page.Locator(".missing-poster-card", new() { HasText = "HIST-902" })).ToBeVisibleAsync();
        await Expect(deletedCard).ToHaveCountAsync(0);
        await Expect(page.Locator(".releases-summary")).ToContainTextAsync("1 previously deleted, hidden");
        await ToggleHideDeletedAsync();
        await Expect(deletedCard.Locator(".status-deleted")).ToBeVisibleAsync();
        await Expect(deletedCard).ToContainTextAsync("Previously Deleted");
        await ToggleHideDeletedAsync();
        await Expect(deletedCard).ToHaveCountAsync(0);
        await ToggleHideDeletedAsync();
        await Expect(deletedCard).ToBeVisibleAsync();

        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "History589" });
            await db.SaveChangesAsync();
        }
        await using (var connection = new SqliteConnection($"Data Source={R18DevDumpSeeding.PathFor(fixture.App.Services)}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO actresses (id, name_romaji) VALUES (589, 'History589');
                INSERT INTO video_actresses (content_id, actress_id, ordinality) VALUES ('hist-901', 589, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await page.GotoInteractiveAsync("/actors/History589/missing");
        await Expect(page.Locator(".missing-summary")).ToContainTextAsync("0 movies");
        var hideDeleted = page.GetByRole(AriaRole.Button, new() { Name = "Hide previously deleted" });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Filter") }).ClickUntilVisibleAsync(hideDeleted);
        await hideDeleted.ClickAsync();
        await Expect(page.Locator(".status-deleted")).ToBeVisibleAsync();
        await Expect(page.Locator(".missing-summary")).ToContainTextAsync("0 missing · 0 already in your library · 1 previously deleted");
        // Back on Movies, the catalog and its "Hide previously deleted" being off are remembered.
        await page.GotoInteractiveAsync("/");
        await page.GetByPlaceholder("Filter by code or title…").FillAsync("HIST");
        await Expect(deletedCard).ToBeVisibleAsync();

        await deletedCard.HoverAsync();
        await deletedCard.GetByRole(AriaRole.Button, new() { Name = "Add HIST-901 to library" }).ClickAsync();
        await Expect(deletedCard.Locator(".status-wanted")).ToBeVisibleAsync();
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            Assert.False(await db.DeletedMovies.AnyAsync(m => m.Code == "HIST-901"));
        }

        await page.GotoInteractiveAsync("/movies/HIST-901");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickUntilVisibleAsync(page.GetByRole(AriaRole.Dialog));
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();
        await page.GotoInteractiveAsync("/activity/history");
        await page.GetByRole(AriaRole.Button, new() { Name = "Deleted movies" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/activity/history\\?view=deleted$"));
        var historyRow = page.Locator(".activity-row-deleted").Filter(new() { HasText = "HIST-901" });
        await Expect(historyRow).ToBeVisibleAsync();
        await historyRow.GetByRole(AriaRole.Button, new() { Name = "Purge history for HIST-901" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(historyRow).ToBeVisibleAsync();
        await historyRow.GetByRole(AriaRole.Button, new() { Name = "Purge history for HIST-901" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.GetByText("No deleted movies recorded.")).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PrefixFilter_AllReleasesToggle_ShowsCatalogWithLibraryStatusAndAddsAMissingRelease()
    {
        await using var catalog = await CatalogScope.CreateAsync(fixture, "MIDE");

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/MIDE-901");
        await page.Locator(".movie-detail-code-prefix").ClickAsync();
        await Expect(page.GetByPlaceholder("Filter by code or title…")).ToHaveValueAsync("MIDE");
        await Expect(page.Locator(".movies-grid .poster-card")).ToHaveCountAsync(1);

        await page.GetByRole(AriaRole.Button, new() { Name = "All releases (r18.dev)" }).ClickUntilVisibleAsync(page.Locator(".missing-poster-card").First);

        // The local grid is hidden (not torn out of the DOM) and the catalog takes its place.
        await Expect(page.Locator(".movies-grid")).ToBeHiddenAsync();
        var cards = page.Locator(".missing-poster-card");
        await Expect(cards).ToHaveCountAsync(3);
        await Expect(page.Locator(".releases-summary")).ToContainTextAsync("Showing 3 of 3 releases");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Untracked (2)" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Got (1)" })).ToBeVisibleAsync();
        await Expect(cards.Filter(new() { HasText = "MIDE-901" }).Locator(".poster-image")).ToHaveClassAsync(new Regex("status-got"));
        await Expect(cards.Filter(new() { HasText = "MIDE-902" }).Locator(".poster-image")).ToHaveClassAsync(new Regex("status-missing"));
        await Expect(page.Locator(".missing-poster-card", new() { HasText = "MIDEA-901" })).ToHaveCountAsync(0);

        // Quick actions are hover-only, so hover the card first — as a user would.
        var missingCard = cards.Filter(new() { HasText = "MIDE-902" });
        await missingCard.HoverAsync();
        await Expect(missingCard.GetByRole(AriaRole.Button, new() { Name = "Search Prowlarr for MIDE-902" })).ToBeVisibleAsync();
        await missingCard.GetByRole(AriaRole.Button, new() { Name = "Add MIDE-902 to library" }).ClickAsync();

        await Expect(missingCard.Locator(".poster-image")).ToHaveClassAsync(new Regex("status-wanted"));
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Untracked (1)" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Wanted (1)" })).ToBeVisibleAsync();

        // Library status narrows the whole catalog, not just the cards on screen.
        await page.GetByRole(AriaRole.Button, new() { Name = "Untracked (1)" }).ClickAsync();
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(cards.Filter(new() { HasText = "MIDE-903" })).ToHaveCountAsync(1);

        // Back to the library: the local grid is visible again and shows the newly added movie.
        await page.GetByRole(AriaRole.Button, new() { Name = "In library" }).ClickAsync();
        await Expect(page.Locator(".movies-grid")).ToBeVisibleAsync();
        await Expect(page.Locator(".movies-grid .poster-card")).ToHaveCountAsync(2);
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(0);
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task AllReleasesToggle_WithNoFilter_BrowsesTheWholeCatalog_ThenWhateverIsTyped()
    {
        await using var catalog = await CatalogScope.CreateAsync(fixture, "TXTQ");

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".movies-grid")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "All releases (r18.dev)" }).ClickUntilVisibleAsync(page.Locator(".missing-poster-card").First);
        await Expect(page.Locator(".movies-grid")).ToBeHiddenAsync();
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(4);

        // A title fragment, not a code or prefix: matches "Not Yet Owned" and "Also Not Owned".
        await page.GetByPlaceholder("Filter by code or title…").FillAsync("owned");
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(2);
        await Expect(page.Locator(".missing-poster-card", new() { HasText = "TXTQ-902" })).ToHaveCountAsync(1);

        // Clearing the box goes back to the whole catalog.
        await page.GetByPlaceholder("Filter by code or title…").FillAsync("");
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(4);

        // Leaving the page and coming back keeps the catalog open, with its filters.
        await page.Locator(".releases-controls").GetByRole(AriaRole.Button, new() { Name = "Untracked (3)" }).ClickAsync();
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(3);
        await page.GotoInteractiveAsync("/activity/history");
        await page.GoBackAsync();
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(3);
        await Expect(page.Locator(".movies-grid")).ToBeHiddenAsync();
        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".missing-poster-card")).ToHaveCountAsync(3);
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    /// <summary>Seeds one owned movie, the r18.dev enabled flag and a small dump under a code
    /// prefix unique to the test, and removes exactly those on dispose — the app's database is
    /// shared by the whole run, and a leftover Missing movie or a present dump file would skew
    /// other flows' counts and their "not imported yet" assertions.</summary>
    private sealed class CatalogScope : IAsyncDisposable
    {
        private readonly E2EFixture fixture;
        private readonly string prefix;
        private int settingsId;

        private CatalogScope(E2EFixture fixture, string prefix)
        {
            this.fixture = fixture;
            this.prefix = prefix;
        }

        public static async Task<CatalogScope> CreateAsync(E2EFixture fixture, string prefix)
        {
            var scope = new CatalogScope(fixture, prefix);
            await DbSeeding.SeedMovieAsync(fixture.DbFactory, $"{prefix}-901", MovieStatus.Got);
            scope.settingsId = await R18DevDumpSeeding.EnableSourceAsync(fixture.DbFactory);
            R18DevDumpSeeding.Write(fixture.App.Services,
            [
                new($"{prefix}-901", "Moodyz", "2020-01-01", "In My Library"),
                new($"{prefix}-902", "Moodyz", "2020-02-01", "Not Yet Owned"),
                new($"{prefix}-903", "Moodyz", "2020-03-01", "Also Not Owned"),
                new($"{prefix}A-901", "Moodyz", "2020-04-01", "Different Prefix"),
            ]);
            return scope;
        }

        public async ValueTask DisposeAsync()
        {
            R18DevDumpSeeding.Delete(fixture.App.Services);
            await R18DevDumpSeeding.RemoveSourceAsync(fixture.DbFactory, settingsId);
            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            await db.Movies.Where(m => m.Code!.StartsWith(prefix)).ExecuteDeleteAsync();
            await db.DeletedMovies.Where(m => m.Code.StartsWith(prefix)).ExecuteDeleteAsync();
            if (prefix == "HIST") await db.Actors.Where(a => a.FirstName == "History589").ExecuteDeleteAsync();
        }
    }
}
