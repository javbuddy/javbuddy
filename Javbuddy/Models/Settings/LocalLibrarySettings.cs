using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Single-row settings for the local file-based metadata source (Stage 7): root
/// directories containing one folder per movie, named after its release code.</summary>
public class LocalLibrarySettings
{
    public int Id { get; set; }

    /// <summary>Comma-separated root directories to search, e.g. "D:\media\movies\jav,D:\media\movies\vr".</summary>
    [StringLength(2000)]
    public string? RootPaths { get; set; }
}
