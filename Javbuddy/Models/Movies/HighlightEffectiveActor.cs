namespace Javbuddy.Models;

/// <summary>One of a highlight's effective actors: its own, else those of the scene it starts in,
/// else the cast. Stored like SceneEffectiveActor, by ClipActorSync only.</summary>
public class HighlightEffectiveActor
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
