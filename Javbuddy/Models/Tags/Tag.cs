using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Canonical tag/genre catalog entry — the authoritative record a movie's raw scraped
/// or imported genre strings resolve onto (see TagNormalization). Genres and tags are a single
/// unified taxonomy in Javbuddy; there is no separate genre-only concept.</summary>
public class Tag
{
    public const string HierarchyDelimiter = "##";

    public int Id { get; set; }

    public int? ParentTagId { get; set; }

    public Tag? ParentTag { get; set; }

    public ICollection<Tag> Subtags { get; } = new List<Tag>();

    public ICollection<MovieTag> MovieTags { get; } = new List<MovieTag>();

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>True for a tag TagNormalization created automatically (ingestion or library
    /// discovery) that hasn't been looked at yet — surfaced in the Tags page's discovery review
    /// list. Cleared by explicitly approving, renaming, or merging the tag.</summary>
    public bool NeedsReview { get; set; }

    /// <summary>True for a tag that applies to one actor within a movie, scene, highlight or apex
    /// ("blonde", ActorTagService), not to the whole movie. Such a tag is only added through the actor-tag
    /// editors, never as a plain movie or clip tag, and has no parent or subtags. Its plain MovieTag rows are
    /// always FromClips (ClipTagSync), so they reach MetaGenres and the .nfo.</summary>
    public bool IsActorTag { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
