namespace Javbuddy.Models;

/// <summary>Single-row settings for trickplay (scrub-bar preview thumbnails). Each
/// field can be overridden by a <c>Trickplay__&lt;Field&gt;</c> environment variable (see
/// TrickplayEnvConfig).</summary>
public class TrickplaySettings
{
    public int Id { get; set; }

    /// <summary>Queue generation when a library refresh finds a new or changed main video file.</summary>
    public bool GenerateForNewFiles { get; set; } = true;

    /// <summary>Decode only keyframes: much faster, but a thumbnail can be off by up to the gap
    /// between keyframes.</summary>
    public bool KeyframeOnly { get; set; }

    /// <summary>Use the movie's Jellyfin trickplay when no local trickplay exists yet.</summary>
    public bool JellyfinFallback { get; set; } = true;
}
