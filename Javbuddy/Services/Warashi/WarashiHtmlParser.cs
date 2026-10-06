using System.Net;
using System.Text.RegularExpressions;
using Javbuddy.Services.Actors;

namespace Javbuddy.Services.Warashi;

public static partial class WarashiHtmlParser
{
    private const string DefaultBaseUrl = "https://warashi-asian-pornstars.fr";

    [GeneratedRegex(@"<div class=""resultat-pornostar(?:\s+(?<exact>correspondance_exacte))?"">(?<content>.*?)</div>(?=\s*(?:<div class=""separateur-resultats""|<div class=""resultat-pornostar""|</div>|$))", RegexOptions.Singleline)]
    private static partial Regex SearchResultBlockRegex();

    [GeneratedRegex(@"<img[^>]+src=""(?<src>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSrcRegex();

    [GeneratedRegex(@"<a[^>]+href=""(?<path>/en/s-(?:2-0|4-1)/[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SearchPerformerAnchorRegex();

    [GeneratedRegex(@"Porn/AV activity:\s*(?<activity>[^<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CareerActivityRegex();

    [GeneratedRegex(@"<p>AKA:\s*(?<aka>.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AkaBlockRegex();

    [GeneratedRegex(@"<meta itemprop=""givenName"" content=""(?<given>[^""]*)""\s*/>", RegexOptions.IgnoreCase)]
    private static partial Regex GivenNameMetaRegex();

    [GeneratedRegex(@"<meta itemprop=""familyName"" content=""(?<family>[^""]*)""\s*/>", RegexOptions.IgnoreCase)]
    private static partial Regex FamilyNameMetaRegex();

    [GeneratedRegex(@"<span itemprop=""name"">(?<name>[^<]+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex PerformerNameSpanRegex();

    [GeneratedRegex(@"<span itemprop=""additionalName"">(?<ja>[^<]+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex AdditionalNameSpanRegex();

    [GeneratedRegex(@"<time itemprop=""birthDate"" content=""(?<date>\d{4}-\d{2}-\d{2})""", RegexOptions.IgnoreCase)]
    private static partial Regex BirthDateRegex();

    [GeneratedRegex(@"height:\s*<span itemprop=""value"">(?<height>\d+)</span>\s*cm", RegexOptions.IgnoreCase)]
    private static partial Regex HeightRegex();

    [GeneratedRegex(@"cup size:\s*(?<cup>[A-Za-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CupSizeRegex();

    [GeneratedRegex(@"measurements:\s*JP\s*(?<bust>\d{2,3})\s*-\s*(?<waist>\d{2,3})\s*-\s*(?<hips>\d{2,3})", RegexOptions.IgnoreCase)]
    private static partial Regex JpMeasurementsRegex();

    [GeneratedRegex(@"blood type:\s*(?<blood>[A-Za-z0-9\+\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BloodTypeRegex();

    [GeneratedRegex(@"<span itemprop=""addressCountry"">(?<country>[^<]+)</span>(?:,\s*<span itemprop=""addressRegion"">(?<region>[^<]+)</span>)?(?:,\s*<span itemprop=""addressLocality"">(?<locality>[^<]+)</span>)?", RegexOptions.IgnoreCase)]
    private static partial Regex BirthPlaceRegex();

    [GeneratedRegex(@"porn/AV activity:\s*(?<start>\d{4})\s*-\s*(?<end>still active|retired|\d{4})?", RegexOptions.IgnoreCase)]
    private static partial Regex DetailActivityRegex();

    [GeneratedRegex(@"<div id=""pornostar-profil-noms-alternatifs"">.*?<ul>(?<items>.*?)</ul>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AlternativeNamesBlockRegex();

    [GeneratedRegex(@"<li>(?<item>.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<div id=""pornostar-profil-photos-0"">.*?<img itemprop=""image""[^>]+src=""(?<src>[^""]+)""", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MainPhotoRegex();

    [GeneratedRegex(@"<div id=""pornostar-profil-photos-1"">.*?(?<links><figure>.*?</figure>)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AdditionalPhotosBlockRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    public static IReadOnlyList<WarashiSearchResult> ParseSearchResults(string html, string baseUrl = DefaultBaseUrl)
    {
        var results = new List<WarashiSearchResult>();
        var matches = SearchResultBlockRegex().Matches(html);

        foreach (Match match in matches)
        {
            var isExact = match.Groups["exact"].Success;
            var content = match.Groups["content"].Value;

            string? path = null;
            string? name = null;
            string? japaneseName = null;

            var anchorMatches = SearchPerformerAnchorRegex().Matches(content);
            foreach (Match am in anchorMatches)
            {
                path ??= am.Groups["path"].Value;
                var clean = CleanText(am.Groups["text"].Value);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    var parts = clean.Split('-', 2, StringSplitOptions.TrimEntries);
                    name = parts[0];
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        japaneseName = parts[1];
                    }
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) continue;

            var imgMatch = ImageSrcRegex().Match(content);
            string? imageUrl = imgMatch.Success ? ResolveUrl(imgMatch.Groups["src"].Value, baseUrl) : null;

            var activityMatch = CareerActivityRegex().Match(content);
            string? activity = activityMatch.Success ? CleanText(activityMatch.Groups["activity"].Value) : null;

            var aliases = new List<string>();
            var akaMatch = AkaBlockRegex().Match(content);
            if (akaMatch.Success)
            {
                var rawAka = CleanText(akaMatch.Groups["aka"].Value);
                foreach (var alias in rawAka.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var cleanAlias = CleanText(alias);
                    if (string.IsNullOrWhiteSpace(cleanAlias)) continue;

                    var parts = cleanAlias.Split('-', 2, StringSplitOptions.TrimEntries);
                    foreach (var part in parts)
                    {
                        var cleanPart = CleanText(part);
                        if (!string.IsNullOrWhiteSpace(cleanPart) && !aliases.Contains(cleanPart))
                        {
                            aliases.Add(cleanPart);
                        }
                    }
                }
            }

            results.Add(new WarashiSearchResult(
                Name: name,
                JapaneseName: japaneseName,
                PathOrUrl: path,
                ImageUrl: imageUrl,
                CareerActivity: activity,
                IsExactMatch: isExact,
                KnownAliases: aliases));
        }

        return results;
    }

    public static WarashiPerformerDetail? ParsePerformerDetail(string html, string pathOrUrl, string baseUrl = DefaultBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var detail = new WarashiPerformerDetail { PathOrUrl = pathOrUrl };

        // Name extraction
        var givenMatch = GivenNameMetaRegex().Match(html);
        var familyMatch = FamilyNameMetaRegex().Match(html);
        if (givenMatch.Success || familyMatch.Success)
        {
            detail.GivenName = givenMatch.Success ? CleanText(givenMatch.Groups["given"].Value) : null;
            detail.FamilyName = familyMatch.Success ? CleanText(familyMatch.Groups["family"].Value) : null;
            detail.Name = $"{detail.GivenName} {detail.FamilyName}".Trim();
        }

        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            var nameSpanMatch = PerformerNameSpanRegex().Match(html);
            if (nameSpanMatch.Success)
            {
                detail.Name = CleanText(nameSpanMatch.Groups["name"].Value);
            }
        }

        var jaSpanMatch = AdditionalNameSpanRegex().Match(html);
        if (jaSpanMatch.Success)
        {
            detail.JapaneseName = CleanText(jaSpanMatch.Groups["ja"].Value);
        }

        // Physical stats
        var heightMatch = HeightRegex().Match(html);
        if (heightMatch.Success && int.TryParse(heightMatch.Groups["height"].Value, out var height))
        {
            detail.HeightCm = height;
        }

        var cupMatch = CupSizeRegex().Match(html);
        if (cupMatch.Success)
        {
            detail.CupSize = ActorPhysicalAttributesHelper.StandardCupSizeOrNull(CleanText(cupMatch.Groups["cup"].Value));
        }

        var jpMeasurementsMatch = JpMeasurementsRegex().Match(html);
        if (jpMeasurementsMatch.Success
            && int.TryParse(jpMeasurementsMatch.Groups["bust"].Value, out var bust)
            && int.TryParse(jpMeasurementsMatch.Groups["waist"].Value, out var waist)
            && int.TryParse(jpMeasurementsMatch.Groups["hips"].Value, out var hips))
        {
            detail.Bust = bust;
            detail.Waist = waist;
            detail.Hips = hips;
        }

        var birthMatch = BirthDateRegex().Match(html);
        if (birthMatch.Success && DateTime.TryParse(birthMatch.Groups["date"].Value, out var birthDate))
        {
            detail.BirthDate = birthDate;
        }

        var bloodMatch = BloodTypeRegex().Match(html);
        if (bloodMatch.Success)
        {
            var blood = CleanText(bloodMatch.Groups["blood"].Value);
            if (!blood.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                detail.BloodType = blood;
            }
        }

        var placeMatch = BirthPlaceRegex().Match(html);
        if (placeMatch.Success)
        {
            var parts = new List<string>();
            if (placeMatch.Groups["country"].Success)
            {
                var country = CleanText(placeMatch.Groups["country"].Value);
                if (!string.IsNullOrWhiteSpace(country) && !country.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(country);
                }
            }
            if (placeMatch.Groups["region"].Success) parts.Add(CleanText(placeMatch.Groups["region"].Value));
            if (placeMatch.Groups["locality"].Success) parts.Add(CleanText(placeMatch.Groups["locality"].Value));
            if (parts.Count > 0) detail.BirthPlace = string.Join(", ", parts);
        }

        // Activity / Retirement
        var activityMatch = DetailActivityRegex().Match(html);
        if (activityMatch.Success)
        {
            if (int.TryParse(activityMatch.Groups["start"].Value, out var startYear))
            {
                detail.DebutYear = startYear;
            }

            var end = activityMatch.Groups["end"].Value.Trim();
            if (end.Equals("retired", StringComparison.OrdinalIgnoreCase))
            {
                detail.IsRetired = true;
            }
            else if (int.TryParse(end, out var endYear) && endYear < DateTime.UtcNow.Year)
            {
                detail.IsRetired = true;
            }
            else if (end.Contains("still active", StringComparison.OrdinalIgnoreCase))
            {
                detail.IsRetired = false;
            }
        }

        // Alternative names (Aliases)
        var altBlock = AlternativeNamesBlockRegex().Match(html);
        if (altBlock.Success)
        {
            var items = ListItemRegex().Matches(altBlock.Groups["items"].Value);
            foreach (Match item in items)
            {
                var raw = CleanText(item.Groups["item"].Value);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var parts = raw.Split('-', 2, StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    var cleanPart = CleanText(part);
                    if (!string.IsNullOrWhiteSpace(cleanPart)
                        && !cleanPart.Equals(detail.Name, StringComparison.OrdinalIgnoreCase)
                        && !cleanPart.Equals(detail.JapaneseName, StringComparison.OrdinalIgnoreCase)
                        && !detail.Aliases.Contains(cleanPart, StringComparer.OrdinalIgnoreCase))
                    {
                        detail.Aliases.Add(cleanPart);
                    }
                }
            }
        }

        // Photos
        var mainPhoto = MainPhotoRegex().Match(html);
        if (mainPhoto.Success)
        {
            detail.MainPhotoUrl = ResolveUrl(mainPhoto.Groups["src"].Value, baseUrl);
        }

        var addPhotosBlock = AdditionalPhotosBlockRegex().Match(html);
        if (addPhotosBlock.Success)
        {
            var photoMatches = ImageSrcRegex().Matches(addPhotosBlock.Value);
            foreach (Match pm in photoMatches)
            {
                var src = ResolveUrl(pm.Groups["src"].Value, baseUrl);
                if (src != null && !detail.AdditionalPhotoUrls.Contains(src))
                {
                    detail.AdditionalPhotoUrls.Add(src);
                }
            }
        }

        return detail;
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
