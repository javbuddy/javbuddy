namespace Javbuddy.Models;

/// <summary>A tag the apex opts out of inheriting (ClipTagInheritance): its parents' "propagate down" tags skip
/// it. Only that one apex; a clip inside it can still inherit the tag. Dormant while nothing would pass the tag
/// down. MovieId is the apex's, for marking the movie's stored inherited tags stale.</summary>
public class ApexTagExclusion
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
