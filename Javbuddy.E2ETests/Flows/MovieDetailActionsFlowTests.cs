using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Covers Movie Detail's interactive badge actions — genre and studio filter navigation.
/// NFO editing has its own coverage in <see cref="NfoEditorFlowTests"/>.</summary>
[Collection(E2ECollection.Name)]
public class MovieDetailActionsFlowTests
{
    private readonly E2EFixture fixture;

    public MovieDetailActionsFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task ClickingGenreBadge_NavigatesToMoviesPageWithActiveGenreFilter()
    {
        var movie1 = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-GENRE-1", MovieStatus.Got);
        var movie2 = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-GENRE-2", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m1 = await db.Movies.FindAsync(movie1.Id);
            var m2 = await db.Movies.FindAsync(movie2.Id);
            m1!.MetaGenres = "Sci-Fi, Action";
            m2!.MetaGenres = "Drama";
            await TagNormalization.ApplyToMovieAsync(db, m1);
            await TagNormalization.ApplyToMovieAsync(db, m2);
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie1.Code}");
            var genreBtn = page.GetByRole(AriaRole.Button, new() { Name = "Sci-Fi", Exact = true });
            await Expect(genreBtn).ToBeVisibleAsync();
            await genreBtn.ClickAsync();

            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();
            Assert.Equal("/", new Uri(page.Url).AbsolutePath);
            Assert.DoesNotContain("genre=", page.Url);

            var posterCard1 = page.Locator($".poster-card:has-text('{movie1.Code}')");
            var posterCard2 = page.Locator($".poster-card:has-text('{movie2.Code}')");
            await Expect(posterCard1).ToBeVisibleAsync();
            await Expect(posterCard2).ToHaveCountAsync(0);

            var filterBtn = page.GetByRole(AriaRole.Button, new() { Name = "Filter (1)", Exact = true });
            await filterBtn.ClickAsync();
            var genreFilterDropdownBtn = page.GetByRole(AriaRole.Button, new() { Name = "Genre (1)" });
            await Expect(genreFilterDropdownBtn).ToBeVisibleAsync();
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ClickingStudioBadge_NavigatesToMoviesPageWithActiveStudioFilter()
    {
        var movie1 = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STUDIO-1", MovieStatus.Got);
        var movie2 = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-STUDIO-2", MovieStatus.Got);
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var m1 = await db.Movies.FindAsync(movie1.Id);
            var m2 = await db.Movies.FindAsync(movie2.Id);
            m1!.MetaStudio = "Studio Alpha";
            m2!.MetaStudio = "Studio Beta";
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie1.Code}");
            var studioBtn = page.GetByRole(AriaRole.Button, new() { Name = "Studio Alpha", Exact = true });
            await Expect(studioBtn).ToBeVisibleAsync();
            await studioBtn.ClickAsync();

            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Movies", Exact = true })).ToBeVisibleAsync();
            Assert.Equal("/", new Uri(page.Url).AbsolutePath);
            Assert.DoesNotContain("studio=", page.Url);

            var posterCard1 = page.Locator($".poster-card:has-text('{movie1.Code}')");
            var posterCard2 = page.Locator($".poster-card:has-text('{movie2.Code}')");
            await Expect(posterCard1).ToBeVisibleAsync();
            await Expect(posterCard2).ToHaveCountAsync(0);

            var filterBtn = page.GetByRole(AriaRole.Button, new() { Name = "Filter (1)", Exact = true });
            await filterBtn.ClickAsync();
            var studioFilterDropdownBtn = page.GetByRole(AriaRole.Button, new() { Name = "Studio (1)" });
            await Expect(studioFilterDropdownBtn).ToBeVisibleAsync();
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>Covers the field-picker end to end through a real browser/SignalR circuit:
    /// fetching a live scrape, the diff-based default selection (only a changed field is
    /// preselected), and applying only that selection back onto the movie.</summary>
    [Fact]
    public async Task ClickingRefreshWithJavinizer_AppliesOnlyTheSelectedFieldFromTheRealScrape()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-REFRESH-1", MovieStatus.Got, metaTitle: "Old Title");

        fixture.FakeJavinizer.ScrapeResponses["E2E-REFRESH-1"] = new MovieViewDto
        {
            Id = "E2E-REFRESH-1",
            Title = "Refreshed Title",
            Director = "New Director",
            Maker = "New Studio",
            ReleaseDate = new DateTime(2024, 1, 1),
            Runtime = 120,
        };

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");

            await page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
            var refreshItem = page.GetByRole(AriaRole.Menuitem, new() { Name = "Refresh with Javinizer" });
            await Expect(refreshItem).ToBeVisibleAsync();
            await refreshItem.ClickAsync();

            var modalTitle = page.Locator(".metadata-refresh-title");
            await Expect(modalTitle).ToBeVisibleAsync();
            await Expect(modalTitle).ToContainTextAsync(movie.Code!);

            var titleField = page.Locator(".import-field-item:has(#field-title)");
            await Expect(titleField).ToContainTextAsync("Refreshed Title");
            await Expect(titleField).ToContainTextAsync("Old Title");

            // Title differs from the seeded value, Runtime doesn't (both 120) — only Title should
            // come preselected by the "Select All Changes" default.
            await Expect(page.Locator("#field-title")).ToBeCheckedAsync();
            await Expect(page.Locator("#field-runtime")).Not.ToBeCheckedAsync();
            await Expect(page.Locator("#field-director")).ToBeCheckedAsync();

            // Deselect Director — only Title should actually be applied.
            await page.Locator("#field-director").UncheckAsync();

            var applyBtn = page.GetByRole(AriaRole.Button, new() { Name = "Apply Selected" });
            await applyBtn.ClickAsync();

            await Expect(page.Locator(".metadata-refresh-backdrop")).ToHaveCountAsync(0);
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

            await using var db = await fixture.DbFactory.CreateDbContextAsync();
            var reloaded = await db.Movies.SingleAsync(m => m.Id == movie.Id);
            Assert.Equal("E2E-REFRESH-1 Refreshed Title", reloaded.MetaTitle);
            Assert.Null(reloaded.MetaDirector);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task PlayMovie_WhenMatchedToJellyfin_OpensAndClosesVideoPlayerModal()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-PLAY-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = await db.Movies.FindAsync(movie.Id);
            m!.MetaSourceName = "javinizer";
            m.JellyfinItemId = "fake-jf-item-play-1";
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");

            var playBadgeBtn = page.Locator("button.movie-detail-badge-play");
            var jellyfinBadge = page.Locator("a.movie-detail-badge-jellyfin");
            await Expect(playBadgeBtn).ToBeVisibleAsync();
            await Expect(jellyfinBadge).ToBeVisibleAsync();
            await Expect(page.Locator(".movie-toolbar-btn-play")).ToHaveCountAsync(0);
            await Expect(page.Locator(".movie-detail-poster-play-btn")).ToHaveCountAsync(0);

            // Click Play badge button -> opens player modal
            await playBadgeBtn.ClickAsync();

            var modalBackdrop = page.Locator(".video-player-modal-backdrop");
            await Expect(modalBackdrop).ToBeVisibleAsync();
            await Expect(page.Locator(".video-player-modal-code")).ToContainTextAsync(movie.Code!);
            await Expect(page.Locator("video.video-player-video")).ToBeVisibleAsync();

            // Press Escape -> closes modal
            await page.Keyboard.PressAsync("Escape");
            await Expect(modalBackdrop).ToHaveCountAsync(0);

            // Click Play badge button again -> modal opens
            await playBadgeBtn.ClickAsync();
            await Expect(modalBackdrop).ToBeVisibleAsync();

            // Click modal close button -> closes modal
            var closeBtn = page.Locator(".video-player-modal-close-btn");
            await closeBtn.ClickAsync();
            await Expect(modalBackdrop).ToHaveCountAsync(0);

            // Click cover poster -> opens cover lightbox (old function preserved)
            var cover = page.Locator(".movie-detail-poster-clickable");
            await cover.ClickAsync();
            await Expect(page.Locator(".image-lightbox-backdrop")).ToBeVisibleAsync();

            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task PlayMovie_AfterLeavingFullscreen_EscapeStillClosesThePlayer()
    {
        // Entering fullscreen moved focus off the backdrop that handles Esc.
        // Fullscreen is bound by the ScrubBar, which the item's trickplay makes sure is drawn.
        const string itemId = "fake-jf-item-fs-esc-1";
        fixture.FakeJellyfin.Trickplay = new JellyfinTrickplayItemDto
        {
            Id = itemId,
            RunTimeTicks = TimeSpan.FromSeconds(3).Ticks,
            Trickplay = new()
            {
                [itemId] = new()
                {
                    ["320"] = new JellyfinTrickplayInfoDto { Width = 320, Height = 180, TileWidth = 10, TileHeight = 10, ThumbnailCount = 3, Interval = 1000 },
                },
            },
        };
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-PLAY-FS-ESC-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = await db.Movies.FindAsync(movie.Id);
            m!.MetaSourceName = "javinizer";
            m.JellyfinItemId = itemId;
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-detail-badge-play").ClickAsync();

            var modalBackdrop = page.Locator(".video-player-modal-backdrop");
            await Expect(modalBackdrop).ToBeVisibleAsync();
            // The ScrubBar's JS module sizes the trickplay thumb once it has bound its keys.
            await page.WaitForFunctionAsync("() => document.querySelector('.scrub-bar-thumb')?.style.width !== ''");

            await page.Keyboard.PressAsync("f");
            await page.WaitForFunctionAsync("() => document.fullscreenElement?.classList.contains('video-player-column')");
            await page.Keyboard.PressAsync("f");
            await page.WaitForFunctionAsync("() => document.fullscreenElement === null");

            // fullscreenElement clears as soon as fullscreen exits, but the fullscreenchange handler that
            // hands focus back to the backdrop runs at the next rendering step: pressing Esc in between
            // would reach <body> instead.
            await page.WaitForFunctionAsync("() => document.activeElement?.classList.contains('video-player-modal-backdrop')");
            await page.Keyboard.PressAsync("Escape");
            await Expect(modalBackdrop).ToHaveCountAsync(0);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task PlayMovie_WhenInFullscreen_PreservesTimelineSeekbarRule()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-PLAY-FS-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = await db.Movies.FindAsync(movie.Id);
            m!.MetaSourceName = "javinizer";
            m.JellyfinItemId = "fake-jf-item-fs-1";
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            await db.SaveChangesAsync();
        }
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");

            var playBadgeBtn = page.Locator("button.movie-detail-badge-play");
            await Expect(playBadgeBtn).ToBeVisibleAsync();
            await playBadgeBtn.ClickAsync();

            var modalBackdrop = page.Locator(".video-player-modal-backdrop");
            await Expect(modalBackdrop).ToBeVisibleAsync();
            await Expect(page.Locator("video.video-player-video")).ToBeVisibleAsync();

            var result = await page.EvaluateAsync<bool[]>(@"async () => {
                const video = document.querySelector('video.video-player-video');
                video.classList.add('video-player-scrub');

                let hidesTimeline = false;
                let hidesFsBtn = false;
                for (const sheet of document.styleSheets) {
                    try {
                        for (const rule of sheet.cssRules) {
                            if (rule.selectorText && rule.selectorText.includes('.video-player-scrub') && rule.selectorText.includes('webkit-media-controls-timeline')) {
                                if (rule.style.display.includes('none')) hidesTimeline = true;
                            }
                            if (rule.selectorText && rule.selectorText.includes('.video-player-scrub') && rule.selectorText.includes('webkit-media-controls-fullscreen-button')) {
                                if (rule.style.display.includes('none')) hidesFsBtn = true;
                            }
                        }
                    } catch (e) {}
                }

                return [hidesTimeline, hidesFsBtn];
            }");

            Assert.True(result[0], "Timeline should be hidden on video-player-scrub");
            Assert.True(result[1], "Native fullscreen button should be hidden on video-player-scrub");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}

