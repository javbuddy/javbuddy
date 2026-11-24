using Bunit;
using Javbuddy.Services.ClipExport;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>ClipEditor's lossless clip download: the edit form's "Download clip" prepares the clip, then the browser downloads it.</summary>
public class ClipEditorExportTests : ClipEditorTestBase
{
    [Fact]
    public async Task NoDownloadButton_WhenFfmpegIsMissing_OrThereIsNoLocalVideo()
    {
        await highlightService.AddHighlightAsync(movieId, 600, 640, "Climax");

        var cut = RenderClipEditor();
        cut.WaitForElement(".highlight-editor-edit-btn").Click();
        cut.WaitForElement(".highlight-editor-cancel-btn");
        Assert.Empty(cut.FindAll(".highlight-editor-download-btn"));

        clipExport.IsAvailable.Returns(true);
        var noVideo = RenderClipEditor(hasLocalVideo: false);
        noVideo.WaitForElement(".highlight-editor-edit-btn").Click();
        noVideo.WaitForElement(".highlight-editor-cancel-btn");
        Assert.Empty(noVideo.FindAll(".highlight-editor-download-btn"));
    }

    [Fact]
    public async Task RowsHaveNoDownloadButton_EveryEditFormHasOne()
    {
        clipExport.IsAvailable.Returns(true);
        await sceneService.AddSceneAsync(movieId, 0, null, null);
        await highlightService.AddHighlightAsync(movieId, 600, 640, "Climax");
        await apexService.AddApexAsync(movieId, 620, []);

        var cut = RenderClipEditor();
        cut.WaitForElement(".apex-editor-edit-btn");
        Assert.Empty(cut.FindAll(".clip-download-btn"));

        cut.Find(".scene-editor-edit-btn").Click();
        cut.WaitForElement(".scene-editor-download-btn");
        cut.Find(".scene-editor-cancel-btn").Click();

        cut.Find(".highlight-editor-edit-btn").Click();
        cut.WaitForElement(".highlight-editor-download-btn");
        cut.Find(".highlight-editor-cancel-btn").Click();

        cut.Find(".apex-editor-edit-btn").Click();
        cut.WaitForElement(".apex-editor-download-btn");
    }

    [Fact]
    public async Task Download_PreparesTheClip_AndStartsTheBrowserDownload_WithoutAMessage()
    {
        clipExport.IsAvailable.Returns(true);
        await highlightService.AddHighlightAsync(movieId, 600, 640, "Climax");
        clipExport.PrepareAsync(ClipKind.Highlight, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ClipExportResult(true, "tok", "ABC-123 - Climax [00-09-56 - 00-10-40].mp4", 600, 596, 640, null));
        var download = module.SetupVoid("download", "/api/clip-exports/tok");
        download.SetVoidResult();

        var cut = RenderClipEditor();
        cut.WaitForElement(".highlight-editor-edit-btn").Click();
        cut.WaitForElement(".highlight-editor-download-btn").Click();

        cut.WaitForAssertion(() => Assert.Single(download.Invocations));
        await clipExport.Received(1).PrepareAsync(ClipKind.Highlight, Arg.Any<int>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.False(cut.Find(".highlight-editor-download-btn").HasAttribute("disabled")));
        Assert.Empty(cut.FindAll(".scene-editor-error"));
        Assert.DoesNotContain("Downloading", cut.Markup);
    }

    [Fact]
    public async Task Download_SpinsWhileTheClipIsPrepared()
    {
        clipExport.IsAvailable.Returns(true);
        await apexService.AddApexAsync(movieId, 620, []);
        var pending = new TaskCompletionSource<ClipExportResult>();
        clipExport.PrepareAsync(ClipKind.Apex, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        var cut = RenderClipEditor();
        cut.WaitForElement(".apex-editor-edit-btn").Click();
        cut.WaitForElement(".apex-editor-download-btn").Click();

        cut.WaitForAssertion(() =>
        {
            var button = cut.Find(".apex-editor-download-btn");
            Assert.True(button.HasAttribute("disabled"));
            Assert.NotNull(button.QuerySelector(".clip-download-spinner"));
        });

        pending.SetResult(ClipExportResult.Failed("stopped"));
        cut.WaitForAssertion(() => Assert.Null(cut.Find(".apex-editor-download-btn").QuerySelector(".clip-download-spinner")));
    }

    [Fact]
    public async Task DownloadFailure_ShowsTheError_AndStartsNoDownload()
    {
        clipExport.IsAvailable.Returns(true);
        await apexService.AddApexAsync(movieId, 620, []);
        clipExport.PrepareAsync(ClipKind.Apex, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ClipExportResult.Failed("The movie's video file wasn't found."));

        var cut = RenderClipEditor();
        cut.WaitForElement(".apex-editor-edit-btn").Click();
        cut.WaitForElement(".apex-editor-download-btn").Click();

        cut.WaitForAssertion(() =>
            Assert.Equal("Download failed: The movie's video file wasn't found.", cut.Find(".scene-editor-error").TextContent));
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "download");
    }

    [Fact]
    public async Task ClosingTheEditor_DuringAnExport_CancelsIt_AndStartsNoDownload()
    {
        clipExport.IsAvailable.Returns(true);
        await highlightService.AddHighlightAsync(movieId, 600, 640, "Climax");
        var cancelled = new TaskCompletionSource();
        clipExport.PrepareAsync(ClipKind.Highlight, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.ArgAt<CancellationToken>(2);
                await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => cancelled.TrySetResult(), TaskScheduler.Default);
                token.ThrowIfCancellationRequested();
                return ClipExportResult.Failed("unreachable");
            });

        var cut = RenderClipEditor();
        cut.WaitForElement(".highlight-editor-edit-btn").Click();
        cut.WaitForElement(".highlight-editor-download-btn").Click();
        cut.WaitForElement(".clip-download-spinner");

        await DisposeComponentsAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "download");
    }
}
