using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>A named time range inside a movie, similar to a chapter. A null
/// EndSeconds means the scene runs until the next scene's start, or the movie's end for the last
/// one — see SceneRanges.ResolveEffectiveRanges. Gaps between scenes are allowed, overlaps aren't
/// (enforced by MovieSceneService, since chapter export needs non-overlapping ranges).</summary>
public class Scene
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public double StartSeconds { get; set; }

    public double? EndSeconds { get; set; }

    /// <summary>Optional; an empty title is displayed as "Scene N" (N = position by start time).</summary>
    [StringLength(200)]
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Marked as a favorite scene; mirrors Movie.IsFavorite/FavoritedAt.</summary>
    public bool IsFavorite { get; set; }

    public DateTime? FavoritedAt { get; set; }

    /// <summary>Left out of the Scenes overview, e.g. an intro or outro; shown
    /// everywhere else.</summary>
    public bool IsHiddenFromOverview { get; set; }

    public ICollection<SceneActor> SceneActors { get; } = new List<SceneActor>();
    public ICollection<SceneTag> SceneTags { get; } = new List<SceneTag>();
}
