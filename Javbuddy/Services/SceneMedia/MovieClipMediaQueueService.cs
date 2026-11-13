using Javbuddy.Services.Scenes;

namespace Javbuddy.Services.SceneMedia;

public interface IMovieClipMediaQueueService
{
    /// <summary>Queues the generation of the screenshots and hover previews of a movie's scenes, highlights and
    /// apexes, for each kind that has a clip still lacking media this image-cache mode keeps. Does nothing for a
    /// kind whose clips all have theirs.</summary>
    Task EnsureQueuedAsync(int movieId, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<ApexItem> apexes, CancellationToken ct = default);
}

/// <summary>What Movie Detail asks for after reading a movie's clips: the media of the clips shown is generated in
/// the background (<see cref="SceneMediaQueue"/>), and the cards reload as each piece is written.</summary>
public class MovieClipMediaQueueService(ISceneMediaService sceneMedia, IHighlightMediaService highlightMedia, IApexMediaService apexMedia) : IMovieClipMediaQueueService
{
    public async Task EnsureQueuedAsync(int movieId, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<ApexItem> apexes, CancellationToken ct = default)
    {
        if (scenes.Any(s => s.ThumbVersion is null || s.PreviewVersion is null))
        {
            await sceneMedia.EnsureQueuedAsync(movieId, ct);
        }
        if (highlights.Any(h => h.ThumbVersion is null || h.PreviewVersion is null))
        {
            await highlightMedia.EnsureQueuedAsync(movieId, ct);
        }
        if (apexes.Any(a => apexMedia.IsMissingMedia(a.ThumbVersion, a.PreviewVersion)))
        {
            await apexMedia.EnsureQueuedAsync(movieId, ct);
        }
    }
}
