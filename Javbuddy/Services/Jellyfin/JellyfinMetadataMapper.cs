using Javbuddy.Models;
using Javbuddy.Services.MediaServer;

namespace Javbuddy.Services.Jellyfin;

/// <summary>Applies a Jellyfin/MediaServer item match onto a Movie's Jellyfin* link fields — ownership/
/// "do I already have this" only. Descriptive and technical metadata come from the local
/// file-based source (see Services/LocalLibrary) or javinizer-go instead.</summary>
public static class JellyfinMetadataMapper
{
    public static void ApplyMatch(Movie movie, MediaServerItemDto item)
    {
        movie.JellyfinItemId = item.Id;
        movie.JellyfinServerId = item.ServerId;
        movie.JellyfinCheckedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(item.LibraryId))
        {
            movie.JellyfinLibraryId = item.LibraryId;
        }
        if (!string.IsNullOrWhiteSpace(item.LibraryName))
        {
            movie.JellyfinLibraryName = item.LibraryName;
        }
    }

    public static void ApplyMatch(Movie movie, JellyfinItemDto item) => ApplyMatch(movie, (MediaServerItemDto)item);
}
