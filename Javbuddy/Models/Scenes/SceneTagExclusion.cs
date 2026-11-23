namespace Javbuddy.Models;

/// <summary>A tag the scene opts out of inheriting (ClipTagInheritance): its parents' "propagate down" tags skip
/// it. Only that one scene; a clip inside it can still inherit the tag. Dormant while nothing would pass the tag
/// down. MovieId is the scene's, for marking the movie's stored inherited tags stale.</summary>
public class SceneTagExclusion
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int MovieId { get; set; }

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
