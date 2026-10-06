using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

public sealed class DeletedMovie
{
    public int Id { get; set; }

    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [StringLength(50)]
    public string NormalizedCode { get; set; } = string.Empty;

    [StringLength(50)]
    public string CanonicalKey { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Title { get; set; }

    public string? MetaTitle { get; set; }
    public DateTime DeletedAt { get; set; }
    public MovieStatus PreviousStatus { get; set; }
}
