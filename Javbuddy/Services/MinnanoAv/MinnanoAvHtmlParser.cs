using System.Net;
using System.Text.RegularExpressions;
using Javbuddy.Services.Actors;

namespace Javbuddy.Services.MinnanoAv;

public static partial class MinnanoAvHtmlParser
{
    private const string DefaultBaseUrl = "https://www.minnano-av.com";

    [GeneratedRegex(
        @"<td><a href=""(?<path>actress\d+\.html)[^""]*""><img src=""(?<img>[^""]+)""[^>]*/?></a></td>\s*<td class=""details"">(?<content>.*?)</td>\s*<td>",
        RegexOptions.Singleline)]
    private static partial Regex SearchResultRowRegex();

    [GeneratedRegex(@"<h2 class=""ttl""><a[^>]*>(?<name>.*?)</a></h2>", RegexOptions.Singleline)]
    private static partial Regex SearchResultNameRegex();

    [GeneratedRegex(@"<p class=""furi"">(?<line>[^<]*)</p>")]
    private static partial Regex FuriLineRegex();

    // Detail page header, e.g. <td><h2>通野未帆 （とおのみほ / Tohno Miho）</h2></td>
    [GeneratedRegex(@"<td><h2>(?<kanji>[^<(（]+?)\s*[（(](?<kana>[^\s/)）]+)\s*/\s*(?<romaji>[^)）]+)[)）]</h2></td>")]
    private static partial Regex DetailHeaderRegex();

    [GeneratedRegex(@"<span>別名</span><p>(?<name>[^（(<]+)")]
    private static partial Regex AliasRegex();

    [GeneratedRegex(@"<span>生年月日</span><p>(?<y>\d{4})年(?<m>\d{1,2})月(?<d>\d{1,2})日")]
    private static partial Regex BirthDateRegex();

    [GeneratedRegex(@"<span>サイズ</span><p>(?<content>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex SizeLineRegex();

    [GeneratedRegex(@"T(?<height>\d+)")]
    private static partial Regex HeightRegex();

    [GeneratedRegex(@"B(?<bust>\d+)(?:\([^)]*?(?<cup>[A-Za-z])カップ[^)]*\))?")]
    private static partial Regex BustCupRegex();

    [GeneratedRegex(@"W(?<waist>\d+)")]
    private static partial Regex WaistRegex();

    [GeneratedRegex(@"H(?<hips>\d+)")]
    private static partial Regex HipsRegex();

    // The actress's own applied tags (distinct from the "AV女優 人気のタグ" sidebar widget, which
    // lists the site's globally popular tags — including "引退" — identically on every actress
    // page regardless of that actress's own status, and isn't a per-actress signal at all). A
    // "引退" chip inside THIS scoped block is the most reliable retirement signal on this site —
    // the AV出演期間 (active years) line doesn't always get an end year filled in even for a
    // retired performer.
    [GeneratedRegex(@"<span>タグ</span>\s*<div class=""tagarea"">(?<tags>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex ProfileTagsRegex();

    [GeneratedRegex(@"<meta property=""og:image"" content=""(?<img>[^""]+)""")]
    private static partial Regex OgImageRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    public static IReadOnlyList<MinnanoAvSearchResult> ParseSearchResults(string html, string baseUrl = DefaultBaseUrl)
    {
        var results = new List<MinnanoAvSearchResult>();
        var matches = SearchResultRowRegex().Matches(html);

        foreach (Match match in matches)
        {
            var path = match.Groups["path"].Value;
            var content = match.Groups["content"].Value;

            var nameMatch = SearchResultNameRegex().Match(content);
            if (!nameMatch.Success) continue;

            var name = CleanText(nameMatch.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name)) continue;

            string? kana = null;
            string? romaji = null;
            var aliases = new List<string>();

            foreach (Match furiMatch in FuriLineRegex().Matches(content))
            {
                var line = CleanText(furiMatch.Groups["line"].Value);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if ((line.StartsWith('（') || line.StartsWith('(')) && (line.EndsWith('）') || line.EndsWith(')')))
                {
                    var alias = line.Trim('（', '）', '(', ')').Trim();
                    if (!string.IsNullOrWhiteSpace(alias)) aliases.Add(alias);
                }
                else if (kana is null && line.Contains('/'))
                {
                    var parts = line.Split('/', 2, StringSplitOptions.TrimEntries);
                    kana = parts[0];
                    romaji = parts.Length > 1 ? parts[1] : null;
                }
            }

            var imgSrc = ResolveUrl(match.Groups["img"].Value, baseUrl);

            results.Add(new MinnanoAvSearchResult(
                Name: name,
                Kana: kana,
                Romaji: romaji,
                PathOrUrl: path,
                ImageUrl: imgSrc,
                DebutInfo: null,
                IsExactMatch: false,
                KnownAliases: aliases));
        }

        return results;
    }

    public static MinnanoAvPerformerDetail? ParsePerformerDetail(string html, string pathOrUrl, string baseUrl = DefaultBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var headerMatch = DetailHeaderRegex().Match(html);
        if (!headerMatch.Success) return null;

        var detail = new MinnanoAvPerformerDetail
        {
            PathOrUrl = pathOrUrl,
            Name = CleanText(headerMatch.Groups["kanji"].Value),
            Kana = CleanText(headerMatch.Groups["kana"].Value),
            Romaji = CleanText(headerMatch.Groups["romaji"].Value)
        };

        var sizeMatch = SizeLineRegex().Match(html);
        if (sizeMatch.Success)
        {
            var sizeContent = sizeMatch.Groups["content"].Value;

            var heightMatch = HeightRegex().Match(sizeContent);
            if (heightMatch.Success && int.TryParse(heightMatch.Groups["height"].Value, out var height))
            {
                detail.HeightCm = height;
            }

            var bustCupMatch = BustCupRegex().Match(sizeContent);
            if (bustCupMatch.Success)
            {
                if (int.TryParse(bustCupMatch.Groups["bust"].Value, out var bust)) detail.Bust = bust;
                if (bustCupMatch.Groups["cup"].Success) detail.CupSize = ActorPhysicalAttributesHelper.StandardCupSizeOrNull(bustCupMatch.Groups["cup"].Value);
            }

            var waistMatch = WaistRegex().Match(sizeContent);
            if (waistMatch.Success && int.TryParse(waistMatch.Groups["waist"].Value, out var waist))
            {
                detail.Waist = waist;
            }

            var hipsMatch = HipsRegex().Match(sizeContent);
            if (hipsMatch.Success && int.TryParse(hipsMatch.Groups["hips"].Value, out var hips))
            {
                detail.Hips = hips;
            }
        }

        var birthMatch = BirthDateRegex().Match(html);
        if (birthMatch.Success)
        {
            var year = int.Parse(birthMatch.Groups["y"].Value);
            var month = int.Parse(birthMatch.Groups["m"].Value);
            var day = int.Parse(birthMatch.Groups["d"].Value);
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31)
            {
                detail.BirthDate = new DateTime(year, month, day);
            }
        }

        var tagsMatch = ProfileTagsRegex().Match(html);
        detail.IsRetired = tagsMatch.Success && tagsMatch.Groups["tags"].Value.Contains("引退");

        foreach (Match aliasMatch in AliasRegex().Matches(html))
        {
            var alias = CleanText(aliasMatch.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(alias)) continue;
            if (string.Equals(alias, detail.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!detail.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                detail.Aliases.Add(alias);
            }
        }

        return detail;
    }

    /// <summary>Extracts the OGP thumbnail so an already-fetched exact-match detail page can
    /// still surface an image on its synthesized search result card.</summary>
    public static string? ParseOgImage(string html, string baseUrl = DefaultBaseUrl)
    {
        var match = OgImageRegex().Match(html);
        return match.Success ? ResolveUrl(match.Groups["img"].Value, baseUrl) : null;
    }

    private static string CleanText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var stripped = StripHtmlRegex().Replace(raw, " ");
        var decoded = WebUtility.HtmlDecode(stripped);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string? ResolveUrl(string? relativeOrAbsolute, string baseUrl)
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
