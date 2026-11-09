using Javbuddy.Models;
using Javbuddy.Services.MediaServer;

namespace Javbuddy.Services.Jellyfin;

/// <summary>Applies a Jellyfin/MediaServer item match onto a Movie's Jellyfin* link fields — the
/// "Open in Jellyfin" link only; it does not decide ownership (the local library does) and carries no
/// metadata (that comes from the local file-based source, see Services/LocalLibrary, or javinizer-go).</summary>
public static class JellyfinMetadataMapper
{
    public static void ApplyMatch(Movie movie, MediaServerItemDto item)
    {
        movie.JellyfinItemId = item.Id;
        movie.JellyfinServerId = item.ServerId;
        movie.JellyfinCheckedAt = DateTime.UtcNow;
    }
}
