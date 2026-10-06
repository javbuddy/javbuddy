namespace Javbuddy.Services.Movies;

/// <summary>Recognises side-by-side VR video by its frame shape, so generated pictures (scene
/// screenshots and previews, trickplay) show only the left eye.</summary>
public static class SideBySideVideo
{
    /// <summary>Side-by-side VR frames are 2:1 (e.g. 3840x1920, 8192x4096); flat video is 16:9.</summary>
    public const double MinAspect = 1.9;

    public static bool IsSideBySide(int? width, int? height) =>
        width is > 0 && height is > 0 && (double)width.Value / height.Value >= MinAspect;
}
