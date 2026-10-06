using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Single-row settings for the minnano-av.com metadata source. Disabled by default —
/// when enabled, powers actor physical attribute enrichment (measurements, cup size, height,
/// birthdate, retirement status, and aliases) via minnano-av.com.</summary>
public class MinnanoAvSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Base URL for minnano-av.com (default: https://www.minnano-av.com).</summary>
    [StringLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>Throttling delay in milliseconds between requests when batch querying or scraping (default: 750).</summary>
    public int RequestDelayMs { get; set; } = 750;

    /// <summary>Whether enriching an actor should overwrite existing non-empty physical attributes.</summary>
    public bool OverwriteExisting { get; set; }

    public DateTime? LastEnrichedAt { get; set; }

    [StringLength(300)]
    public string? LastEnrichSummary { get; set; }
}
