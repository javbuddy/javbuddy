namespace Javbuddy.Models;

/// <summary>One actor tag an apex has for an actor, own or inherited. Stored like SceneEffectiveActorTag, by
/// ClipActorSync only. Nothing rolls up into an apex.</summary>
public class ApexEffectiveActorTag
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
