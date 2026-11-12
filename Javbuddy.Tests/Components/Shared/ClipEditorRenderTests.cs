using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>What ClipEditor re-renders on typing in its forms. Every row's action buttons (ClipRowActions)
/// are swapped for a counting subclass that tallies each time its parent renders it, so the tally moves
/// exactly when ClipEditor (scene rows) or a row component re-renders the tree. bUnit's own RenderCount
/// can't tell: it also moves for a child's render.</summary>
public class ClipEditorRenderTests : ClipEditorTestBase
{
    private int rowRenders;

    private sealed class CountingRowActions(Action onParameters) : ClipRowActions
    {
        protected override void OnParametersSet() => onParameters();
    }

    public ClipEditorRenderTests() =>
        ComponentFactories.Add(t => t == typeof(ClipRowActions), _ => new CountingRowActions(() => Interlocked.Increment(ref rowRenders)));

    [Fact]
    public async Task TypingInTheSceneForm_DoesNotRerenderTheEditor_ButTheFormStillFollows()
    {
        await sceneService.AddSceneAsync(movieId, 0, 600, "Intro");
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Clip");
        await apexService.AddApexAsync(movieId, 30, []);
        var cut = RenderClipEditor();
        await cut.Find(".scene-editor-add-btn").ClickAsync();

        Assert.True(rowRenders >= 3);
        var renders = rowRenders;
        await cut.Find(".scene-editor-title-input").InputAsync("Bath");
        await cut.Find(".scene-editor-start-input").InputAsync("15:00");
        await cut.Find(".scene-editor-end-input").InputAsync("17:00");

        Assert.Equal(renders, rowRenders);
        // What depends on the typed text still updates inside the form.
        Assert.NotEmpty(cut.FindAll(".scene-editor-clear-end-btn"));
        await cut.Find(".scene-editor-clear-end-btn").ClickAsync();
        Assert.Empty(cut.FindAll(".scene-editor-clear-end-btn"));
        Assert.Equal(string.Empty, cut.Find(".scene-editor-end-input").GetAttribute("value") ?? string.Empty);

        await cut.Find(".scene-editor-form").SubmitAsync();
        Assert.Equal(2, (await sceneService.GetScenesAsync(movieId)).Count);
        Assert.Contains(cut.FindAll(".scene-editor-name"), n => n.TextContent == "Bath");
    }

    [Fact]
    public async Task TypingInTheHighlightAndApexForms_DoesNotRerenderTheEditor_ButTheFormsStillFollow()
    {
        await sceneService.AddSceneAsync(movieId, 0, 600, "Intro");
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Clip");
        await apexService.AddApexAsync(movieId, 30, []);
        var cut = RenderClipEditor();
        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        await cut.Find(".apex-editor-add-btn").ClickAsync();

        var renders = rowRenders;
        await cut.Find(".highlight-editor-title-input").InputAsync("Best");
        await cut.Find(".highlight-editor-start-input").InputAsync("1:00");
        await cut.Find(".highlight-editor-end-input").InputAsync("2:00");
        await cut.Find(".apex-editor-time-input").InputAsync("1:30");
        await cut.Find(".apex-editor-lead-in-input").InputAsync("5");

        Assert.Equal(renders, rowRenders);
        await cut.Find(".highlight-editor-form").SubmitAsync();
        await cut.Find(".apex-editor-form").SubmitAsync();
        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId), h => h.Title == "Best");
        Assert.Equal((60d, 120d, "Best"), (highlight.StartSeconds, highlight.EndSeconds, highlight.Title));
        Assert.Contains(await apexService.GetApexesAsync(movieId), a => a.Seconds == 90);
    }
}
