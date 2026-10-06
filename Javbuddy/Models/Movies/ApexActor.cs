namespace Javbuddy.Models;

/// <summary>An actor hitting one apex. Like SceneActor, only the movie's cast can be
/// assigned: the (MovieId, ActorId) foreign key points at the movie's MovieActor link and cascades, so
/// leaving the cast leaves its apexes too. MovieId must equal MovieApex.MovieId (set by MovieApexService). An
/// apex without any inherits its actors, computed (ClipActors).</summary>
public class ApexActor
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
