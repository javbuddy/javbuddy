namespace Javbuddy.Models;

/// <summary>An actor in one highlight. Like ApexActor, only the movie's cast can be
/// assigned: the (MovieId, ActorId) foreign key points at the movie's MovieActor link and cascades, so
/// leaving the cast leaves its highlights too. MovieId must equal MovieHighlight.MovieId (set by
/// MovieHighlightService). A highlight without any inherits its actors, computed (ClipActors).</summary>
public class HighlightActor
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
