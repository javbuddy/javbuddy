namespace Javbuddy.Models;

/// <summary>A tag on one actor within one scene. The actor is one of the movie's cast (the (MovieId, ActorId)
/// foreign key points at the MovieActor link and cascades), not necessarily one of the scene's own SceneActors:
/// a scene without its own actors inherits the cast. MovieId must equal Scene.MovieId (set by the service).</summary>
public class SceneActorTag
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
