namespace Javbuddy.Models;

/// <summary>An actor appearing in one scene. Only actors in the movie's cast can be
/// assigned: the (MovieId, ActorId) foreign key points at the movie's MovieActor link and cascades,
/// so removing an actor from the movie's cast — by any path, including the MetaActresses re-sync —
/// removes them from its scenes too. MovieId must equal Scene.MovieId (set by MovieSceneService).</summary>
public class SceneActor
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;
}
