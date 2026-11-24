using Bunit;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>The ClipEditor re-reading what its clips inherit when the movie's own tags change.</summary>
public class ClipEditorTagInheritanceTests : ClipEditorTestBase
{
    /// <summary>The real service, except a scene tag's "pass down" toggle waits for the test and then fails.</summary>
    private sealed class GatedTagInheritance(IClipTagInheritanceService inner) : IClipTagInheritanceService
    {
        public TaskCompletionSource<SceneOperationResult> Toggle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<SceneTagItem>> GetPropagatingMovieTagsAsync(int movieId, CancellationToken ct = default) =>
            inner.GetPropagatingMovieTagsAsync(movieId, ct);

        public Task<SceneOperationResult> SetSceneTagPropagatesDownAsync(int sceneId, int tagId, bool propagatesDown, CancellationToken ct = default) =>
            Toggle.Task;

        public Task<SceneOperationResult> SetExcludedAsync(ClipKind kind, int clipId, int tagId, bool excluded, CancellationToken ct = default) =>
            inner.SetExcludedAsync(kind, clipId, tagId, excluded, ct);
    }

    [Fact]
    public async Task MovieTagsChangingDuringASaveThatFails_StillReloadsWhatTheClipsInherit()
    {
        var gated = new GatedTagInheritance(new ClipTagInheritanceService(factory));
        Services.AddSingleton<IClipTagInheritanceService>(gated);
        var outdoorId = SeedTag("Outdoor");
        var nightId = SeedTag("Night");
        using (var db = factory.CreateDbContext())
        {
            var scene = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 600 };
            db.Scenes.Add(scene);
            db.SaveChanges();
            db.SceneTags.Add(new SceneTag { SceneId = scene.Id, TagId = outdoorId });
            db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = nightId });
            db.SaveChanges();
        }

        var cut = RenderClipEditor();
        cut.WaitForElement(".scene-editor-edit-btn").Click();
        // The scene's own "Outdoor" ↓: the save stays in flight.
        var toggle = cut.Find(".clip-tag-chip-propagate").ClickAsync(new());
        Assert.DoesNotContain(cut.FindAll(".inherited-chip"), c => c.TextContent.Contains("Night"));

        // Meanwhile the movie starts passing "Night" down (MovieTagsEditor), and the save then fails.
        using (var db = factory.CreateDbContext())
        {
            db.MovieTags.Single(mt => mt.MovieId == movieId && mt.TagId == nightId).PropagatesDown = true;
            db.SaveChanges();
        }
        await cut.InvokeAsync(() => cut.Instance.MovieTagsChangedAsync());
        gated.Toggle.SetResult(SceneOperationResult.Fail("Tag is not on this scene."));
        await toggle;

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll(".inherited-chip"), c => c.TextContent.Contains("Night"));
            Assert.Contains("Tag is not on this scene.", cut.Markup);
        });
    }
}
