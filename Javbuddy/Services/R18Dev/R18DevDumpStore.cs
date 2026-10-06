using Javbuddy.Services.Movies;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.R18Dev;

public sealed record R18DevFilmographyEntry(
    string DvdId,
    string? TitleEn,
    string? TitleJa,
    DateOnly? ReleaseDate,
    string? PosterUrl,
    string? MakerName = null,
    int ActressCount = 1,
    bool IsCompilation = false,
    bool IsVr = false)
{
    public string DisplayTitle => !string.IsNullOrWhiteSpace(TitleEn) ? TitleEn : !string.IsNullOrWhiteSpace(TitleJa) ? TitleJa : DvdId;
    public bool IsSolo => ActressCount <= 1;
}

public sealed record R18DevFilmographyResult(bool DumpAvailable, IReadOnlyList<R18DevFilmographyEntry> Movies);

public sealed record R18DevMovieDetail(
    string DvdId,
    string? TitleEn,
    string? TitleJa,
    string? DescriptionEn,
    string? DescriptionJa,
    int? RuntimeMinutes,
    DateOnly? ReleaseDate,
    string? PosterUrl,
    string? Studio,
    string? Label,
    string? Series,
    string? Director,
    IReadOnlyList<string> Actresses,
    IReadOnlyList<string> Genres,
    string? TrailerUrl,
    IReadOnlyList<string> GalleryUrls,
    IReadOnlyList<string>? GalleryThumbUrls = null,
    R18DevCatalogRef? SeriesRef = null,
    R18DevCatalogRef? LabelRef = null)
{
    public string DisplayTitle => !string.IsNullOrWhiteSpace(TitleEn) ? TitleEn : !string.IsNullOrWhiteSpace(TitleJa) ? TitleJa : DvdId;
    public string? Description => !string.IsNullOrWhiteSpace(DescriptionEn) ? DescriptionEn : DescriptionJa;
}

public interface IR18DevDumpStore
{
    /// <summary>True once a dump has been imported (the sidecar SQLite file exists) — the
    /// Metadata settings page/actor page use this to distinguish "enabled but never imported"
    /// from "disabled".</summary>
    bool IsAvailable();

    /// <summary>Deletes the local sidecar SQLite dump file (and its rollback journal, if one is
    /// present) — a regenerable cache, so this never touches the main AppDbContext or the r18.dev
    /// settings row itself. A no-op when no dump exists.</summary>
    void DeleteDump();

    /// <summary>Finds every movie credited to the same r18.dev actress/actresses as the actor's
    /// already-linked local movies — the actor's full known filmography, not filtered against the
    /// local library (the caller cross-references against Movie.Code/Status itself, since which
    /// entries are already owned/wanted needs the main AppDbContext, not this sidecar DB).
    ///
    /// Bootstraps the actress identity from linkedMovieCodes first (needs at least one
    /// locally-tracked movie r18.dev also has). A linked movie's r18.dev cast can include more
    /// than one actress (a collab/compilation), so when the bootstrap turns up more than one
    /// candidate it's narrowed back down by cross-checking against actorName — otherwise a shared
    /// movie with someone else would pull in that person's entire unrelated filmography too. If
    /// bootstrapping finds nothing at all, falls back to matching actorName against r18.dev's
    /// name_romaji — tried both as-is and, for a two-word name, with the words swapped (r18.dev
    /// romanizes "FirstName LastName", the opposite of actorName's usual "LastName FirstName"
    /// convention). r18DevNameOverride, when set, skips both of those and is matched exactly
    /// instead — for names the heuristic can't handle (not two words, or a differently romanized
    /// name entirely).</summary>
    Task<R18DevFilmographyResult> GetFilmographyForActorAsync(
        IReadOnlyCollection<string> linkedMovieCodes,
        string? actorName,
        string? r18DevNameOverride,
        CancellationToken ct = default);

    /// <summary>Looks up a single movie by code, with studio/label/series/director, genres,
    /// cast, trailer and gallery images — enough to render a preview of a not-yet-added movie
    /// (see MovieDetail.razor's preview branch). Returns null on a dump miss (including when no
    /// dump is available).</summary>
    Task<R18DevMovieDetail?> GetMovieByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Whether the catalog methods below can run: they need a dump imported with
    /// <see cref="R18DevCatalogFacets"/>' columns.</summary>
    Task<R18DevCatalogAvailability> GetCatalogAvailabilityAsync(CancellationToken ct = default);

    /// <summary>One page of the catalog matching <paramref name="filter"/> (and, when given,
    /// <paramref name="keys"/>), newest first. Distributor variants are left out; other variants of one
    /// release can still come back as separate rows (the caller collapses them). Not cross-referenced
    /// against the local library — <paramref name="keys"/> is how the caller narrows by it.</summary>
    Task<IReadOnlyList<R18DevFilmographyEntry>> GetCatalogPageAsync(
        R18DevCatalogFilter filter,
        R18DevCanonicalKeyFilter? keys,
        int offset,
        int limit,
        CancellationToken ct = default);

    /// <summary>How many distinct releases (canonical codes) <see cref="GetCatalogPageAsync"/> can
    /// page through for the same arguments.</summary>
    Task<int> CountCatalogAsync(R18DevCatalogFilter filter, R18DevCanonicalKeyFilter? keys, CancellationToken ct = default);

    /// <summary>Every r18.dev category with at least one release, by name, for the genre filter.</summary>
    Task<IReadOnlyList<R18DevCategoryOption>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>The years of the oldest and newest release dates in the dump, or null when it has none.</summary>
    Task<(int Min, int Max)?> GetReleaseYearBoundsAsync(CancellationToken ct = default);

    /// <summary>Up to <paramref name="limit"/> releases whose code starts with <paramref name="term"/>
    /// (ignoring case, hyphens and spaces): an exact code first, then newest first, one per release.
    /// Empty for a term shorter than two characters.</summary>
    Task<IReadOnlyList<R18DevFilmographyEntry>> SuggestAsync(string term, int limit, CancellationToken ct = default);
}

/// <summary>Read-only queries against the r18.dev dump's sidecar SQLite file (see
/// R18DevDumpImporter). Opened in read-only mode so it can be safely queried while a re-import is
/// building the next temp file — the atomic rename only swaps the new file in once it's complete.</summary>
public partial class R18DevDumpStore(IConfiguration configuration) : IR18DevDumpStore
{
    // The columns (and their order) ReadFilmographyEntry expects — shared by the actor filmography
    // and the prefix/studio release browse so both classify compilations/VR identically.
    private const string FilmographyEntryColumnsSql = """
        v.dvd_id,
        v.title_en,
        v.title_ja,
        v.release_date,
        v.jacket_thumb_url,
        m.name_en,
        (SELECT COUNT(*) FROM video_actresses va2 WHERE va2.content_id = v.content_id) as actress_count,
        (
            EXISTS (
                SELECT 1 FROM video_categories vc
                JOIN categories cat ON cat.id = vc.category_id
                WHERE vc.content_id = v.content_id AND (cat.name_en IN ('Compilation', 'Over 4 Hours') OR cat.name_ja LIKE '%総集編%' OR cat.name_ja LIKE '%ベスト%')
            ) OR
            v.dvd_id LIKE '%BOX%' OR
            (v.title_en IS NOT NULL AND (v.title_en LIKE '%BOX%' OR v.title_en LIKE '%Collection%' OR v.title_en LIKE '%Hours%')) OR
            (SELECT COUNT(*) FROM video_actresses va2 WHERE va2.content_id = v.content_id) >= 5
        ) as is_compilation,
        (
            v.dvd_id LIKE '%VR%' OR v.dvd_id LIKE '%3DBD%' OR
            EXISTS (
                SELECT 1 FROM video_categories vc
                JOIN categories cat ON cat.id = vc.category_id
                WHERE vc.content_id = v.content_id AND (cat.name_en = 'VR' OR cat.name_ja LIKE '%VR%')
            )
        ) as is_vr
        """;

    private readonly IConfiguration configuration = configuration;

    public bool IsAvailable() => File.Exists(R18DevDumpPaths.GetDbPath(configuration));

    public void DeleteDump()
    {
        var dbPath = R18DevDumpPaths.GetDbPath(configuration);
        // Clears just this dump's pooled connections (not the whole process' pools, unlike
        // ClearAllPools) so a previously-opened read connection doesn't keep the file handle
        // alive underneath the delete below.
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            SqliteConnection.ClearPool(connection);
        }
        if (File.Exists(dbPath)) File.Delete(dbPath);
        var journalPath = dbPath + "-journal";
        if (File.Exists(journalPath)) File.Delete(journalPath);
    }

    public async Task<R18DevFilmographyResult> GetFilmographyForActorAsync(
        IReadOnlyCollection<string> linkedMovieCodes,
        string? actorName,
        string? r18DevNameOverride,
        CancellationToken ct = default)
    {
        if (!IsAvailable())
        {
            return new R18DevFilmographyResult(false, Array.Empty<R18DevFilmographyEntry>());
        }

        var dbPath = R18DevDumpPaths.GetDbPath(configuration);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(ct);

        List<string> actressIds;
        if (!string.IsNullOrWhiteSpace(r18DevNameOverride))
        {
            // An explicit override is trusted exactly as given — no reordering guesswork.
            actressIds = await QueryActressIdsByNamesAsync(connection, new List<string> { r18DevNameOverride.Trim() }, ct);
        }
        else
        {
            var bootstrapped = await BootstrapActressIdsFromCodesAsync(connection, linkedMovieCodes, ct);
            if (bootstrapped.Count > 1)
            {
                // More than one actress turned up across the linked movies' r18.dev cast lists —
                // at least one of those movies has other cast members too (a collab/compilation),
                // so trusting every co-star as "this actor" would pull in each of their entire
                // unrelated filmographies as well (the root cause of an actor's Missing page
                // showing thousands of mostly-unrelated movies). Narrow back down to whichever of
                // them also matches the actor's own name; only if none of them do, fall back to
                // the untrimmed bootstrap set rather than returning nothing.
                var byName = await QueryActressIdsByNamesAsync(connection, NameOrderCandidates(actorName), ct);
                var disambiguated = bootstrapped.Intersect(byName).ToList();
                actressIds = disambiguated.Count > 0 ? disambiguated : bootstrapped;
            }
            else if (bootstrapped.Count == 1)
            {
                actressIds = bootstrapped;
            }
            else
            {
                actressIds = await QueryActressIdsByNamesAsync(connection, NameOrderCandidates(actorName), ct);
            }
        }

        if (actressIds.Count == 0)
        {
            return new R18DevFilmographyResult(true, Array.Empty<R18DevFilmographyEntry>());
        }

        var filmography = new List<R18DevFilmographyEntry>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT DISTINCT
                    {FilmographyEntryColumnsSql}
                FROM video_actresses va
                JOIN videos v ON v.content_id = va.content_id
                LEFT JOIN makers m ON m.id = v.maker_id
                WHERE va.actress_id IN ({InClause(actressIds, "a")})
                  AND v.dvd_id IS NOT NULL
                """;
            BindInClause(cmd, actressIds, "a");

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                filmography.Add(ReadFilmographyEntry(reader));
            }
        }

        return new R18DevFilmographyResult(true, DedupeByCode(filmography));
    }

    private static async Task<List<string>> BootstrapActressIdsFromCodesAsync(
        SqliteConnection connection, IReadOnlyCollection<string> linkedMovieCodes, CancellationToken ct)
    {
        var linkedNorms = linkedMovieCodes.Select(CodeNormalization.Normalize).Where(n => n.Length > 0).Distinct().ToList();
        if (linkedNorms.Count == 0) return new List<string>();

        var contentIds = await QueryStringListAsync(connection,
            $"SELECT content_id FROM videos WHERE dvd_id_norm IN ({InClause(linkedNorms, "n")})",
            linkedNorms, "n", ct);
        if (contentIds.Count == 0) return new List<string>();

        return await QueryStringListAsync(connection,
            $"SELECT DISTINCT actress_id FROM video_actresses WHERE content_id IN ({InClause(contentIds, "c")})",
            contentIds, "c", ct);
    }

    /// <summary>The name as given, plus — for a two-word name only — the words swapped, since
    /// r18.dev romanizes "FirstName LastName" while actorName is normally "LastName FirstName".
    /// A name of any other word count only yields the as-given form (nothing to swap).</summary>
    private static List<string> NameOrderCandidates(string? name)
    {
        var candidates = new List<string>();
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return candidates;

        candidates.Add(trimmed);
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            candidates.Add($"{parts[1]} {parts[0]}");
        }
        return candidates;
    }

    private static async Task<List<string>> QueryActressIdsByNamesAsync(SqliteConnection connection, List<string> names, CancellationToken ct)
    {
        if (names.Count == 0) return new List<string>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT id FROM actresses WHERE name_romaji COLLATE NOCASE IN ({InClause(names, "nm")})";
        BindInClause(cmd, names, "nm");

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0)) results.Add(reader.GetValue(0).ToString()!);
        }
        return results;
    }

    public async Task<R18DevMovieDetail?> GetMovieByCodeAsync(string code, CancellationToken ct = default)
    {
        if (!IsAvailable()) return null;

        var norm = CodeNormalization.Normalize(code);
        if (norm.Length == 0) return null;

        var dbPath = R18DevDumpPaths.GetDbPath(configuration);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(ct);

        string contentId, dvdId;
        string? titleEn, titleJa, commentEn, commentJa, releaseDateText, jacketFullUrl, jacketThumbUrl;
        string? galleryFullFirst, galleryFullLast, galleryThumbFirst, galleryThumbLast;
        int? runtimeMins;
        string? makerId, labelId, seriesId;

        await using (var cmd = connection.CreateCommand())
        {
            // The same dvd_id can appear under more than one content_id — prefer the row with a
            // title (same "most complete" heuristic as GetFilmographyForActorAsync).
            cmd.CommandText = """
                SELECT content_id, dvd_id, title_en, title_ja, comment_en, comment_ja, runtime_mins, release_date,
                       jacket_full_url, jacket_thumb_url, gallery_full_first, gallery_full_last,
                       gallery_thumb_first, gallery_thumb_last, maker_id, label_id, series_id
                FROM videos
                WHERE dvd_id_norm = $norm
                ORDER BY (title_en IS NOT NULL) DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$norm", norm);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;

            contentId = reader.GetString(0);
            dvdId = reader.GetString(1);
            titleEn = reader.IsDBNull(2) ? null : reader.GetString(2);
            titleJa = reader.IsDBNull(3) ? null : reader.GetString(3);
            commentEn = reader.IsDBNull(4) ? null : reader.GetString(4);
            commentJa = reader.IsDBNull(5) ? null : reader.GetString(5);
            runtimeMins = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            releaseDateText = reader.IsDBNull(7) ? null : reader.GetString(7);
            jacketFullUrl = reader.IsDBNull(8) ? null : reader.GetString(8);
            jacketThumbUrl = reader.IsDBNull(9) ? null : reader.GetString(9);
            galleryFullFirst = reader.IsDBNull(10) ? null : reader.GetString(10);
            galleryFullLast = reader.IsDBNull(11) ? null : reader.GetString(11);
            galleryThumbFirst = reader.IsDBNull(12) ? null : reader.GetString(12);
            galleryThumbLast = reader.IsDBNull(13) ? null : reader.GetString(13);
            makerId = reader.IsDBNull(14) ? null : reader.GetValue(14).ToString();
            labelId = reader.IsDBNull(15) ? null : reader.GetValue(15).ToString();
            seriesId = reader.IsDBNull(16) ? null : reader.GetValue(16).ToString();
        }

        var studio = makerId is not null ? await QueryEntityNameAsync(connection, "makers", makerId, ct) : null;
        var label = labelId is not null ? await QueryEntityNameAsync(connection, "labels", labelId, ct) : null;
        var series = seriesId is not null ? await QueryEntityNameAsync(connection, "series", seriesId, ct) : null;

        var actresses = await QueryNamesAsync(connection, """
            SELECT a.name_romaji, a.name_kanji
            FROM video_actresses va
            JOIN actresses a ON a.id = va.actress_id
            WHERE va.content_id = $contentId
            ORDER BY va.ordinality
            """, contentId, ct);

        var director = (await QueryNamesAsync(connection, """
            SELECT d.name_romaji, d.name_kanji
            FROM video_directors vd
            JOIN directors d ON d.id = vd.director_id
            WHERE vd.content_id = $contentId
            """, contentId, ct)).FirstOrDefault();

        var genres = await QueryNamesAsync(connection, """
            SELECT c.name_en, c.name_ja
            FROM video_categories vc
            JOIN categories c ON c.id = vc.category_id
            WHERE vc.content_id = $contentId
            """, contentId, ct);

        string? trailerUrl = null;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT url FROM trailers WHERE content_id = $contentId";
            cmd.Parameters.AddWithValue("$contentId", contentId);
            var result = await cmd.ExecuteScalarAsync(ct);
            trailerUrl = result as string;
        }

        DateOnly? releaseDate = DateOnly.TryParse(releaseDateText, out var parsed) ? parsed : null;
        var posterUrl = NormalizeDumpUrl(jacketFullUrl) ?? NormalizeDumpUrl(jacketThumbUrl);

        // Prefer the full-size gallery range — the thumb range is a much smaller crop. Only fall
        // back to it when the full range is incomplete (either endpoint missing), matching
        // javinizer-go's ThumbGalleryFallback/AsymmetricGalleryFallback behavior.
        // Full gallery paths are normalized so DMM digital video thumbnail ranges (-1..-20)
        // recorded in gallery_full_first/last (e.g. MGMJ-077) map to full-size jp- preview URLs.
        var galleryUrls = !string.IsNullOrWhiteSpace(galleryFullFirst) && !string.IsNullOrWhiteSpace(galleryFullLast)
            ? ExpandGallery(NormalizeFullGalleryPath(galleryFullFirst), NormalizeFullGalleryPath(galleryFullLast))
            : ExpandGallery(NormalizeFullGalleryPath(galleryThumbFirst), NormalizeFullGalleryPath(galleryThumbLast));

        var galleryThumbUrls = !string.IsNullOrWhiteSpace(galleryThumbFirst) && !string.IsNullOrWhiteSpace(galleryThumbLast)
            ? ExpandGallery(galleryThumbFirst, galleryThumbLast)
            : Array.Empty<string>();

        return new R18DevMovieDetail(dvdId, titleEn, titleJa, commentEn, commentJa, runtimeMins, releaseDate,
            posterUrl, studio, label, series, director, actresses, genres, trailerUrl, galleryUrls, galleryThumbUrls,
            CatalogRef(seriesId, series), CatalogRef(labelId, label));
    }

    private static R18DevCatalogRef? CatalogRef(string? id, string? name) =>
        long.TryParse(id, out var parsed) && !string.IsNullOrWhiteSpace(name) ? new R18DevCatalogRef(parsed, name) : null;

    private static async Task<string?> QueryEntityNameAsync(SqliteConnection connection, string table, string id, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT name_en, name_ja FROM {table} WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var nameEn = reader.IsDBNull(0) ? null : reader.GetString(0);
        var nameJa = reader.IsDBNull(1) ? null : reader.GetString(1);
        return !string.IsNullOrWhiteSpace(nameEn) ? nameEn : nameJa;
    }

    /// <summary>Runs a two-column (primary-name, fallback-name) query scoped to $contentId and
    /// returns one non-blank name per row, preferring the first column. Shared by the
    /// actress/director/genre lookups in GetMovieByCodeAsync — same "romaji/en, falling back to
    /// kanji/ja" shape each time, just different tables.</summary>
    private static async Task<List<string>> QueryNamesAsync(SqliteConnection connection, string sql, string contentId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$contentId", contentId);

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var primary = reader.IsDBNull(0) ? null : reader.GetString(0);
            var fallback = reader.IsDBNull(1) ? null : reader.GetString(1);
            var name = !string.IsNullOrWhiteSpace(primary) ? primary : fallback;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names;
    }

    // DMM's image CDN — dump paths are relative to this. pics.dmm.co.jp, not
    // awsimgsrc.dmm.com/dig/: the dump mixes path shapes from different DMM services (e.g.
    // "digital/video/{id}/{id}pl" vs. "mono/movie/adult/{id}/{id}ps" for mono/physical-media
    // titles), and awsimgsrc's /dig/ prefix only resolves the "digital/" shape — it 404s (DMM
    // then serves a small "Now Printing" placeholder graphic, not an error) for "mono/" and
    // presumably other non-digital service paths. pics.dmm.co.jp serves every shape correctly.
    private const string DmmImageCdnBase = "https://pics.dmm.co.jp/";

    /// <summary>Resolves a dump-relative image path (e.g. "digital/video/x/xps") to an absolute
    /// DMM CDN URL, appending ".jpg" when the path has no extension of its own. Already-absolute
    /// URLs pass through unchanged.</summary>
    public static string? NormalizeDumpUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return Path.HasExtension(trimmed) ? $"{DmmImageCdnBase}{trimmed}" : $"{DmmImageCdnBase}{trimmed}.jpg";
    }

    /// <summary>Normalizes a DMM digital video (or amateur) gallery path or URL to use the
    /// high-resolution full preview filename variant ("jp-") instead of the thumbnail variant
    /// ("-" for digital/video, "js-" for digital/amateur). In the r18.dev dump, many titles (e.g.
    /// MGMJ-077) recorded the thumbnail path in gallery_full_first/last; normalizing repairs them
    /// so full-resolution preview images are resolved.</summary>
    public static string? NormalizeFullGalleryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.Trim();
        var extension = Path.GetExtension(trimmed);
        var withoutExtension = string.IsNullOrEmpty(extension) ? trimmed : trimmed[..^extension.Length];

        var dash = withoutExtension.LastIndexOf('-');
        if (dash < 0) return trimmed;

        if (!int.TryParse(withoutExtension[(dash + 1)..], out _)) return trimmed;

        var prefix = withoutExtension[..dash];
        var suffix = withoutExtension[(dash + 1)..];

        if (trimmed.Contains("digital/video/", StringComparison.OrdinalIgnoreCase))
        {
            if (!prefix.EndsWith("jp", StringComparison.OrdinalIgnoreCase))
            {
                prefix += "jp";
                return $"{prefix}-{suffix}{extension}";
            }
        }
        else if (trimmed.Contains("digital/amateur/", StringComparison.OrdinalIgnoreCase))
        {
            if (prefix.EndsWith("js", StringComparison.OrdinalIgnoreCase))
            {
                prefix = prefix[..^2] + "jp";
                return $"{prefix}-{suffix}{extension}";
            }
        }

        return trimmed;
    }

    /// <summary>Expands a "first"/"last" numbered-filename gallery range (e.g.
    /// ".../xjp-1" .. ".../xjp-12") into individual image URLs. Requires both endpoints to share
    /// the same prefix and a non-decreasing numeric suffix, and caps the range at 1000 images as
    /// a guard against a corrupt/mismatched pair producing a huge loop. Mirrors javinizer-go's
    /// ExpandGallery.</summary>
    public static IReadOnlyList<string> ExpandGallery(string? first, string? last)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last)) return Array.Empty<string>();

        var (prefixFirst, numFirst) = SplitTrailingNumber(first);
        var (prefixLast, numLast) = SplitTrailingNumber(last);
        if (prefixFirst is null || prefixLast is null || prefixFirst != prefixLast) return Array.Empty<string>();
        if (numFirst > numLast || numLast - numFirst + 1 > 1000) return Array.Empty<string>();

        var urls = new List<string>(numLast - numFirst + 1);
        for (var n = numFirst; n <= numLast; n++)
        {
            urls.Add(NormalizeDumpUrl($"{prefixFirst}{n}")!);
        }
        return urls;
    }

    private static (string? Prefix, int Number) SplitTrailingNumber(string path)
    {
        var dash = path.LastIndexOf('-');
        if (dash < 0) return (null, 0);
        return int.TryParse(path[(dash + 1)..], out var num) ? (path[..(dash + 1)], num) : (null, 0);
    }

    private static R18DevFilmographyEntry ReadFilmographyEntry(SqliteDataReader reader)
    {
        var dvdId = reader.GetString(0);
        var titleEn = reader.IsDBNull(1) ? null : reader.GetString(1);
        var titleJa = reader.IsDBNull(2) ? null : reader.GetString(2);
        var releaseDateText = reader.IsDBNull(3) ? null : reader.GetString(3);
        var jacketThumbUrl = reader.IsDBNull(4) ? null : reader.GetString(4);
        var makerName = reader.IsDBNull(5) ? null : reader.GetString(5);
        var actressCount = reader.IsDBNull(6) ? 1 : Convert.ToInt32(reader.GetValue(6));
        var isCompilation = !reader.IsDBNull(7) && Convert.ToInt64(reader.GetValue(7)) != 0;
        var isVr = !reader.IsDBNull(8) && Convert.ToInt64(reader.GetValue(8)) != 0;

        DateOnly? releaseDate = DateOnly.TryParse(releaseDateText, out var parsed) ? parsed : null;

        return new R18DevFilmographyEntry(
            dvdId,
            titleEn,
            titleJa,
            releaseDate,
            NormalizeDumpUrl(jacketThumbUrl),
            makerName,
            actressCount,
            isCompilation,
            isVr);
    }

    // The same dvd_id can appear under more than one content_id (e.g. sold through more than
    // one service/site with slightly different metadata) — collapse to one row per movie
    // code, preferring the most complete-looking row when they disagree.
    private static List<R18DevFilmographyEntry> DedupeByCode(IEnumerable<R18DevFilmographyEntry> entries) => entries
        .GroupBy(m => CodeNormalization.Normalize(m.DvdId))
        .Select(g => g
            .OrderByDescending(m => !string.IsNullOrWhiteSpace(m.TitleEn))
            .ThenByDescending(m => !string.IsNullOrWhiteSpace(m.PosterUrl))
            .ThenByDescending(m => !string.IsNullOrWhiteSpace(m.MakerName))
            .First())
        .OrderByDescending(m => m.ReleaseDate)
        .ToList();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string InClause(IReadOnlyCollection<string> values, string prefix) =>
        string.Join(",", Enumerable.Range(0, values.Count).Select(i => $"${prefix}{i}"));

    private static void BindInClause(SqliteCommand cmd, IReadOnlyCollection<string> values, string prefix)
    {
        var i = 0;
        foreach (var value in values)
        {
            cmd.Parameters.AddWithValue($"${prefix}{i}", value);
            i++;
        }
    }

    private static async Task<List<string>> QueryStringListAsync(SqliteConnection connection, string sql, IReadOnlyCollection<string> inValues, string prefix, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        BindInClause(cmd, inValues, prefix);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0)) results.Add(reader.GetValue(0).ToString()!);
        }
        return results;
    }
}
