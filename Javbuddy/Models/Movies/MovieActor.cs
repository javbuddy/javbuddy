namespace Javbuddy.Models;

/// <summary>Indexed association between a tracked movie and a tracked actor.</summary>
public class MovieActor
{
    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public int ActorId { get; set; }
    public Actor Actor { get; set; } = null!;
}
