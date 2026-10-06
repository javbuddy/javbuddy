using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>A DeoVR list: the movies matching a Movies-grid filter, in its sort
/// order. A fresh database starts with one, "All movies" (no filter, newest first), which can be
/// edited or deleted like any other.</summary>
public class DeoVrGroup
{
    /// <summary>The group a fresh database starts with (seeded by AppDbContext).</summary>
    public static DeoVrGroup AllMovies => new() { Id = 1, Name = "All movies", Position = 0, FilterJson = "{}", SortField = "added", SortDescending = true };

    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Order among the groups, ascending.</summary>
    public int Position { get; set; }

    /// <summary>The group's MovieGridFilter as JSON (see DeoVrGroupFilter).</summary>
    [Required]
    public string FilterJson { get; set; } = "{}";

    [Required]
    [StringLength(20)]
    public string SortField { get; set; } = "added";

    public bool SortDescending { get; set; } = true;
}
