using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>A raw tag value banned from the catalog — stripped during ingestion/normalization
/// instead of becoming (or remaining) a canonical Tag.</summary>
public class IgnoredTag
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Value { get; set; } = string.Empty;

    public TagMatchMode MatchMode { get; set; } = TagMatchMode.CaseInsensitive;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
