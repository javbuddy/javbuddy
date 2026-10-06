using System.Net;
using System.Text.RegularExpressions;

namespace Javbuddy.Services.MovieDiscovery.Sources;

/// <summary>Parses the "released" (<c>/works/list/release</c>) and "reserved"/pre-order
/// (<c>/works/list/reserve</c>) listing pages of any studio site built on the up-timely.com site
/// template/vendor. s1s1s1.com, moodyz.com, kawaiikawaii.jp, and ideapocket.com all share this
/// exact markup, down to the CDN host (<c>cdn.up-timely.com</c>) — verified against each live
/// site's real markup — so one parser handles all of them; only
/// the domain baked into card/actress links and the studio's display name vary per site, supplied
/// via the constructor instead of one parser subclass per studio.</summary>
public partial class UpTimelyHtmlParser
{
    private readonly string studio;
    private readonly Regex cardRegex;
    private readonly Regex actressLinkRegex;
    private readonly Regex performerLinkRegex;

    public UpTimelyHtmlParser(string baseUrl, string studio)
    {
        this.studio = studio;
        var escapedBaseUrl = Regex.Escape(baseUrl);

        // Shared card markup between both listing pages — identical on every site apart from the
        // domain in the detail-page href.
        cardRegex = new Regex(
            $"""<div class="item">\s*<div class="c-card">.*?href="{escapedBaseUrl}/works/detail/(?<code>[A-Za-z0-9]+)".*?data-src="(?<cover>[^"]+)".*?<p class="text">(?<title>[^<]*)</p>""",
            RegexOptions.Singleline | RegexOptions.Compiled);

        actressLinkRegex = new Regex(
            $"""href="{escapedBaseUrl}/actress/detail/\d+"[^>]*>(?<name>[^<]+)</a>""",
            RegexOptions.Compiled);

        performerLinkRegex = new Regex(
            $"""href="{escapedBaseUrl}/(?:actress|actor)/detail/\d+"[^>]*>(?<name>[^<]+)</a>""",
            RegexOptions.Compiled);
    }

    // Same per-date header format on every site, e.g. "<p>2026年9月15日発売</p>".
    [GeneratedRegex(@"<p>(?<y>\d{4})年(?<m>\d{1,2})月(?<d>\d{1,2})日発売</p>")]
    private static partial Regex ReleaseDateHeaderRegex();

    // The detail page's own image gallery is a Swiper carousel, identical markup on every site.
    [GeneratedRegex(""""<div class="swiper-slide">\s*<img class="swiper-lazy" data-src="(?<image>[^"]+)"""")]
    private static partial Regex GalleryImageRegex();

    // The detail page's "女優" (actress) field, identical structure on every site.
    [GeneratedRegex(
        """<div class="th">女優</div>\s*<div class="td">(?<body>.*?)(?:(?:</div>\s*)*(?:<div class="item">\s*)?<div class="th">|</div>\s*</div>\s*</div>|$)""",
        RegexOptions.Singleline)]
    private static partial Regex ActressSectionRegex();

    // The detail page's "男優" (male actor) field when present.
    [GeneratedRegex(
        """<div class="th">男優</div>\s*<div class="td">(?<body>.*?)(?:(?:</div>\s*)*(?:<div class="item">\s*)?<div class="th">|</div>\s*</div>\s*</div>|$)""",
        RegexOptions.Singleline)]
    private static partial Regex ActorSectionRegex();

    [GeneratedRegex(@"<a [^>]*>(?<name>[^<]+)</a>")]
    private static partial Regex TagLinkRegex();

    // Every site's own "品番" (product code) field and detail-page URL slug have no hyphen (e.g.
    // "SIVR501") — normalized here to match the hyphenated convention javinizer-go/DMM use (e.g.
    // "SIVR-501") and that the rest of Javbuddy's Movie.Code values follow.
    [GeneratedRegex(@"^(?<prefix>[A-Za-z]+)(?<num>\d+)$")]
    private static partial Regex CodeFormatRegex();

    public IReadOnlyList<DiscoveredMovieItem> ParseReleaseListing(string html)
    {
        var headers = ReleaseDateHeaderRegex().Matches(html)
            .Select(m => (
                m.Index,
                Date: new DateTime(
                    int.Parse(m.Groups["y"].Value),
                    int.Parse(m.Groups["m"].Value),
                    int.Parse(m.Groups["d"].Value))))
            .ToList();

        var items = new List<DiscoveredMovieItem>();
        foreach (Match card in cardRegex.Matches(html))
        {
            var releaseDate = headers
                .Where(h => h.Index <= card.Index)
                .Select(h => (DateTime?)h.Date)
                .LastOrDefault();
            items.Add(BuildItem(card, releaseDate, isUpcoming: false));
        }
        return items;
    }

    public IReadOnlyList<DiscoveredMovieItem> ParseReserveListing(string html) =>
        // Only one release-window header for the whole page, not per item, so exact per-item
        // release dates aren't available here.
        cardRegex.Matches(html)
            .Select(card => BuildItem(card, releaseDate: null, isUpcoming: true))
            .ToList();

    private DiscoveredMovieItem BuildItem(Match card, DateTime? releaseDate, bool isUpcoming)
    {
        var code = NormalizeCode(card.Groups["code"].Value);
        var title = WebUtility.HtmlDecode(card.Groups["title"].Value.Trim());
        var cover = card.Groups["cover"].Value;
        return new DiscoveredMovieItem(code, title, studio, cover, [], [], releaseDate, isUpcoming);
    }

    public IReadOnlyList<string> ParseGalleryImageUrls(string html) =>
        GalleryImageRegex().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["image"].Value))
            .Distinct(StringComparer.Ordinal)
            .Take(40)
            .ToList();

    public IReadOnlyList<string> ParseActressNames(string html)
    {
        var section = ActressSectionRegex().Match(html);
        if (!section.Success) return [];

        var maleActorNames = ParseMaleActorNames(html).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return actressLinkRegex.Matches(section.Groups["body"].Value)
            .Select(match => WebUtility.HtmlDecode(match.Groups["name"].Value.Trim()))
            .Where(name => !maleActorNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> ParseMaleActorNames(string html)
    {
        var section = ActorSectionRegex().Match(html);
        if (!section.Success) return [];

        var body = section.Groups["body"].Value;
        var links = performerLinkRegex.Matches(body);
        if (links.Count > 0)
        {
            return links
                .Select(match => WebUtility.HtmlDecode(match.Groups["name"].Value.Trim()))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return TagLinkRegex().Matches(body)
            .Select(match => WebUtility.HtmlDecode(match.Groups["name"].Value.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string NormalizeCode(string rawCode)
    {
        var match = CodeFormatRegex().Match(rawCode);
        return match.Success ? $"{match.Groups["prefix"].Value}-{match.Groups["num"].Value}" : rawCode;
    }
}
