namespace Javbuddy.Models;

/// <summary>Single-row settings for the artwork generated automatically for a movie a library rescan imports.
/// Each field can be overridden by a <c>NewMovieArtwork__&lt;Field&gt;</c> environment variable (see
/// NewMovieArtworkEnvConfig).</summary>
public class NewMovieArtworkSettings
{
    public int Id { get; set; }

    /// <summary>Generate extrafanart stills from the video for an imported movie that has none.</summary>
    public bool GenerateExtraFanart { get; set; } = true;

    /// <summary>Generate poster and fanart images from the video for an imported movie that lacks them.</summary>
    public bool GeneratePosterAndFanart { get; set; } = true;
}
