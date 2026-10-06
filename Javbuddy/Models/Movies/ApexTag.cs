namespace Javbuddy.Models;

/// <summary>A library tag on one apex. It rolls up, computed, to the highlights and the scene
/// holding the apex and, as a clip tag (MovieTag.FromClips), to the movie.</summary>
public class ApexTag
{
    public int ApexId { get; set; }
    public MovieApex Apex { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
