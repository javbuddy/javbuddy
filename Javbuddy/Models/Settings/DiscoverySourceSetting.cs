using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Whether one studio discovery source (Movies &gt; Discover) is scanned. A source with no row is
/// enabled, so a newly registered source works without being switched on first. Disabling only stops the
/// scan; candidates already found stay listed.</summary>
public class DiscoverySourceSetting
{
    public int Id { get; set; }

    /// <summary><c>IStudioDiscoverySource.SourceName</c> (e.g. "S1"), unique.</summary>
    [Required, StringLength(100)]
    public string SourceName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
