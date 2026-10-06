using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>A standout moment inside a movie. Unlike a Scene it doesn't partition the
/// movie: highlights may overlap each other and any scene, and always have an explicit end. Never
/// written to the file as a chapter.</summary>
public class MovieHighlight
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public double StartSeconds { get; set; }

    public double EndSeconds { get; set; }

    /// <summary>Optional; an empty title is displayed as the actor and tag names, or "Highlight N"
    /// (N = position by start time) when it has neither.</summary>
    [StringLength(200)]
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mirrors Scene.IsFavorite/FavoritedAt.</summary>
    public bool IsFavorite { get; set; }

    public DateTime? FavoritedAt { get; set; }

    public ICollection<HighlightTag> HighlightTags { get; } = new List<HighlightTag>();
    public ICollection<HighlightActor> HighlightActors { get; } = new List<HighlightActor>();
}
