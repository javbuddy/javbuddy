namespace Javbuddy.Models;

/// <summary>One of a scene's effective actors: its own, else the cast — what ClipActors computes,
/// stored so the Scenes wall can filter on it. Derived data: ClipActorSync rewrites a movie's rows while
/// Movie.ClipActorsStale is set; nothing else writes them. Like SceneActor, (MovieId, ActorId) points at the
/// movie's cast link and cascades.</summary>
public class SceneEffectiveActor
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
