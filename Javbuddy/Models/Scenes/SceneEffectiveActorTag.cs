namespace Javbuddy.Models;

/// <summary>One actor tag a scene has for an actor: its own or inherited one, or (IsRolledUp) one only a
/// highlight or apex inside it carries. Stored like SceneEffectiveActor, by ClipActorSync only; the wall filters on it.</summary>
public class SceneEffectiveActorTag
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;

    public bool IsRolledUp { get; set; }
}
