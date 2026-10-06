using System.Text.RegularExpressions;
using Javbuddy.Data;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class MovieCleanupFlowTests
{
    private readonly E2EFixture fixture;

    public MovieCleanupFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    /// <summary>The fixture's app+DB is shared across every E2E test in the run, so the unscoped
    /// queue holds hundreds of other tests' Got movies in random order. Scoping the queue to an
    /// actor linked only to this test's movie makes it the one card, instead of Skipping through
    /// the whole queue one SignalR round trip at a time (up to a minute).</summary>
    [Fact]
    public async Task DeleteConfirm_RemovesTheMovieAndItsFolderFromDisk()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "javbuddy-e2e-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var movieFolder = Path.Combine(rootPath, "E2E-CLEAN-1");
        Directory.CreateDirectory(movieFolder);
        await File.WriteAllTextAsync(Path.Combine(movieFolder, "E2E-CLEAN-1.mp4"), "fake video bytes");

        var sessionTracker = fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>();
        sessionTracker.ClearSession();

        try
        {
            await DbSeeding.SeedLocalLibraryRootPathAsync(fixture.App.Services, rootPath);
            var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var actor = await DbSeeding.SeedActorAsync(dbFactory, "Deletetest Cleanup");
            var movie = await DbSeeding.SeedMovieAsync(dbFactory, "E2E-CLEAN-1", MovieStatus.Got);
            await using (var seedDb = await dbFactory.CreateDbContextAsync())
            {
                seedDb.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
                await seedDb.SaveChangesAsync();
            }

            var page = await fixture.NewPageAsync();
            await page.GotoInteractiveAsync($"/movies/cleanup?actorId={actor.Id}");

            await Expect(page.Locator(".cleanup-eligible-pill")).ToHaveTextAsync("1 eligible");
            await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync("E2E-CLEAN-1");

            await page.Locator(".cleanup-delete-btn").ClickAsync();
            await Expect(page.Locator(".cleanup-modal-confirm-btn")).ToBeVisibleAsync();
            await page.Locator(".cleanup-modal-confirm-btn").ClickAsync();

            await Expect(page.Locator(".cleanup-code", new() { HasText = "E2E-CLEAN-1" })).Not.ToBeVisibleAsync();
            Assert.False(Directory.Exists(movieFolder));

            await using var db = await dbFactory.CreateDbContextAsync();
            Assert.False(await db.Movies.AnyAsync(m => m.Code == "E2E-CLEAN-1"));
        }
        finally
        {
            sessionTracker.ClearSession();
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    /// <summary>Stop no longer leaves Cleanup: it ends the session and shows the summary, and the
    /// summary's Finish button is what returns to Movies. The app's session tracker
    /// is a shared singleton, so it is cleared up front to start from a fresh queue rather than an
    /// earlier test's leftover session (clicking Restart instead would race its reload against
    /// reading the current code) — and two movies are seeded so the Skip doesn't exhaust the
    /// queue, which would end the session on its own before Stop is clicked.</summary>
    [Fact]
    public async Task Stop_ShowsSessionSummary_AndFinishReturnsToMovies()
    {
        var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await DbSeeding.SeedMovieAsync(dbFactory, "E2E-SUMMARY-1", MovieStatus.Got);
        await DbSeeding.SeedMovieAsync(dbFactory, "E2E-SUMMARY-2", MovieStatus.Got);

        var sessionTracker = fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>();
        sessionTracker.ClearSession();

        try
        {
            var page = await fixture.NewPageAsync();
            await page.GotoInteractiveAsync("/movies/cleanup");
            await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 0 reviewed");
            var skippedCode = await page.Locator(".cleanup-code").TextContentAsync();
            await page.Locator(".cleanup-skip-btn").ClickAsync();
            await Expect(page.Locator(".cleanup-session-count")).ToHaveTextAsync("Session: 1 reviewed");
            await page.Locator(".cleanup-stop-btn").ClickAsync();

            await Expect(page.Locator(".cleanup-summary")).ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-card")).Not.ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-stat-reviewed .cleanup-stat-value")).ToHaveTextAsync("1");
            await Expect(page.Locator(".cleanup-summary-kept .cleanup-summary-item")).ToHaveCountAsync(1);
            await Expect(page.Locator(".cleanup-summary-kept")).ToContainTextAsync(skippedCode!.Trim());
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

            await page.Locator(".cleanup-finish-btn").ClickAsync();

            await Expect(page).ToHaveURLAsync(new Regex(@"^[^/]+//[^/]+/$"));
        }
        finally
        {
            // A failure mid-test would otherwise leave an ended session that the next Cleanup
            // test resumes as a summary instead of a review card.
            sessionTracker.ClearSession();
        }
    }

    /// <summary>The Review button on Actor Detail launches a Review-mode queue scoped to that actor's Got movies
    /// through the real circuit — the ?actorId= query parameter, the favorites rule
    /// being skipped (the actor is a favorite here), and the "All movies" escape hatch all need the
    /// real router rather than bUnit's fake navigation. The shared fixture DB holds many unrelated
    /// Got movies, so the scoped queue size (exactly the two seeded links) is what proves the scope.</summary>
    [Fact]
    public async Task ActorDetailReviewButton_ScopesQueueToThatActor_AndAllMoviesClearsTheScope()
    {
        var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var actor = await DbSeeding.SeedActorAsync(dbFactory, "Scopedfav Cleanuptest", isFavorite: true);
        var first = await DbSeeding.SeedMovieAsync(dbFactory, "E2E-SCOPE-1", MovieStatus.Got);
        var second = await DbSeeding.SeedMovieAsync(dbFactory, "E2E-SCOPE-2", MovieStatus.Got);
        await DbSeeding.SeedMovieAsync(dbFactory, "E2E-SCOPE-OTHER", MovieStatus.Got);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MovieActors.AddRange(
                new MovieActor { MovieId = first.Id, ActorId = actor.Id },
                new MovieActor { MovieId = second.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        var sessionTracker = fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>();
        sessionTracker.ClearSession();

        try
        {
            var page = await fixture.NewPageAsync();
            await page.GotoInteractiveAsync($"/actors/{Uri.EscapeDataString(actor.DisplayName)}");
            await page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Review", Exact = true }).ClickAsync();

            await Expect(page).ToHaveURLAsync(new Regex($@"/movies/review\?actorId={actor.Id}$"));
            await Expect(page.Locator(".cleanup-scope")).ToContainTextAsync(actor.DisplayName);
            await Expect(page.Locator(".cleanup-eligible-pill")).ToHaveTextAsync("2 eligible");
            await Expect(page.Locator(".cleanup-code")).ToHaveTextAsync(new Regex("^E2E-SCOPE-[12]$"));
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

            await page.Locator(".cleanup-scope-clear").WaitForInteractiveAsync();
            await page.Locator(".cleanup-scope-clear").ClickAsync();

            await Expect(page.Locator(".cleanup-scope")).Not.ToBeVisibleAsync();
            await Expect(page).ToHaveURLAsync(new Regex(@"/movies/review$"));
            await Expect(page.Locator(".cleanup-eligible-pill")).Not.ToHaveTextAsync("2 eligible");
        }
        finally
        {
            sessionTracker.ClearSession();
        }
    }

    /// <summary>Review mode shares the Cleanup page component across two routes, so switching
    /// via the header toggle only works if the real router/circuit reuses the instance and the page
    /// picks the new mode up from the URL — the part bUnit's fake NavigationManager can't prove.</summary>
    [Fact]
    public async Task ReviewMode_HasNextInsteadOfDelete_AndTheToggleSwitchesModes()
    {
        var dbFactory = fixture.App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await DbSeeding.SeedMovieAsync(dbFactory, "E2E-REVIEW-1", MovieStatus.Got);
        var sessionTracker = fixture.App.Services.GetRequiredService<IMovieCleanupSessionTracker>();
        sessionTracker.ClearSession();

        try
        {
            var page = await fixture.NewPageAsync();
            await page.GotoInteractiveAsync("/movies/review");

            await Expect(page.Locator("h1.cleanup-page-title")).ToHaveTextAsync("Review");
            await Expect(page.Locator(".cleanup-next-btn")).ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-delete-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator(".cleanup-skip-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator(".cleanup-snooze-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator(".cleanup-blacklist-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator(".cleanup-unreviewed-label")).ToBeVisibleAsync();

            await page.Locator("a.cleanup-mode-link", new() { HasText = "Cleanup" }).ClickAsync();

            await Expect(page).ToHaveURLAsync(new Regex(@"/movies/cleanup$"));
            await Expect(page.Locator("h1.cleanup-page-title")).ToHaveTextAsync("Cleanup");
            await Expect(page.Locator(".cleanup-delete-btn")).ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-snooze-btn")).ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-blacklist-btn")).ToBeVisibleAsync();
            await Expect(page.Locator(".cleanup-next-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator(".cleanup-unreviewed-label")).ToHaveCountAsync(0);

            await page.Locator("a.cleanup-mode-link", new() { HasText = "Review" }).ClickAsync();

            await Expect(page.Locator("h1.cleanup-page-title")).ToHaveTextAsync("Review");
            await Expect(page.Locator(".cleanup-delete-btn")).ToHaveCountAsync(0);
            await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        }
        finally
        {
            sessionTracker.ClearSession();
        }
    }
}
