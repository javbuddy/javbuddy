using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The scene editor's row thumbnail plays the scene's hover preview. The
/// preview is a WebM video, which only a real browser can show to be decoding rather
/// than a broken image — bUnit sees just the markup.</summary>
[Collection(E2ECollection.Name)]
public class SceneEditorPreviewFlowTests
{
    private readonly E2EFixture fixture;

    public SceneEditorPreviewFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task HoveringASceneRow_PlaysItsWebmPreview()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-SCNPREV-1", MovieStatus.Got);
        await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, movie);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var sceneId = (await scope.ServiceProvider.GetRequiredService<IMovieSceneService>().AddSceneAsync(movie.Id, 0, 3, "Intro")).SceneId!.Value;
            // The real screenshot and WebM preview, from the fixture clip via ffmpeg.
            Assert.Equal(movie.Id, await scope.ServiceProvider.GetRequiredService<ISceneMediaService>().GenerateAsync(sceneId));
        }

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoInteractiveAsync($"/movies/{movie.Code}");
            await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
            var row = page.Locator(".video-player-side-panel .scene-editor-item");
            await Expect(row.Locator(".scene-editor-thumb img")).ToBeVisibleAsync();

            await row.HoverAsync();

            var video = row.Locator(".scene-editor-thumb video");
            await Expect(video).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("/scene-image/\\d+/preview"));
            // Decoded and playing, muted so Chrome allows the autoplay.
            await page.WaitForFunctionAsync(
                "() => { const v = document.querySelector('.scene-editor-thumb video'); return v && !v.error && v.readyState >= 2 && v.muted && !v.paused; }");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
