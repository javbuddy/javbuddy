namespace Javbuddy.Services.DeoVr;

/// <summary>The /deovr routes and the absolute URLs the JSON points DeoVR at, all on
/// the address DeoVR's own request came in on (baseUrl, without a trailing slash).</summary>
public static class DeoVrUrls
{
    public const string Root = "/deovr";

    /// <summary>The address a request came in on. A reverse proxy that terminates HTTPS forwards
    /// plain http, so its X-Forwarded-Proto (the first value, when it's http or https) wins over
    /// the request's own scheme; otherwise DeoVR would be sent http:// links it may refuse.</summary>
    public static string BaseUrl(string scheme, string host, string pathBase, string? forwardedProto)
    {
        var forwarded = forwardedProto?.Split(',')[0].Trim();
        if (string.Equals(forwarded, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || string.Equals(forwarded, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            scheme = forwarded!.ToLowerInvariant();
        }
        return $"{scheme}://{host}{pathBase}";
    }

    public static string Video(string baseUrl, int movieId) => $"{baseUrl}{Root}/movies/{movieId}";

    public static string Thumbnail(string baseUrl, int movieId) => $"{baseUrl}{Root}/thumb/{movieId}";

    /// <summary>The timeline mosaic of the movie's trickplay set with this identity. The
    /// identity is in the path so a replaced set gets a new URL, rather than DeoVR showing a cached
    /// mosaic of the old file.</summary>
    public static string Timeline(string baseUrl, int movieId, string identity) =>
        $"{baseUrl}{Root}/timeline/{movieId}/{identity}/{DeoVrTimeline.FileName}";

    /// <summary>One version's stream; the file name ends the URL so a player sees its extension.</summary>
    public static string Stream(string baseUrl, int movieId, int fileId, string fileName) =>
        $"{baseUrl}{Root}/stream/{movieId}/{fileId}/{Uri.EscapeDataString(fileName)}";
}
