using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

public enum DiscoveredMovieStatus
{
    New,
    Dismissed,
    Added,
}

/// <summary>A movie found by a Movies &gt; Discover studio scan (see <c>IStudioDiscoverySource</c>),
/// persisted so results survive between the weekly <c>MovieDiscoveryScanTask</c> run and whenever the
/// page is next opened. Upserted by <see cref="Code"/> on every scan — a <see cref="Dismissed"/> or
/// <see cref="Added"/> status is left alone by later scans so it doesn't reappear (see
/// MovieDiscoveryService.RunScanAsync).</summary>
public class DiscoveredMovieCandidate
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string Studio { get; set; } = null!;

    /// <summary>Which <c>IStudioDiscoverySource</c> last saw this candidate.</summary>
    [Required]
    [StringLength(50)]
    public string SourceName { get; set; } = null!;

    public string? CoverImageUrl { get; set; }

    /// <summary>Still-image URLs scraped from the studio's detail page, in gallery order.</summary>
    public List<string> GalleryImageUrls { get; set; } = [];

    /// <summary>Actress name(s) as credited on the studio's detail page. Matched against tracked
    /// Actors at display time rather than persisted (see
    /// IMovieDiscoveryService.GetCandidatesAsync), so a later-added/renamed actor still matches.</summary>
    public List<string> ActressNames { get; set; } = [];

    /// <summary>Null when the source couldn't report an exact date (e.g. a pre-order listing that
    /// only gives a release window for the whole page, not per item).</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>True when this came from a pre-order/"coming soon" listing rather than an
    /// already-released one.</summary>
    public bool IsUpcoming { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DiscoveredMovieStatus Status { get; set; } = DiscoveredMovieStatus.New;
}
