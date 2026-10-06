using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Tracks one converted/resized local-library image (WebP) in the local image cache.
/// The actual bytes live on disk at LocalImageCache.GetStoragePath(StorageId) — an opaque,
/// hash-sharded path, not the movie's code/original filename (see ILocalImageCacheService).</summary>
public class CachedImage
{
    public int Id { get; set; }

    /// <summary>The movie's code, e.g. "IPX-535".</summary>
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    /// <summary>"poster", "fanart", "extrafanart", "scene", "highlight" or
    /// "deovr-timeline".</summary>
    [Required]
    [StringLength(20)]
    public string Role { get; set; } = string.Empty;

    /// <summary>0 for poster/fanart; the natural-sort position (0-based) within the movie's
    /// extrafanart folder for that role.</summary>
    public int Index { get; set; }

    /// <summary>"thumb" (resized, small) or "full" (original resolution, WebP re-encode only).</summary>
    [Required]
    [StringLength(20)]
    public string Variant { get; set; } = string.Empty;

    /// <summary>Opaque key used to build the on-disk cache path — never the movie code/filename.</summary>
    public Guid StorageId { get; set; }

    /// <summary>Size/last-write time of the *source* file this was converted from, used to detect
    /// a changed source without re-reading/re-converting every scan. Unused (left at their
    /// defaults) for a row populated from SourceUrl instead — see below.</summary>
    public long SourceLength { get; set; }

    public DateTime SourceLastWriteUtc { get; set; }

    /// <summary>Set only for a poster cropped from a Missing (non-local) movie's remote cover image
    /// — see RemotePosterCropService. Freshness for these rows is "does this still match the
    /// movie's current MetaCoverUrl", not a local file's size/last-write time; null for every
    /// locally-sourced row.</summary>
    [StringLength(2000)]
    public string? SourceUrl { get; set; }

    /// <summary>Set only for a scene screenshot/preview (Role "scene", Index = Scene.Id,
    ///) or a highlight's (Role "highlight", Index = MovieHighlight.Id):
    /// the start, in milliseconds, the media was generated for. One whose start has since moved no
    /// longer matches, so its media is regenerated.</summary>
    public long? SceneStartMs { get; set; }

    /// <summary>With SceneStartMs: the scene's effective end, in milliseconds, the media was sampled
    /// for (null when it was unknown). A scene whose end moved — its own, or through a following
    /// scene being added, moved or deleted — no longer matches either.</summary>
    public long? SceneEndMs { get; set; }

    /// <summary>Set only for a DeoVR timeline mosaic (Role "deovr-timeline"): the identity
    /// of the trickplay set it was built from. One built from another set no longer matches, so it's
    /// rebuilt.</summary>
    [StringLength(64)]
    public string? SourceIdentity { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
