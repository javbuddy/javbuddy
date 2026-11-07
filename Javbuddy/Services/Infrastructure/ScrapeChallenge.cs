using System.Net;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Thrown when a scrape source is behind a bot challenge (Cloudflare) that could not be passed,
/// so callers can say so instead of reporting an empty result. The message is user-facing.</summary>
public sealed class ScrapeBlockedException(string message) : Exception(message);

/// <summary>Recognizes a Cloudflare-style bot challenge in a response.</summary>
public static class ScrapeChallenge
{
    private static readonly string[] BodyMarkers = ["<title>Just a moment...</title>", "challenge-platform", "cf-chl"];

    /// <summary>Whether a response is a bot challenge rather than the page: Cloudflare's own
    /// <c>cf-mitigated: challenge</c> header, the interstitial's markers in the body (it is served with a 403,
    /// 503 or even 200), or a 403/503 from a Cloudflare-fronted site (<paramref name="viaCloudflare"/>). A bare
    /// 403/503 from anywhere else is an ordinary error, not a challenge.</summary>
    public static bool IsChallenge(HttpStatusCode status, bool viaCloudflare, bool cfMitigatedChallenge, string? body)
    {
        if (cfMitigatedChallenge) return true;

        if (body is not null && BodyMarkers.Any(m => body.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return viaCloudflare && status is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable;
    }

    public static bool IsChallenge(HttpResponseMessage response, string? body) =>
        IsChallenge(
            response.StatusCode,
            response.Headers.Contains("cf-ray")
                || response.Headers.Server.Any(s => string.Equals(s.Product?.Name, "cloudflare", StringComparison.OrdinalIgnoreCase)),
            response.Headers.TryGetValues("cf-mitigated", out var values) && values.Any(v => string.Equals(v, "challenge", StringComparison.OrdinalIgnoreCase)),
            body);
}

/// <summary>The environment override a scraped source offers for using the challenge solver
/// (<c>MinnanoAv__UseChallengeSolver</c>).</summary>
public static class ScrapeSourceEnvConfig
{
    public static bool? GetUseChallengeSolver(IConfiguration? configuration, string section) =>
        configuration is null ? null : EnvConfig.GetBool(configuration, $"{section}:UseChallengeSolver");
}
