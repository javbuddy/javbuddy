using System.ComponentModel.DataAnnotations;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Models;

/// <summary>Single-row settings for the optional challenge solver (FlareSolverr, or a compatible
/// service such as Trawl) that Cloudflare-protected scrape sources can be routed through. It is only
/// asked when a site serves a challenge, and not by a source that switched it off with its own
/// <c>UseChallengeSolver</c> flag.</summary>
public class FlareSolverrSettings : IHasConnectionUrls
{
    public const int DefaultMaxTimeoutMs = 60000;

    public int Id { get; set; }

    /// <summary>Address of the solver's FlareSolverr-compatible API, e.g. http://flaresolverr:8191.</summary>
    [StringLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>How long the solver may spend on one request (solving the challenge included).</summary>
    [Range(5000, 300000)]
    public int MaxTimeoutMs { get; set; } = DefaultMaxTimeoutMs;

    // The solver is an internal service with no browser-facing page.
    string? IHasConnectionUrls.ExternalUrl => null;
}
