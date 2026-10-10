namespace Javbuddy.Models;

/// <summary>A tag on one actor within one apex. Like SceneActorTag, the actor is one of the movie's cast.
/// MovieId must equal MovieApex.MovieId (set by the service).</summary>
public class ApexActorTag
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
