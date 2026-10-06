using Javbuddy.Services.Movies;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.R18Dev;

/// <summary>Precomputes the per-release columns and indexes the catalog browse filters
/// and pages on, so a filter over the whole ~1.8M-row catalog is an indexed lookup instead of a
/// per-row subquery: each video's canonical code (to match the local library in SQL), whether it's a
/// distributor variant, its cast size, and the compilation/VR classification the actor filmography
/// already used. Run by the importer once the rows are in; idempotent, so it can also upgrade a dump
/// imported before it existed.</summary>
public static class R18DevCatalogFacets
{
    /// <summary>Bumped whenever <see cref="Build"/> adds or changes a column or index the store's
    /// catalog queries rely on. Stored in dump_meta.catalog_version; a dump with an older value needs
    /// a re-import before the catalog browse can query it.</summary>
    public const int CurrentVersion = 1;

    // The same heuristics FilmographyEntryColumnsSql used per row: categories named like a
    // compilation, a BOX set code/title, or five or more credited actresses.
    private const string CompilationCategorySql = """
        SELECT id FROM categories
        WHERE name_en IN ('Compilation', 'Over 4 Hours') OR name_ja LIKE '%総集編%' OR name_ja LIKE '%ベスト%'
        """;

    private const string VrCategorySql = "SELECT id FROM categories WHERE name_en = 'VR' OR name_ja LIKE '%VR%'";

    public static void Build(SqliteConnection connection, CancellationToken ct = default)
    {
        AddColumnIfMissing(connection, "videos", "canonical_key", "TEXT");
        AddColumnIfMissing(connection, "videos", "is_distributor", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "videos", "actress_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "videos", "is_compilation", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "videos", "is_vr", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "categories", "video_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "dump_meta", "catalog_version", "INTEGER");

        BuildCodeColumns(connection, ct);

        Execute(connection, $"""
            UPDATE videos SET actress_count = (SELECT COUNT(*) FROM video_actresses va WHERE va.content_id = videos.content_id)
            WHERE content_id IN (SELECT content_id FROM video_actresses);

            -- COALESCE: a NULL dvd_id makes its LIKE NULL, which an OR of otherwise-false terms keeps.
            UPDATE videos SET is_compilation = COALESCE(
                content_id IN (SELECT content_id FROM video_categories WHERE category_id IN ({CompilationCategorySql}))
                OR dvd_id LIKE '%BOX%'
                OR (title_en IS NOT NULL AND (title_en LIKE '%BOX%' OR title_en LIKE '%Collection%' OR title_en LIKE '%Hours%'))
                OR actress_count >= 5, 0);

            UPDATE videos SET is_vr = COALESCE(
                dvd_id LIKE '%VR%' OR dvd_id LIKE '%3DBD%'
                OR content_id IN (SELECT content_id FROM video_categories WHERE category_id IN ({VrCategorySql})), 0);

            CREATE INDEX IF NOT EXISTS idx_videos_browse ON videos(release_date DESC, canonical_key, content_id);
            CREATE INDEX IF NOT EXISTS idx_videos_canonical_key ON videos(canonical_key);
            CREATE INDEX IF NOT EXISTS idx_videos_maker ON videos(maker_id, release_date);
            CREATE INDEX IF NOT EXISTS idx_videos_label ON videos(label_id, release_date);
            CREATE INDEX IF NOT EXISTS idx_videos_series ON videos(series_id, release_date);
            CREATE INDEX IF NOT EXISTS idx_video_categories_category ON video_categories(category_id, content_id);

            UPDATE categories SET video_count = (SELECT COUNT(*) FROM video_categories vc WHERE vc.category_id = categories.id);

            UPDATE dump_meta SET catalog_version = {CurrentVersion};
            """);
    }

    /// <summary>canonical_key and is_distributor come from CodeNormalization, which only exists in
    /// C#, so they're filled in rowid-ordered batches rather than by one UPDATE — a batch at a time,
    /// so the whole catalog's codes are never held in memory together.</summary>
    private static void BuildCodeColumns(SqliteConnection connection, CancellationToken ct)
    {
        const int batchSize = 50_000;
        var lastRowId = 0L;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<(long RowId, string DvdId)>(batchSize);
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT rowid, dvd_id FROM videos WHERE rowid > $last AND dvd_id IS NOT NULL ORDER BY rowid LIMIT $take";
                select.Parameters.AddWithValue("$last", lastRowId);
                select.Parameters.AddWithValue("$take", batchSize);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    batch.Add((reader.GetInt64(0), reader.GetString(1)));
                }
            }
            if (batch.Count == 0) return;

            using var transaction = connection.BeginTransaction();
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE videos SET canonical_key = $key, is_distributor = $distributor WHERE rowid = $rowid";
            var key = update.Parameters.Add("$key", SqliteType.Text);
            var distributor = update.Parameters.Add("$distributor", SqliteType.Integer);
            var rowId = update.Parameters.Add("$rowid", SqliteType.Integer);
            update.Prepare();

            foreach (var (id, dvdId) in batch)
            {
                key.Value = CodeNormalization.GetCanonicalKey(dvdId);
                distributor.Value = CodeNormalization.IsDistributorRelease(dvdId) ? 1 : 0;
                rowId.Value = id;
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            lastRowId = batch[^1].RowId;
        }
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column";
            check.Parameters.AddWithValue("$column", column);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }
        Execute(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.ExecuteNonQuery();
    }
}
