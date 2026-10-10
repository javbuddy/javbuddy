namespace Javbuddy.Models;

/// <summary>A tag on one actor within one highlight. Like SceneActorTag, the actor is one of the movie's cast.
/// MovieId must equal MovieHighlight.MovieId (set by the service).</summary>
public class HighlightActorTag
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
