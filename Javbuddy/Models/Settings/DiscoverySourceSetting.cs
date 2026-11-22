using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Whether one studio discovery source (Movies &gt; Discover) is scanned. Discovery is opt-in: a source
/// with no row is disabled until the user switches it on in Settings. Disabling only stops the scan; candidates already found stay listed.</summary>
public class DiscoverySourceSetting
{
    public int Id { get; set; }

    /// <summary><c>IStudioDiscoverySource.SourceName</c> (e.g. "S1"), unique.</summary>
    [Required, StringLength(100)]
    public string SourceName { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}
