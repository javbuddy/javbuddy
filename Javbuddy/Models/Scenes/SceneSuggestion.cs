namespace Javbuddy.Models;

/// <summary>What detected a suggested scene boundary.</summary>
public enum SceneSuggestionSource
{
    /// <summary>The end of a black stretch (a fade/cut to black between scenes).</summary>
    Black,

    /// <summary>A hard cut (scene-change score).</summary>
    Cut,
}

/// <summary>A scene boundary proposed by FFmpeg signal detection. Accepting one creates
/// a scene and removes the suggestion; dismissing one keeps it (Dismissed) so a later detection run
/// doesn't suggest the same point again.</summary>
public class SceneSuggestion
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public double Seconds { get; set; }

    public SceneSuggestionSource Source { get; set; }

    /// <summary>Black: the black stretch's length in seconds; Cut: ffmpeg scdet's score (0–100).</summary>
    public double Score { get; set; }

    public bool Dismissed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
