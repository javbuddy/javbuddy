namespace Javbuddy.Models;

/// <summary>One actor tag a highlight has for an actor: its own or inherited one, or (IsRolledUp) one only an
/// apex inside it carries. Stored like SceneEffectiveActorTag, by ClipActorSync only.</summary>
public class HighlightEffectiveActorTag
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;

    public bool IsRolledUp { get; set; }
}
