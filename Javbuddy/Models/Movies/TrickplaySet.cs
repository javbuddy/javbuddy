namespace Javbuddy.Models;

/// <summary>One generated trickplay set, whose tile sheets are in the durable
/// object store under <c>trickplay/&lt;CodeFolder&gt;/&lt;Identity&gt;/0.webp, 1.webp, …</c>. The row is
/// both the index and the commit marker: it's saved only after every sheet is written, so sheets
/// without a row are an interrupted generation (or left over from a lost database) and get
/// cleaned up. Keyed by code rather than movie so a set survives the movie being removed and
/// re-added.</summary>
public class TrickplaySet
{
    public int Id { get; set; }

    /// <summary>The movie code made safe as a key segment (TrickplayStore.CodeFolder).</summary>
    public string CodeFolder { get; set; } = "";

    /// <summary>The video file it was generated from (TrickplayIdentity).</summary>
    public string Identity { get; set; } = "";

    public int Width { get; set; }

    public int Height { get; set; }

    public int TileWidth { get; set; }

    public int TileHeight { get; set; }

    public int ThumbnailCount { get; set; }

    public int IntervalMs { get; set; }

    public double DurationSeconds { get; set; }

    public bool LeftEyeOnly { get; set; }

    public bool KeyframeOnly { get; set; }

    public DateTime GeneratedAt { get; set; }

    public string FileName { get; set; } = "";

    /// <summary>Set for a highlight's own denser set: the identity of the video file
    /// it was cut from, whose set is keyed by TrickplayIdentity.ForClip instead. Null for a whole-file
    /// set.</summary>
    public string? SourceIdentity { get; set; }

    /// <summary>Where in the video the first thumbnail is: the clip's start for a highlight's set,
    /// else 0.</summary>
    public double StartSeconds { get; set; }
}
