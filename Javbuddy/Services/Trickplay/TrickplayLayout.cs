namespace Javbuddy.Services.Trickplay;

/// <summary>Trickplay (scrub-preview) layout for one movie's video, whichever source it came from
/// (Javbuddy's own generated tiles or Jellyfin's): thumbnails of
/// <see cref="Width"/>x<see cref="Height"/> px laid out <see cref="TileWidth"/>x<see cref="TileHeight"/>
/// per tile sheet, one every <see cref="IntervalMs"/> ms. <see cref="TileUrlTemplate"/> is a
/// browser-reachable URL with an <c>{index}</c> placeholder for the zero-based tile sheet. The first
/// thumbnail is at <see cref="StartSeconds"/> into the video: 0, except for a highlight's own set
///, which covers <see cref="DurationSeconds"/> from the highlight's start.</summary>
public record TrickplayLayout(
    int Width,
    int Height,
    int TileWidth,
    int TileHeight,
    int ThumbnailCount,
    int IntervalMs,
    double DurationSeconds,
    string TileUrlTemplate,
    double StartSeconds = 0);
