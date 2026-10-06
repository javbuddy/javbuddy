using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>A path-prefix rewrite rule translating qBittorrent's filesystem view of a download
/// into javinizer-go's own filesystem view of the same file — the two may run on different
/// hosts/containers with different mounts for what is physically the same download (see the
/// torrent sorting wizard, TorrentSort.razor).</summary>
public class PathMapping
{
    public int Id { get; set; }

    [Required]
    [StringLength(1000)]
    public string QBittorrentPrefix { get; set; } = "";

    [Required]
    [StringLength(1000)]
    public string JavinizerPrefix { get; set; } = "";

    /// <summary>The same folder as Javbuddy's own process sees it (e.g. for the VR part-merge
    /// step, which needs to open the file directly rather than hand a path to javinizer-go).
    /// Optional — null when only the qBittorrent/javinizer-go translation has been configured.</summary>
    [StringLength(1000)]
    public string? AppPrefix { get; set; }
}
