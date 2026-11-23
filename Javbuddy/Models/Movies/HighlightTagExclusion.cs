namespace Javbuddy.Models;

/// <summary>A tag the highlight opts out of inheriting (ClipTagInheritance): its parents' "propagate down" tags skip
/// it. Only that one highlight; a clip inside it can still inherit the tag. Dormant while nothing would pass the tag
/// down. MovieId is the highlight's, for marking the movie's stored inherited tags stale.</summary>
public class HighlightTagExclusion
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int MovieId { get; set; }

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
