namespace Javbuddy.Components.Pages.MovieDetailSections;

/// <summary>The parts of Movie Detail's data an edit can change. A handler reloads only the parts its change can
/// affect; anything it isn't sure about reloads <see cref="All"/> (what the first load does too).</summary>
[Flags]
public enum MovieParts
{
    // Nothing beyond the movie row with its files and tags (status, flags, metadata text, NFO drift,
    // tag chips), which every reload reads.
    Core = 0,
    // The poster's cache-busting version (a disk stat and two queries).
    Poster = 2,
    // Scenes, highlights and apexes, and their generated media being queued.
    Clips = 4,
    // The movie's tracked actors the clips can be assigned to.
    SceneCast = 8,
    // The cast chips with their ages and avatars, read from the movie's cast text.
    Cast = 16,
    // The Jellyfin link, even when the movie's Jellyfin ids are the same.
    Jellyfin = 32,
    // The extrafanart folder listing.
    Images = 64,
    All = Poster | Clips | SceneCast | Cast | Jellyfin | Images,
}
