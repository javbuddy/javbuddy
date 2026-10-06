namespace Javbuddy.Models;

/// <summary>A library tag on one highlight. It rolls up, computed, to the scenes the highlight
/// overlaps and, as a clip tag (MovieTag.FromClips), to the movie.</summary>
public class HighlightTag
{
    public int HighlightId { get; set; }
    public MovieHighlight Highlight { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
