namespace Javbuddy.Models;

/// <summary>A tag on one actor within a movie ("blonde"), the movie-level end of the actor-scoped tags. The tag
/// must be an actor tag (Tag.IsActorTag). The (MovieId, ActorId) foreign key points at the movie's MovieActor
/// link and cascades, so leaving the cast drops the tags. Scenes, highlights and apexes inherit them per actor
/// (ClipActorTags).</summary>
public class MovieActorTag
{
    public int MovieId { get; set; }

    public int ActorId { get; set; }

    public MovieActor MovieActor { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
