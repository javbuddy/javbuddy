namespace Javbuddy.Services.Actors;

/// <summary>The /actor-photo URLs. A photo's stored files never change once uploaded (its storage ids are set at
/// upload and regenerated thumbnails are rebuilt from the same source into the same ids), so a URL carrying the
/// photo's version token is served as immutable: the browser keeps it instead of revalidating every visit.</summary>
public static class ActorPhotoUrls
{
    /// <summary>A short token unique to the photo's stored files, so a photo id that ever pointed at different
    /// files (e.g. after restoring an older backup) never matches a URL the browser cached for the old ones.</summary>
    public static string Version(Guid thumbStorageId) => thumbStorageId.ToString("N")[..8];

    public static string Url(int photoId, string variant, string? version) =>
        string.IsNullOrEmpty(version)
            ? $"/actor-photo/{photoId}/{variant}"
            : $"/actor-photo/{photoId}/{variant}?v={Uri.EscapeDataString(version)}";
}
