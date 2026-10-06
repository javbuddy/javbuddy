namespace Javbuddy.Models;

/// <summary>One of an apex's effective actors: its own, else its parent highlight's, else its
/// scene's, else the cast. Stored like SceneEffectiveActor, by ClipActorSync only.</summary>
public class ApexEffectiveActor
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
