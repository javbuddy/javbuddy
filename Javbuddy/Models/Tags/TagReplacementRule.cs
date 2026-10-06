using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Normalizes a raw scraped/imported tag value onto a canonical Tag (e.g. "Solowork" →
/// "Solo"). Applied automatically by TagNormalization at ingestion time and retroactively via
/// the Tags page's "Apply Rules to Library" action.</summary>
public class TagReplacementRule
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string SourceValue { get; set; } = string.Empty;

    public TagMatchMode MatchMode { get; set; } = TagMatchMode.CaseInsensitive;

    public int TargetTagId { get; set; }
    public Tag TargetTag { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
