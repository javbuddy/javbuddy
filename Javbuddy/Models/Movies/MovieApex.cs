namespace Javbuddy.Models;

/// <summary>A single moment inside a movie where an actor hits an "apex". A point in
/// time rather than a range, and independent of highlights and scenes. Never written to the file.</summary>
public class MovieApex
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public double Seconds { get; set; }

    /// <summary>This apex's own lead-in and tail in seconds; null uses the default from
    /// ApexPlaybackSettings. Relative to Seconds, so moving the apex keeps them.</summary>
    public double? LeadInSeconds { get; set; }

    public double? TailSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Marked as a favorite apex; mirrors MovieHighlight.IsFavorite/FavoritedAt.</summary>
    public bool IsFavorite { get; set; }

    public DateTime? FavoritedAt { get; set; }

    public ICollection<ApexTag> ApexTags { get; } = new List<ApexTag>();
    public ICollection<ApexActor> ApexActors { get; } = new List<ApexActor>();
}
