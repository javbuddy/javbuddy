using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>A long actor tag, or one under a parent ("Parent › Child"), is shown in full on Movie Detail: in the
/// cast pills and in the hover popovers, wrapping instead of being clipped or running out of its popover.</summary>
[Collection(E2ECollection.Name)]
public class ActorTagLongLabelFlowTests
{
    private const string Overflowing =
        "e => e.scrollWidth > e.clientWidth + 1 || e.scrollHeight > e.clientHeight + 1";

    private readonly E2EFixture fixture;

    public ActorTagLongLabelFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task LongAndNestedActorTags_AreNotCutOff()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-ATAG-LONG", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Mei Longtag");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            (await db.Movies.FindAsync(movie.Id))!.MetaActresses = "Mei Longtag";
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            var parent = new Tag { Name = "E2E Hair colour and styling", IsActorTag = true };
            db.Tags.Add(parent);
            await db.SaveChangesAsync();
            var child = new Tag { Name = "E2E Strawberry blonde with long loose curls", IsActorTag = true, ParentTagId = parent.Id };
            db.Tags.Add(child);
            db.Scenes.Add(new Scene { MovieId = movie.Id, StartSeconds = 0 });
            await db.SaveChangesAsync();
            db.MovieActorTags.Add(new MovieActorTag { MovieId = movie.Id, ActorId = actor.Id, TagId = child.Id });
            await db.SaveChangesAsync();
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");

            var pill = page.Locator(".actor-pills-row .actor-pill").First;
            await Expect(pill).ToContainTextAsync("Strawberry blonde with long loose curls");
            Assert.False(await pill.EvaluateAsync<bool>(Overflowing), "the cast pill is cut off");

            var card = page.Locator(".movie-scenes-grid .movie-scenes-card").First;
            var actorName = card.Locator(".actor-hover", new() { HasText = "Mei Longtag" });
            await actorName.HoverAsync();
            var popover = actorName.Locator(".actor-hover-popover");
            await Expect(popover).ToBeVisibleAsync();
            var hoverPill = popover.Locator(".actor-hover-pill").First;
            Assert.False(await popover.EvaluateAsync<bool>(Overflowing), "the hover popover is overrun");
            var popoverBox = (await popover.BoundingBoxAsync())!;
            var pillBox = (await hoverPill.BoundingBoxAsync())!;
            Assert.True(pillBox.X + pillBox.Width <= popoverBox.X + popoverBox.Width + 1, "the hover pill runs out of its popover");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
