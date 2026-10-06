using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>One user-uploaded actor gallery photo. Both variants are optimized WebP files held
/// under opaque keys in the local image cache; database rows never expose the cache path.</summary>
public class ActorPhoto
{
    public int Id { get; set; }

    public int ActorId { get; set; }

    public int? AlbumId { get; set; }

    public Guid ThumbStorageId { get; set; }

    public Guid FullStorageId { get; set; }

    /// <summary>Opaque key for the original uploaded bytes in the durable actor-image data store.</summary>
    public Guid? SourceStorageId { get; set; }

    /// <summary>The extension SourceStorageId's bytes are stored under, e.g. ".jpg", so the store
    /// reads them by exact key. Null for sources stored before it was recorded.</summary>
    [StringLength(10)]
    public string? SourceExtension { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public double? AspectRatio { get; set; }

    public Actor? Actor { get; set; }

    public ActorAlbum? Album { get; set; }
}
