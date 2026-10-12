using System.Net;
using System.Text.RegularExpressions;

namespace Javbuddy.Services.Common;

/// <summary>Text helpers shared by the HTML-scraping parsers (Warashi, MinnanoAv).</summary>
public static partial class HtmlTextHelper
{
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Strips tags, decodes entities and collapses whitespace runs to single spaces.</summary>
    public static string CleanText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var stripped = StripHtmlRegex().Replace(raw, " ");
        var decoded = WebUtility.HtmlDecode(stripped);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    /// <summary>Returns absolute URLs unchanged and joins relative ones onto <paramref name="baseUrl"/>.</summary>
    public static string? ResolveUrl(string? relativeOrAbsolute, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return null;
        if (relativeOrAbsolute.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativeOrAbsolute.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return relativeOrAbsolute;
        }

        return $"{baseUrl.TrimEnd('/')}/{relativeOrAbsolute.TrimStart('/')}";
    }
}
