namespace Javbuddy.Models;

/// <summary>The settings shape shared by the scraped actor-metadata sources (Warashi, minnano-av.com):
/// an on/off switch, the site's base URL and request throttle, whether enrichment overwrites
/// existing values, and the last batch run's outcome.</summary>
public interface IEnrichmentSourceSettings
{
    int Id { get; set; }

    bool Enabled { get; set; }

    string? BaseUrl { get; set; }

    int RequestDelayMs { get; set; }

    bool OverwriteExisting { get; set; }

    DateTime? LastEnrichedAt { get; set; }

    string? LastEnrichSummary { get; set; }
}
