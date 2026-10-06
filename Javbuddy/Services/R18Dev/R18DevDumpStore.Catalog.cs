using System.Text.Json;
using Javbuddy.Services.Movies;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.R18Dev;

/// <summary>The catalog browse's queries: paged, filtered and counted over the
/// precomputed columns and indexes <see cref="R18DevCatalogFacets"/> adds at import.</summary>
public partial class R18DevDumpStore
{
    // ReadFilmographyEntry's columns, read from the precomputed facet columns instead of the
    // per-row subqueries FilmographyEntryColumnsSql runs.
    private const string CatalogEntryColumnsSql = """
        v.dvd_id,
        v.title_en,
        v.title_ja,
        v.release_date,
        v.jacket_thumb_url,
        (SELECT m.name_en FROM makers m WHERE m.id = v.maker_id),
        v.actress_count,
        v.is_compilation,
        v.is_vr
        """;

    // Codes are uppercase letters, digits and '_' once normalized, all of which sort before '~'.
    private const string CodePrefixUpperBound = "~";

    private sealed record CatalogVersionStamp(string Path, DateTime WriteTime, int Version);

    // Static (the store is scoped per circuit) and swapped by reference, so concurrent readers see
    // either the old stamp or the new one.
    private static CatalogVersionStamp? catalogVersionCache;

    public async Task<R18DevCatalogAvailability> GetCatalogAvailabilityAsync(CancellationToken ct = default)
    {
        var dbPath = R18DevDumpPaths.GetDbPath(configuration);
        if (!File.Exists(dbPath)) return R18DevCatalogAvailability.NoDump;

        // Cached per file write time: the importer swaps a new file in, which changes it.
        var writeTime = File.GetLastWriteTimeUtc(dbPath);
        var cached = catalogVersionCache;
        if (cached is null || cached.Path != dbPath || cached.WriteTime != writeTime)
        {
            await using var connection = await OpenReadOnlyAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('dump_meta') WHERE name = 'catalog_version'";
            var hasColumn = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
            var version = 0;
            if (hasColumn)
            {
                cmd.CommandText = "SELECT catalog_version FROM dump_meta WHERE id = 1";
                version = await cmd.ExecuteScalarAsync(ct) is long v ? (int)v : 0;
            }
            cached = new CatalogVersionStamp(dbPath, writeTime, version);
            catalogVersionCache = cached;
        }

        return cached.Version >= R18DevCatalogFacets.CurrentVersion ? R18DevCatalogAvailability.Ready : R18DevCatalogAvailability.NeedsReimport;
    }

    public async Task<IReadOnlyList<R18DevFilmographyEntry>> GetCatalogPageAsync(
        R18DevCatalogFilter filter,
        R18DevCanonicalKeyFilter? keys,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        await using var connection = await OpenReadOnlyAsync(ct);
        await using var cmd = connection.CreateCommand();
        var where = BuildCatalogWhere(cmd, filter, keys);

        // Variants of one release share a canonical key and usually a release date, so ordering by
        // the key next keeps them on the same page for DedupeCatalogPage; the content id makes the
        // order total, so offset paging never repeats or skips a row.
        cmd.CommandText = $"""
            SELECT {CatalogEntryColumnsSql}
            FROM videos v
            WHERE {where}
            ORDER BY v.release_date DESC, v.canonical_key, v.content_id
            LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var entries = new List<R18DevFilmographyEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(ReadFilmographyEntry(reader));
        }
        return entries;
    }

    public async Task<int> CountCatalogAsync(R18DevCatalogFilter filter, R18DevCanonicalKeyFilter? keys, CancellationToken ct = default)
    {
        await using var connection = await OpenReadOnlyAsync(ct);
        await using var cmd = connection.CreateCommand();
        var where = BuildCatalogWhere(cmd, filter, keys);
        cmd.CommandText = $"SELECT COUNT(DISTINCT v.canonical_key) FROM videos v WHERE {where}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<R18DevCategoryOption>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenReadOnlyAsync(ct);
        await using var cmd = connection.CreateCommand();
        // The dump repeats some names under several ids (e.g. three "Big Tits"), so they're merged.
        cmd.CommandText = """
            SELECT COALESCE(NULLIF(TRIM(name_en), ''), name_ja) AS name, SUM(video_count) AS total
            FROM categories
            WHERE video_count > 0 AND COALESCE(NULLIF(TRIM(name_en), ''), name_ja) IS NOT NULL
            GROUP BY name
            ORDER BY name COLLATE NOCASE
            """;
        var options = new List<R18DevCategoryOption>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            options.Add(new R18DevCategoryOption(reader.GetString(0), Convert.ToInt32(reader.GetValue(1))));
        }
        return options;
    }

    public async Task<(int Min, int Max)?> GetReleaseYearBoundsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenReadOnlyAsync(ct);
        await using var cmd = connection.CreateCommand();
        // Two index seeks on idx_videos_browse rather than one MIN/MAX scan.
        cmd.CommandText = """
            SELECT (SELECT release_date FROM videos WHERE release_date IS NOT NULL ORDER BY release_date LIMIT 1),
                   (SELECT release_date FROM videos WHERE release_date IS NOT NULL ORDER BY release_date DESC LIMIT 1)
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1)) return null;
        return DateOnly.TryParse(reader.GetString(0), out var min) && DateOnly.TryParse(reader.GetString(1), out var max)
            ? (min.Year, max.Year)
            : null;
    }

    public async Task<IReadOnlyList<R18DevFilmographyEntry>> SuggestAsync(string term, int limit, CancellationToken ct = default)
    {
        var norm = CodeNormalization.Normalize(term);
        if (norm.Length < 2) return [];

        await using var connection = await OpenReadOnlyAsync(ct);
        await using var cmd = connection.CreateCommand();
        // A range over idx_videos_dvd_id_norm: everything starting with what was typed, the exact
        // code first, then newest first. Over-fetched so collapsing variants still leaves enough.
        cmd.CommandText = $"""
            SELECT {CatalogEntryColumnsSql}
            FROM videos v
            WHERE v.dvd_id_norm >= $low AND v.dvd_id_norm < $high AND v.is_distributor = 0
            ORDER BY (v.dvd_id_norm = $low) DESC, v.release_date DESC, v.canonical_key
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$low", norm);
        cmd.Parameters.AddWithValue("$high", norm + CodePrefixUpperBound);
        cmd.Parameters.AddWithValue("$limit", limit * 3);

        var entries = new List<R18DevFilmographyEntry>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                entries.Add(ReadFilmographyEntry(reader));
            }
        }

        return entries
            .GroupBy(e => CodeNormalization.GetCanonicalKey(e.DvdId))
            .Select(g => g.First())
            .Take(limit)
            .ToList();
    }

    private static string BuildCatalogWhere(SqliteCommand cmd, R18DevCatalogFilter filter, R18DevCanonicalKeyFilter? keys)
    {
        // Distributor/retailer variants are hidden, like the actor Missing page's default.
        var conditions = new List<string> { "v.dvd_id IS NOT NULL", "v.is_distributor = 0" };

        var normalizedPrefix = CodeNormalization.Normalize(filter.CodePrefix);
        if (normalizedPrefix.Length > 0)
        {
            // [PREFIX0, PREFIX:) is every normalized code that is the prefix followed by a digit
            // (':' sorts right after '9'), so "MIDE" doesn't also pull in the unrelated "MIDEA-…".
            conditions.Add("v.dvd_id_norm >= $prefixLow AND v.dvd_id_norm < $prefixHigh");
            cmd.Parameters.AddWithValue("$prefixLow", normalizedPrefix + "0");
            cmd.Parameters.AddWithValue("$prefixHigh", normalizedPrefix + ":");
        }

        var text = filter.SearchText?.Trim() ?? "";
        if (text.Length > 0)
        {
            // LIKE is ASCII-case-insensitive in SQLite, and the user's own %/_ must match literally.
            var titleClause = "v.title_en LIKE $titleLike ESCAPE '\\' OR v.title_ja LIKE $titleLike ESCAPE '\\'";
            cmd.Parameters.AddWithValue("$titleLike", $"%{EscapeLike(text)}%");
            var normalizedText = CodeNormalization.Normalize(text);
            if (normalizedText.Length > 0)
            {
                conditions.Add($"(v.dvd_id_norm LIKE $codeLike ESCAPE '\\' OR {titleClause})");
                cmd.Parameters.AddWithValue("$codeLike", $"%{EscapeLike(normalizedText)}%");
            }
            else
            {
                conditions.Add($"({titleClause})");
            }
        }

        var studios = Distinct(filter.Studios);
        if (studios.Count > 0)
        {
            conditions.Add($"v.maker_id IN (SELECT id FROM makers WHERE name_en COLLATE NOCASE IN ({InClause(studios, "s")}))");
            BindInClause(cmd, studios, "s");
        }

        var genres = Distinct(filter.Genres);
        if (genres.Count > 0)
        {
            const string categoryName = "COALESCE(NULLIF(TRIM(c.name_en), ''), c.name_ja)";
            if (filter.MatchAllGenres)
            {
                for (var i = 0; i < genres.Count; i++)
                {
                    conditions.Add($"""
                        v.content_id IN (SELECT vc.content_id FROM video_categories vc
                            WHERE vc.category_id IN (SELECT c.id FROM categories c WHERE {categoryName} = $g{i}))
                        """);
                    cmd.Parameters.AddWithValue($"$g{i}", genres[i]);
                }
            }
            else
            {
                conditions.Add($"""
                    v.content_id IN (SELECT vc.content_id FROM video_categories vc
                        WHERE vc.category_id IN (SELECT c.id FROM categories c WHERE {categoryName} IN ({InClause(genres, "g")})))
                    """);
                BindInClause(cmd, genres, "g");
            }
        }

        // release_date is ISO "yyyy-MM-dd" text, so whole years are string ranges over the index.
        if (filter.YearFrom is { } yearFrom)
        {
            conditions.Add("v.release_date >= $yearFrom");
            cmd.Parameters.AddWithValue("$yearFrom", $"{yearFrom:D4}-01-01");
        }
        if (filter.YearTo is { } yearTo)
        {
            conditions.Add("v.release_date < $yearTo");
            cmd.Parameters.AddWithValue("$yearTo", $"{yearTo + 1:D4}-01-01");
        }

        AddFlagCondition(conditions, "v.is_vr", filter.Vr);
        AddFlagCondition(conditions, "v.is_compilation", filter.Compilation);

        var castSizes = filter.CastSizes.Distinct().ToList();
        if (castSizes.Count is > 0 and < 3)
        {
            conditions.Add("(" + string.Join(" OR ", castSizes.Select(size => size switch
            {
                R18DevCastSize.Solo => "v.actress_count = 1",
                R18DevCastSize.Multiple => "v.actress_count > 1",
                _ => "v.actress_count = 0",
            })) + ")");
        }

        if (filter.Series is { } series)
        {
            conditions.Add("v.series_id = $seriesId");
            cmd.Parameters.AddWithValue("$seriesId", series.Id);
        }
        if (filter.Label is { } label)
        {
            conditions.Add("v.label_id = $labelId");
            cmd.Parameters.AddWithValue("$labelId", label.Id);
        }

        if (keys is not null)
        {
            // One JSON parameter rather than a parameter per key: a library can hold more codes
            // than SQLite allows bound parameters.
            conditions.Add($"v.canonical_key {(keys.Exclude ? "NOT IN" : "IN")} (SELECT value FROM json_each($keys))");
            cmd.Parameters.AddWithValue("$keys", JsonSerializer.Serialize(keys.Keys));
        }

        return string.Join(" AND ", conditions);
    }

    private static void AddFlagCondition(List<string> conditions, string column, R18DevCatalogFlag flag)
    {
        if (flag == R18DevCatalogFlag.Only) conditions.Add($"{column} = 1");
        else if (flag == R18DevCatalogFlag.Exclude) conditions.Add($"{column} = 0");
    }

    private static List<string> Distinct(IReadOnlyCollection<string> values) => values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private async Task<SqliteConnection> OpenReadOnlyAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = R18DevDumpPaths.GetDbPath(configuration),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(ct);
        return connection;
    }
}
