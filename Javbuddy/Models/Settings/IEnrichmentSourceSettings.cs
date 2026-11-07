namespace Javbuddy.Models;

/// <summary>The settings shape shared by the scraped actor-metadata sources (minnano-av.com):
/// an on/off switch, the site's base URL and request throttle, whether enrichment overwrites
/// existing values, and the last batch run's outcome.</summary>
public interface IEnrichmentSourceSettings
{
    int Id { get; set; }

    bool Enabled { get; set; }

    string? BaseUrl { get; set; }

    int RequestDelayMs { get; set; }

    bool OverwriteExisting { get; set; }

    /// <summary>Whether a Cloudflare challenge on this source may be solved through the configured
    /// challenge solver (FlareSolverr/Trawl); off means a challenge is reported as blocked instead.</summary>
    bool UseChallengeSolver { get; set; }

    DateTime? LastEnrichedAt { get; set; }

    string? LastEnrichSummary { get; set; }
}
