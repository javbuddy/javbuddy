using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public class MovieClipMediaQueueServiceTests
{
    private readonly ISceneMediaService sceneMedia = Substitute.For<ISceneMediaService>();
    private readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    private readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();
    private readonly MovieClipMediaQueueService service;

    public MovieClipMediaQueueServiceTests()
    {
        service = new MovieClipMediaQueueService(sceneMedia, highlightMedia, apexMedia);
        apexMedia.ServesVariant(Arg.Any<string>()).Returns(true);
        apexMedia.ServesPreview.Returns(true);
    }

    private static SceneItem Scene(long? thumb, long? preview) =>
        new(1, 1, "Scene", null, 0, 10, 10) { ThumbVersion = thumb, PreviewVersion = preview };

    private static HighlightItem Highlight(long? thumb, long? preview) =>
        new(1, 1, "Highlight", null, 0, 10, false, 0) { ThumbVersion = thumb, PreviewVersion = preview };

    private static ApexItem Apex(long? thumb, long? preview) =>
        new(1, 1, 5, []) { ThumbVersion = thumb, PreviewVersion = preview };

    [Fact]
    public async Task EveryClipWithItsMedia_QueuesNothing()
    {
        await service.EnsureQueuedAsync(7, [Scene(1, 1)], [Highlight(1, 1)], [Apex(1, 1)]);

        await sceneMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await highlightMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await apexMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task NoClipsAtAll_QueuesNothing()
    {
        await service.EnsureQueuedAsync(7, [], [], []);

        await sceneMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await highlightMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await apexMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task ASceneLackingEitherPiece_QueuesTheScenes_AndOnlyThose()
    {
        await service.EnsureQueuedAsync(7, [Scene(1, null)], [Highlight(1, 1)], [Apex(1, 1)]);

        await sceneMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
        await highlightMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await apexMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task AHighlightLackingEitherPiece_QueuesTheHighlights_AndOnlyThose()
    {
        await service.EnsureQueuedAsync(7, [Scene(1, 1)], [Highlight(null, 1)], [Apex(1, 1)]);

        await highlightMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
        await sceneMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await apexMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task AnApexLackingMediaTheModeKeeps_QueuesTheApexes()
    {
        await service.EnsureQueuedAsync(7, [Scene(1, 1)], [Highlight(1, 1)], [Apex(1, null)]);

        await apexMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
        await sceneMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
        await highlightMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task AnApexWithoutAPreviewInThumbnailsMode_IsNotAReasonToQueue()
    {
        apexMedia.ServesPreview.Returns(false);

        await service.EnsureQueuedAsync(7, [], [], [Apex(1, null)]);

        await apexMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);
    }

    [Fact]
    public async Task EveryKindWithGaps_QueuesEachOfThem()
    {
        await service.EnsureQueuedAsync(7, [Scene(null, null)], [Highlight(null, null)], [Apex(null, null)]);

        await sceneMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
        await highlightMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
        await apexMedia.Received(1).EnsureQueuedAsync(7, Arg.Any<CancellationToken>());
    }
}
