namespace Javbuddy.Services.Images;

/// <summary>
/// Validates external actor image URLs and filters out defunct or inaccessible domains.
/// </summary>
public static class ActorImageUrlHelper
{
    /// <summary>
    /// Checks whether a URL belongs to a known defunct image hosting domain (e.g. r18.com).
    /// </summary>
    public static bool IsDefunctUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            if (host.Equals("r18.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".r18.com", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (trimmed.Contains("r18.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the URL is a valid absolute HTTP or HTTPS URL and not pointing to a defunct domain.
    /// </summary>
    public static bool IsUsableRemoteActorImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        return !IsDefunctUrl(trimmed);
    }
}
