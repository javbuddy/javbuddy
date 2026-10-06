namespace Javbuddy.Models;

/// <summary>A tag on one scene. It rolls up to the movie as a clip tag (MovieTag.FromClips,
///) for as long as a scene, highlight or apex of the movie carries it.</summary>
public class SceneTag
{
    public int SceneId { get; set; }
    public Scene Scene { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
