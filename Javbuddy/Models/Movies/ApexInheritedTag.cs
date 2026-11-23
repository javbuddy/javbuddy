namespace Javbuddy.Models;

/// <summary>A tag the apex inherits from a parent that passes it down (MovieTag/SceneTag/HighlightTag
/// PropagatesDown), less its exclusions. Stored like the effective actors, by ClipActorSync only, for the Scenes
/// wall's filters; ClipTagInheritance is the definition.</summary>
public class ApexInheritedTag
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
