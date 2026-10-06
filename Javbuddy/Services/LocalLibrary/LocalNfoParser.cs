using System.Globalization;
using System.Xml.Linq;
using Javbuddy.Services.Images;

namespace Javbuddy.Services.LocalLibrary;

/// <summary>Reads a Kodi/Jellyfin-style movie .nfo into <see cref="LocalMovieMetadata"/>.</summary>
public static class LocalNfoParser
{
    public static async Task<LocalMovieMetadata?> ParseAsync(string nfoPath, CancellationToken ct)
    {
        XDocument doc;
        try
        {
            // XDocument.Load(path) reads the whole file synchronously, blocking a thread-pool
            // thread for the full network round-trip on a share like D:\ — called once per movie,
            // that adds up across a few thousand movies enough to starve the thread pool and make
            // *unrelated* concurrent work (e.g. another circuit's SignalR keep-alive) time out.
            // useAsync: true on the FileStream plus XDocument.LoadAsync keeps the read off a
            // thread-pool thread instead.
            await using var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return null;
        }

        var root = doc.Root;
        if (root is null) return null;

        string? El(string name)
        {
            var value = root.Element(name)?.Value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        var genres = root.Elements("genre").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        var actors = new List<LocalActorMetadata>();
        foreach (var actorEl in root.Elements("actor"))
        {
            var name = actorEl.Element("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var role = actorEl.Element("role")?.Value?.Trim();
            var type = actorEl.Element("type")?.Value?.Trim();
            var thumb = actorEl.Elements("thumb").FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Value))?.Value?.Trim();
            if (ActorImageUrlHelper.IsDefunctUrl(thumb))
            {
                thumb = null;
            }

            var altName = actorEl.Element("altname")?.Value?.Trim()
                ?? actorEl.Element("japanesename")?.Value?.Trim()
                ?? actorEl.Element("japanese_name")?.Value?.Trim();

            var aliases = new List<string>();

            foreach (var altEl in actorEl.Elements("altname"))
            {
                var v = altEl.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(v) && !string.Equals(v, altName, StringComparison.OrdinalIgnoreCase) && !aliases.Contains(v, StringComparer.OrdinalIgnoreCase))
                {
                    aliases.Add(v);
                }
            }

            foreach (var aliasEl in actorEl.Elements("alias"))
            {
                var v = aliasEl.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(v) && !string.Equals(v, altName, StringComparison.OrdinalIgnoreCase) && !aliases.Contains(v, StringComparer.OrdinalIgnoreCase))
                {
                    aliases.Add(v);
                }
            }

            foreach (var aliasesEl in actorEl.Elements("aliases"))
            {
                var childAliases = aliasesEl.Elements("alias").ToList();
                if (childAliases.Count > 0)
                {
                    foreach (var ca in childAliases)
                    {
                        var v = ca.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(v) && !string.Equals(v, altName, StringComparison.OrdinalIgnoreCase) && !aliases.Contains(v, StringComparer.OrdinalIgnoreCase))
                        {
                            aliases.Add(v);
                        }
                    }
                }
                else
                {
                    var rawAliases = aliasesEl.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(rawAliases))
                    {
                        var parts = rawAliases.Split(new[] { ',', ';', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var p in parts)
                        {
                            if (!string.IsNullOrWhiteSpace(p) && !string.Equals(p, altName, StringComparison.OrdinalIgnoreCase) && !aliases.Contains(p, StringComparer.OrdinalIgnoreCase))
                            {
                                aliases.Add(p);
                            }
                        }
                    }
                }
            }

            actors.Add(new LocalActorMetadata
            {
                Name = name,
                Role = string.IsNullOrWhiteSpace(role) ? null : role,
                Type = string.IsNullOrWhiteSpace(type) ? null : type,
                Thumb = string.IsNullOrWhiteSpace(thumb) ? null : thumb,
                AltName = string.IsNullOrWhiteSpace(altName) ? null : altName,
                Aliases = aliases
            });
        }

        // If an altname is duplicated across different actors in the same movie,
        // it was duplicated by a scraper bug (e.g. copying the first actress's Japanese name
        // across all cast members in a multi-actress movie) and is unreliable.
        if (actors.Count > 1)
        {
            var duplicatedAltNames = actors
                .Where(a => !string.IsNullOrWhiteSpace(a.AltName))
                .GroupBy(a => a.AltName!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (duplicatedAltNames.Count > 0)
            {
                foreach (var a in actors)
                {
                    if (a.AltName is not null && duplicatedAltNames.Contains(a.AltName))
                    {
                        a.AltName = null;
                    }

                    a.Aliases.RemoveAll(alias => duplicatedAltNames.Contains(alias));
                }
            }
        }

        var actresses = actors.Select(a => a.Name).ToList();

        // Two .nfo dialects show up in the wild: javinizer-go's <ratings><rating><value> wrapper,
        // and a flatter <rating>/<votes> pair with no wrapper at all.
        double? rating = null;
        int? ratingVotes = null;
        var ratingValue = root.Element("ratings")?.Element("rating")?.Element("value")?.Value
            ?? root.Element("rating")?.Value;
        if (double.TryParse(ratingValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedRating))
        {
            rating = parsedRating;
        }
        if (int.TryParse(root.Element("votes")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVotes))
        {
            ratingVotes = parsedVotes;
        }

        // Same story for the poster: javinizer-go tags it with aspect="poster"; other tools just
        // emit a single bare <thumb> with no aspect attribute at all.
        var posterUrl = root.Elements("thumb").FirstOrDefault(e => (string?)e.Attribute("aspect") == "poster")?.Value?.Trim()
            ?? root.Elements("thumb").FirstOrDefault(e => e.Attribute("aspect") is null)?.Value?.Trim();

        var fanartUrl = root.Element("fanart")?.Elements("thumb").FirstOrDefault()?.Value?.Trim();

        int? runtimeMinutes = int.TryParse(El("runtime"), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedRuntime)
            ? parsedRuntime
            : null;

        DateTime? releaseDate = null;
        var releaseDateStr = El("releasedate") ?? El("premiered");
        if (releaseDateStr is not null && DateTime.TryParse(releaseDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            releaseDate = parsedDate;
        }
        else if (int.TryParse(El("year"), out var year) && year > 0)
        {
            releaseDate = new DateTime(year, 1, 1);
        }

        return new LocalMovieMetadata
        {
            Title = El("title"),
            OriginalTitle = El("originaltitle"),
            Plot = El("plot"),
            ReleaseDate = releaseDate,
            Director = El("director"),
            Studio = El("studio") ?? El("maker"),
            Label = El("label"),
            SeriesName = root.Element("set")?.Element("name")?.Value?.Trim() ?? El("set"),
            RatingScore = rating,
            RatingVotes = ratingVotes,
            RuntimeMinutes = runtimeMinutes,
            Genres = genres,
            Actresses = actresses,
            Actors = actors,
            PosterFallbackUrl = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl,
            FanartFallbackUrl = string.IsNullOrWhiteSpace(fanartUrl) ? null : fanartUrl
        };
    }
}
