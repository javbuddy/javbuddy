using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>Actor tags on Movie Detail over a real circuit: tagging an actor in the cast section saves at once, survives a
/// reload, and can be removed again.</summary>
[Collection(E2ECollection.Name)]
public class ActorTagsFlowTests
{
    private readonly E2EFixture fixture;

    public ActorTagsFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task TaggingAnActorOnMovieDetail_SavesAtOnce_AndCanBeRemoved()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-1", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Mei Tagged");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            // Movie Detail's cast comes from the cast text, matched to the linked actors.
            (await db.Movies.FindAsync(movie.Id))!.MetaActresses = "Mei Tagged";
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.Tags.Add(new Tag { Name = "E2E Blonde", IsActorTag = true });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");

        // Nothing to add until the cast is being edited.
        await Expect(page.Locator(".actor-pill-add")).ToHaveCountAsync(0);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Edit Cast" }).ClickAsync();

        var pills = page.Locator(".actor-pills", new() { Has = page.Locator("[aria-label='Add an actor tag to Mei Tagged']") });
        await pills.Locator(".actor-pill-add").ClickAsync();
        await pills.Locator("input.tag-search-input").FillAsync("E2E Blo");
        await pills.Locator(".tag-search-candidate", new() { HasText = "E2E Blonde" }).ClickAsync();
        await Expect(pills.Locator(".actor-pills-row .actor-pill").First).ToHaveTextAsync("E2E Blonde");

        // Saved: it is still there after a reload.
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await Expect(page.Locator(".actor-pills-row .actor-pill").First).ToHaveTextAsync("E2E Blonde");
        await Expect(page.Locator(".actor-pill-add")).ToHaveCountAsync(0);

        // And it can be removed again.
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Edit Cast" }).ClickAsync();
        await page.Locator(".actor-pills").First.HoverAsync();
        await page.Locator("button[aria-label='Remove E2E Blonde from Mei Tagged']").ClickAsync();
        await Expect(page.Locator(".actor-pills-row .actor-pill:not(.actor-pill-add)")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ActorTags_AreNotOfferedAsPlainMovieTags()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-2", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "E2E Hidden Actor Tag", IsActorTag = true });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Actor Tags" }).ClickAsync();
        await Expect(page.Locator(".tags-panel table")).ToContainTextAsync("E2E Hidden Actor Tag");

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "All Tags" }).ClickAsync();
        await Expect(page.Locator("body")).Not.ToContainTextAsync("E2E Hidden Actor Tag");
        Assert.NotNull(movie);
    }

    [Fact]
    public async Task HoveringAnActorOnASceneCard_OpensTheirTagsBelow_NotClippedByIt()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-3", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Mei Hover");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            (await db.Movies.FindAsync(movie.Id))!.MetaActresses = "Mei Hover";
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            var tag = new Tag { Name = "E2E Hover Tag", IsActorTag = true };
            db.Tags.Add(tag);
            db.Scenes.Add(new Scene { MovieId = movie.Id, StartSeconds = 0 });
            await db.SaveChangesAsync();
            db.MovieActorTags.Add(new MovieActorTag { MovieId = movie.Id, ActorId = actor.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");

        var card = page.Locator(".movie-scenes-grid .movie-scenes-card").First;
        var actorName = card.Locator(".actor-hover", new() { HasText = "Mei Hover" });
        var popover = actorName.Locator(".actor-hover-popover");
        await Expect(popover).Not.ToBeVisibleAsync();

        await actorName.HoverAsync();
        await Expect(popover).ToBeVisibleAsync();
        await Expect(popover).ToContainTextAsync("E2E Hover Tag");

        // Not cut off by the card: it is fully inside the window even where the card ends.
        var box = (await popover.BoundingBoxAsync())!;
        var cardBox = (await card.BoundingBoxAsync())!;
        var viewport = page.ViewportSize!;
        Assert.True(box.X >= 0 && box.X + box.Width <= viewport.Width);
        Assert.True(box.Y + box.Height > cardBox.Y + cardBox.Height || box.Y + box.Height <= viewport.Height);

        await page.Mouse.MoveAsync(5, 5);
        await Expect(popover).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ActorTagsPage_CreatesATagUnderAParent_WithTheSameModalAsTheMainTags()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Actor Tags" }).ClickAsync();

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create Tag" }).ClickAsync();
        await page.Locator("#new-tag-name").FillAsync("E2E Hair");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Expect(page.Locator(".tags-panel table")).ToContainTextAsync("E2E Hair");

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create Tag" }).ClickAsync();
        await page.Locator("#new-tag-name").FillAsync("E2E Long");
        await page.Locator("#new-tag-parent").FillAsync("E2E Ha");
        await page.Locator(".tag-parent-suggestion-item", new() { HasText = "E2E Hair" }).ClickAsync();
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();

        // The subtag sits under its parent, with the parent as a badge. Searching first keeps the other tests' tags out of the rows.
        await page.GetByPlaceholder("Search tags…").FillAsync("E2E Hair");
        var row = page.Locator(".tags-panel tbody tr").Nth(1);
        await Expect(row.Locator(".badge.bg-secondary")).ToHaveTextAsync("E2E Hair");
        await Expect(row).ToContainTextAsync("E2E Long");

        // Rename and delete work as on the main tags.
        await row.GetByTitle("Rename").ClickAsync();
        await row.Locator("input").FillAsync("E2E Short");
        await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(row).ToContainTextAsync("E2E Short");
        await row.GetByTitle("Delete").ClickAsync();
        await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
        await Expect(page.Locator(".tags-panel tbody tr", new() { HasText = "E2E Short" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MoviesPage_FilterByAnActorTagCarriedByASceneOfTheMovie()
    {
        var tagged = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-F1", MovieStatus.Got);
        var other = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-F2", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Mei Filter");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.MovieActors.AddRange(new MovieActor { MovieId = tagged.Id, ActorId = actor.Id }, new MovieActor { MovieId = other.Id, ActorId = actor.Id });
            var blonde = new Tag { Name = "E2E Filter Blonde", IsActorTag = true };
            var scene = new Scene { MovieId = tagged.Id, StartSeconds = 0 };
            db.AddRange(blonde, scene);
            await db.SaveChangesAsync();
            db.SceneActorTags.Add(new SceneActorTag { SceneId = scene.Id, MovieId = tagged.Id, ActorId = actor.Id, TagId = blonde.Id });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Filter" }).First.ClickAsync();
        await page.Locator(".sort-dropdown-item", new() { HasText = "Actor" }).First.ClickAsync();
        await page.Locator(".sort-dropdown-item", new() { HasText = "Actor tag" }).ClickAsync();
        await page.Locator(".sort-dropdown-subitem", new() { HasText = "E2E Filter Blonde" }).ClickAsync();

        await Expect(page.Locator(".poster-card", new() { HasText = "E2E-ATAG-F1" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".poster-card", new() { HasText = "E2E-ATAG-F2" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ActorTagsPage_FilterButton_OpensTheMoviesGridOnTheActorTagFilter()
    {
        var tagged = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-N1", MovieStatus.Got);
        var other = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-N2", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Mei Navigate");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            db.MovieActors.AddRange(new MovieActor { MovieId = tagged.Id, ActorId = actor.Id }, new MovieActor { MovieId = other.Id, ActorId = actor.Id });
            var tag = new Tag { Name = "E2E Navigate Redhead", IsActorTag = true };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.MovieActorTags.Add(new MovieActorTag { MovieId = tagged.Id, ActorId = actor.Id, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Actor Tags" }).ClickAsync();
        await page.GetByPlaceholder("Search tags…").FillAsync("E2E Navigate");
        await page.Locator(".tags-panel tbody tr", new() { HasText = "E2E Navigate Redhead" }).GetByTitle("Filter movies by this tag").ClickAsync();

        await Expect(page.Locator(".poster-card", new() { HasText = "E2E-ATAG-N1" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".poster-card", new() { HasText = "E2E-ATAG-N2" })).ToHaveCountAsync(0);

        // It is the Actor tag filter, ticked in its menu, not a Genre the menu doesn't list.
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Filter" }).First.ClickAsync();
        await page.Locator(".sort-dropdown-item", new() { HasText = "Actor" }).First.ClickAsync();
        await page.Locator(".sort-dropdown-item", new() { HasText = "Actor tag" }).ClickAsync();
        await Expect(page.Locator(".sort-dropdown-subitem.active", new() { HasText = "E2E Navigate Redhead" })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task AGenreBecomesAnActorTag_AndLeavesTheMoviesGenres()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-C1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var genre = new Tag { Name = "E2E Convert Blonde" };
            db.Tags.Add(genre);
            await db.SaveChangesAsync();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = genre.Id, IsExplicit = true });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync("/movies/tags");
        await page.GetByPlaceholder("Search tags…").First.FillAsync("E2E Convert");
        var row = page.Locator(".tags-panel tbody tr", new() { HasText = "E2E Convert Blonde" });
        await row.GetByTitle("Make an actor tag (removes it from its movies' genres)").ClickAsync();
        await Expect(row).ToContainTextAsync("remove it from 1 movie's genres");
        await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
        await Expect(page.Locator(".tags-panel tbody tr", new() { HasText = "E2E Convert Blonde" })).ToHaveCountAsync(0);

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Actor Tags" }).ClickAsync();
        await Expect(page.Locator(".tags-panel table")).ToContainTextAsync("E2E Convert Blonde");
        await using var check = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Empty(check.MovieTags.Where(mt => mt.MovieId == movie.Id).ToList());
    }
}
