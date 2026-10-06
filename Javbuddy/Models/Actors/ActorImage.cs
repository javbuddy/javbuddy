using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Tracks one converted/resized actor image (WebP) in the actor image cache. The actual
/// bytes live on disk at ActorImageCache.GetStoragePath(StorageId). ActorId is a loose reference
/// (no FK constraint), the same pattern CachedImage.Code uses for Movie — rows for actors that no
/// longer exist are pruned by ImageCacheTask, not by a cascade delete.</summary>
public class ActorImage
{
    public int Id { get; set; }

    public int ActorId { get; set; }

    /// <summary>The code of the movie whose ".actors" subfolder this image was found in, e.g.
    /// "REBD-483" — used to re-resolve the live source file for freshness checks.</summary>
    [Required]
    [StringLength(50)]
    public string SourceMovieCode { get; set; } = string.Empty;

    /// <summary>"thumb" (resized, small) or "full" (original resolution, WebP re-encode only).</summary>
    [Required]
    [StringLength(20)]
    public string Variant { get; set; } = string.Empty;

    /// <summary>Opaque key used to build the on-disk cache path.</summary>
    public Guid StorageId { get; set; }

    /// <summary>Opaque key for canonical actor-image bytes in the durable actor-image data store.
    /// Null for cache-only images acquired before durable storage was introduced.</summary>
    public Guid? SourceStorageId { get; set; }

    /// <summary>The extension SourceStorageId's bytes are stored under, e.g. ".jpg", so the store
    /// reads them by exact key. Null for sources stored before it was recorded.</summary>
    [StringLength(10)]
    public string? SourceExtension { get; set; }

    public double? CropX { get; set; }

    public double? CropY { get; set; }

    public double? CropWidth { get; set; }

    public double? CropHeight { get; set; }

    /// <summary>Size/last-write time of the *source* file this was converted from, used to detect
    /// a changed source without re-reading/re-converting every scan.</summary>
    public long SourceLength { get; set; }

    public DateTime SourceLastWriteUtc { get; set; }

    /// <summary>Set only for an image downloaded from an actor's remote ThumbnailUrl fallback.
    /// Freshness is "does this still match the actor's current ThumbnailUrl", not a local file's
    /// size/last-write time; null for every locally-sourced row.</summary>
    [StringLength(2000)]
    public string? SourceUrl { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
