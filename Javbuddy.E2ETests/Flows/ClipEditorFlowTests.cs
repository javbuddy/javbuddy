using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The nested scene editor in the player modal, end to end over a real circuit: a scene,
/// a highlight inside it and an apex inside that, with the apex's tag rolling up to the scene and the scene's
/// actor flowing down to the apex.</summary>
[Collection(E2ECollection.Name)]
public class ClipEditorFlowTests
{
    private const string Video = "video.video-player-video";

    private readonly E2EFixture fixture;

    public ClipEditorFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task SceneHighlightApex_NestAndShowRolledUpTagsAndInheritedActors()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-CLIP-1", MovieStatus.Got);
        var actor = await DbSeeding.SeedActorAsync(fixture.DbFactory, "Aika Clip");
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.JellyfinItemId = "fake-jf-item-clip-1";
            m.JellyfinServerId = FakeJellyfinServer.ServerId;
            // The fixture video is a 3-second clip.
            m.MediaDurationSeconds = 3;
            db.MovieActors.Add(new MovieActor { MovieId = m.Id, ActorId = actor.Id });
            db.Tags.Add(new Tag { Name = "E2E Rollup" });
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }

        var page = await fixture.NewPageAsync();
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}");
        var panel = page.Locator(".video-player-side-panel");

        // A scene over the whole clip: new scenes inherit the cast.
        await panel.Locator(".scene-editor-add-btn").ClickAsync();
        await panel.Locator(".scene-editor-start-input").FillAsync("0");
        await panel.Locator(".scene-editor-save-btn").ClickAsync();
        await Expect(panel.Locator(".scene-editor-row")).ToHaveCountAsync(1);
        await Expect(panel.Locator(".scene-editor-row .implicit-chip")).ToHaveTextAsync("Aika Clip");

        // A highlight inside it.
        await panel.Locator(".highlight-editor-add-btn").ClickAsync();
        await panel.Locator(".highlight-editor-start-input").FillAsync("0.5");
        await panel.Locator(".highlight-editor-end-input").FillAsync("2");
        await panel.Locator(".highlight-editor-save-btn").ClickAsync();
        await Expect(panel.Locator(".scene-editor-item .highlight-editor-item")).ToHaveCountAsync(1);

        // An apex inside the highlight, tagged; its actor is inherited, shown grayed in the form.
        await panel.Locator(".apex-editor-add-btn").ClickAsync();
        await panel.Locator(".apex-editor-time-input").FillAsync("1");
        await Expect(panel.Locator(".apex-editor-form .clip-actor-chip-inherited")).ToHaveTextAsync("Aika Clip");
        await panel.Locator(".apex-editor-form .tag-search-input").FillAsync("E2E Roll");
        await panel.Locator(".apex-editor-form .tag-search-candidate").First.ClickAsync();
        await panel.Locator(".apex-editor-save-btn").ClickAsync();

        var apexRow = panel.Locator(".highlight-editor-item .apex-editor-item .apex-editor-row");
        await Expect(apexRow).ToHaveCountAsync(1);
        await Expect(apexRow.Locator(".implicit-chip")).ToHaveTextAsync("Aika Clip");
        await Expect(panel.Locator(".scene-editor-row .implicit-chip").Last).ToHaveTextAsync("E2E Rollup");
        await Expect(panel.Locator(".highlight-editor-row .implicit-chip").Last).ToHaveTextAsync("E2E Rollup");
    }
}
