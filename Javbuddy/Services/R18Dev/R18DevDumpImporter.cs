using System.Data;
using System.IO.Compression;
using Javbuddy.Data;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

public sealed record R18DevImportResult(bool Success, string? ErrorMessage, string? SourceDate, int VideoCount, int ActressCount)
{
    public string Summary => Success
        ? $"{VideoCount:N0} movies, {ActressCount:N0} actresses"
        : ErrorMessage ?? "import failed";
}

public interface IR18DevDumpImporter
{
    Task<R18DevImportResult> ImportAsync(IProgress<TaskProgress> progress, CancellationToken ct = default);
}

/// <summary>Downloads (or reads locally) r18.dev's public database dump — a gzipped pg_dump —
/// and streams it into a fresh sidecar SQLite file, then atomically swaps it in. Reimplements
/// javinizer-go's internal/r18devdump import pipeline for .NET: never holds the multi-GB
/// decompressed dump in memory, and a failed import never touches the previously-working DB
/// (built into a ".importing" temp file, only renamed over the live path on success).</summary>
public class R18DevDumpImporter(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IR18DevDumpImporter
{
    // r18.dev sits behind Cloudflare, which 403s the app's normal "Javbuddy/1.0" User-Agent
    // (see Program.cs's DefaultUserAgent) — this browser-like one overrides it per-request below,
    // since Cloudflare's bot check keys off looking like a real browser, not just being non-blank.
    private const string UserAgent = "Mozilla/5.0 (compatible; Javbuddy/1.0; +https://github.com/javbuddy/javbuddy)";
    private const string DefaultDumpUrl = "https://r18.dev/dumps/latest";

    private static readonly HashSet<string> WantedTables = new(StringComparer.Ordinal)
    {
        "derived_video", "derived_actress", "derived_video_actress", "derived_maker", "derived_label", "derived_series",
        "derived_director", "derived_video_director", "derived_category", "derived_video_category", "source_dmm_trailer",
    };

    // Guards against two imports racing on the same ".importing" temp file — the scheduler can
    // fire this task on startup (never run before) in the same window as a manual "Import now"
    // click, and a second run must not delete/open the file the first run still has open.
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;

    public async Task<R18DevImportResult> ImportAsync(IProgress<TaskProgress> progress, CancellationToken ct = default)
    {
        if (!await ImportLock.WaitAsync(0, ct))
        {
            return new R18DevImportResult(false, "an r18.dev import is already in progress", null, 0, 0);
        }

        try
        {
            return await ImportCoreAsync(progress, ct);
        }
        finally
        {
            ImportLock.Release();
        }
    }

    private async Task<R18DevImportResult> ImportCoreAsync(IProgress<TaskProgress> progress, CancellationToken ct)
    {
        progress.Report(new TaskProgress(0, null, "Downloading r18.dev dump"));
        var source = await ResolveSourceAsync(ct);
        var dbPath = R18DevDumpPaths.GetDbPath(configuration);
        // A unique-per-run name rather than a fixed ".importing" path: a crashed run's leftover
        // file (or one transiently held open by antivirus/an indexer right after creation) must
        // never block the *next* run from starting — it's an orphaned regenerable cache file,
        // not something we need to reclaim before proceeding.
        var tempPath = $"{dbPath}.importing-{Guid.NewGuid():N}";

        try
        {
            var (stream, resolvedLocation) = await OpenSourceStreamAsync(source, ct);
            await using (stream)
            {
                await using var connection = new SqliteConnection($"Data Source={tempPath}");
                await connection.OpenAsync(ct);
                CreateSchema(connection);

                progress.Report(new TaskProgress(0, null, "Importing r18.dev dump"));
                var counts = await ImportRowsAsync(connection, stream, progress, ct);
                var sourceDate = ExtractSourceDate(resolvedLocation);
                WriteDumpMeta(connection, resolvedLocation, sourceDate);
                progress.Report(new TaskProgress(0, null, "Indexing r18.dev catalog"));
                R18DevCatalogFacets.Build(connection, ct);

                await connection.CloseAsync();
                SqliteConnection.ClearPool(connection);

                await MoveFileWithRetryAsync(tempPath, dbPath, ct);
                TryDeleteFile(dbPath + "-journal");

                return new R18DevImportResult(true, null, sourceDate, counts.Videos, counts.Actresses);
            }
        }
        catch (Exception ex)
        {
            // Best-effort only: if this delete itself fails (e.g. the file is momentarily held
            // by another process), that must not mask the real ex being reported below.
            TryDeleteFile(tempPath);
            TryDeleteFile(tempPath + "-journal");
            return new R18DevImportResult(false, ex.Message, null, 0, 0);
        }
    }

    private async Task<string> ResolveSourceAsync(CancellationToken ct)
    {
        var envOverride = configuration["R18Dev:DumpSourceOverride"];
        if (!string.IsNullOrWhiteSpace(envOverride)) return envOverride;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.R18DevSettings.ReadSingleRowAsync(ct);
        return !string.IsNullOrWhiteSpace(settings?.DumpSourceOverride) ? settings!.DumpSourceOverride! : DefaultDumpUrl;
    }

    /// <summary>Opens the dump as a decompressed stream, whether it's an http(s) URL or a local
    /// file path/file:// URI (the latter is how development points at an already-downloaded
    /// dump without hitting the network). Returns the resolved location alongside the stream so
    /// the caller can pull a source date out of the final filename (post-redirect for HTTP).</summary>
    private async Task<(Stream Stream, string Location)> OpenSourceStreamAsync(string source, CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var client = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var finalUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? uri.AbsolutePath;
            var body = await response.Content.ReadAsStreamAsync(ct);
            Stream result = LooksGzipped(finalUrl) ? new GZipStream(body, CompressionMode.Decompress) : body;
            return (result, finalUrl);
        }

        var path = uri is { Scheme: "file" } ? uri.LocalPath : source;
        var fileStream = File.OpenRead(path);
        Stream fileResult = LooksGzipped(path) ? new GZipStream(fileStream, CompressionMode.Decompress) : fileStream;
        return (fileResult, path);
    }

    private static bool LooksGzipped(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractSourceDate(string location)
    {
        var fileName = Path.GetFileName(location);
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"\d{4}-\d{2}-\d{2}");
        return match.Success ? match.Value : null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup only — see the callers' comments. Never let this fail the caller.
        }
    }

    /// <summary>Renames the completed temp file over the live path, retrying on a transient
    /// "file in use" / "access denied" failure — observed in practice on Windows, most likely a
    /// brief exclusive lock from antivirus/indexing scanning the file right as it's replaced.
    /// Backs off increasingly (1s, 2s, ... up to 15s total) before giving up and letting the last
    /// attempt's exception propagate.</summary>
    private static async Task MoveFileWithRetryAsync(string source, string dest, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt < maxAttempts; attempt++)
        {
            try
            {
                File.Move(source, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
        File.Move(source, dest, overwrite: true);
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE videos (
                content_id TEXT PRIMARY KEY,
                dvd_id TEXT,
                dvd_id_norm TEXT,
                title_en TEXT,
                title_ja TEXT,
                comment_en TEXT,
                comment_ja TEXT,
                runtime_mins INTEGER,
                release_date TEXT,
                jacket_full_url TEXT,
                jacket_thumb_url TEXT,
                gallery_full_first TEXT,
                gallery_full_last TEXT,
                gallery_thumb_first TEXT,
                gallery_thumb_last TEXT,
                maker_id INTEGER,
                label_id INTEGER,
                series_id INTEGER
            );
            CREATE INDEX idx_videos_dvd_id_norm ON videos(dvd_id_norm);

            CREATE TABLE actresses (
                id INTEGER PRIMARY KEY,
                name_romaji TEXT,
                name_kanji TEXT,
                name_kana TEXT,
                image_url TEXT
            );

            CREATE TABLE video_actresses (
                content_id TEXT NOT NULL,
                actress_id INTEGER NOT NULL,
                ordinality INTEGER,
                PRIMARY KEY (content_id, actress_id)
            );
            CREATE INDEX idx_video_actresses_actress_id ON video_actresses(actress_id);

            CREATE TABLE makers (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE labels (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE series (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);

            CREATE TABLE directors (id INTEGER PRIMARY KEY, name_romaji TEXT, name_kanji TEXT, name_kana TEXT);

            CREATE TABLE video_directors (
                content_id TEXT NOT NULL,
                director_id INTEGER NOT NULL,
                PRIMARY KEY (content_id, director_id)
            );

            CREATE TABLE categories (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);

            CREATE TABLE video_categories (
                content_id TEXT NOT NULL,
                category_id INTEGER NOT NULL,
                PRIMARY KEY (content_id, category_id)
            );

            CREATE TABLE trailers (content_id TEXT PRIMARY KEY, url TEXT);

            CREATE TABLE dump_meta (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                source TEXT NOT NULL,
                source_date TEXT,
                imported_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void WriteDumpMeta(SqliteConnection connection, string source, string? sourceDate)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO dump_meta (id, source, source_date, imported_at) VALUES (1, $source, $sourceDate, $importedAt)";
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$sourceDate", (object?)sourceDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$importedAt", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private sealed record RowCounts(int Videos, int Actresses);

    private static async Task<RowCounts> ImportRowsAsync(SqliteConnection connection, Stream dumpStream, IProgress<TaskProgress> progress, CancellationToken ct)
    {
        // Commit periodically instead of wrapping the entire (multi-million-row) dump in one
        // transaction: SQLite has to keep every page the open transaction has touched in memory
        // (or its own journal) until commit, so one giant transaction over a 5GB+ dump makes the
        // process' working set balloon along with it. Committing every BatchSize rows bounds that
        // to one batch's worth regardless of dump size — atomicity of the *import as a whole* is
        // still guaranteed by the temp-file-then-rename swap in ImportCoreAsync, not by this
        // transaction, so committing along the way is safe: a failure partway through just
        // discards the whole temp file, committed batches and all.
        const int batchSize = 50_000;
        var pendingInBatch = 0;
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        using var insertVideo = PrepareInsert(connection, transaction,
            // OR IGNORE: derived_video's real PK is (content_id, service_code) — content_id
            // alone can collide across services (rare, but the dump does contain a few). We
            // only track content_id, so the first row for a given id wins and later ones drop.
            "INSERT OR IGNORE INTO videos (content_id, dvd_id, dvd_id_norm, title_en, title_ja, comment_en, comment_ja, runtime_mins, release_date, jacket_full_url, jacket_thumb_url, gallery_full_first, gallery_full_last, gallery_thumb_first, gallery_thumb_last, maker_id, label_id, series_id) VALUES ($content_id, $dvd_id, $dvd_id_norm, $title_en, $title_ja, $comment_en, $comment_ja, $runtime_mins, $release_date, $jacket_full_url, $jacket_thumb_url, $gallery_full_first, $gallery_full_last, $gallery_thumb_first, $gallery_thumb_last, $maker_id, $label_id, $series_id)",
            "content_id", "dvd_id", "dvd_id_norm", "title_en", "title_ja", "comment_en", "comment_ja", "runtime_mins", "release_date", "jacket_full_url", "jacket_thumb_url", "gallery_full_first", "gallery_full_last", "gallery_thumb_first", "gallery_thumb_last", "maker_id", "label_id", "series_id");

        using var insertActress = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO actresses (id, name_romaji, name_kanji, name_kana, image_url) VALUES ($id, $name_romaji, $name_kanji, $name_kana, $image_url)",
            "id", "name_romaji", "name_kanji", "name_kana", "image_url");

        using var insertVideoActress = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO video_actresses (content_id, actress_id, ordinality) VALUES ($content_id, $actress_id, $ordinality)",
            "content_id", "actress_id", "ordinality");

        using var insertMaker = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO makers (id, name_en, name_ja) VALUES ($id, $name_en, $name_ja)",
            "id", "name_en", "name_ja");

        using var insertLabel = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO labels (id, name_en, name_ja) VALUES ($id, $name_en, $name_ja)",
            "id", "name_en", "name_ja");

        using var insertSeries = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO series (id, name_en, name_ja) VALUES ($id, $name_en, $name_ja)",
            "id", "name_en", "name_ja");

        using var insertDirector = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO directors (id, name_romaji, name_kanji, name_kana) VALUES ($id, $name_romaji, $name_kanji, $name_kana)",
            "id", "name_romaji", "name_kanji", "name_kana");

        using var insertVideoDirector = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO video_directors (content_id, director_id) VALUES ($content_id, $director_id)",
            "content_id", "director_id");

        using var insertCategory = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO categories (id, name_en, name_ja) VALUES ($id, $name_en, $name_ja)",
            "id", "name_en", "name_ja");

        using var insertVideoCategory = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO video_categories (content_id, category_id) VALUES ($content_id, $category_id)",
            "content_id", "category_id");

        using var insertTrailer = PrepareInsert(connection, transaction,
            "INSERT OR IGNORE INTO trailers (content_id, url) VALUES ($content_id, $url)",
            "content_id", "url");

        // Reassigned to the new transaction after every periodic commit — Microsoft.Data.Sqlite
        // requires each command's Transaction property to match the connection's currently
        // pending transaction; it does not rebind automatically when the transaction is swapped.
        var allCommands = new[]
        {
            insertVideo, insertActress, insertVideoActress, insertMaker, insertLabel, insertSeries,
            insertDirector, insertVideoDirector, insertCategory, insertVideoCategory, insertTrailer,
        };

        var videoCount = 0;
        var actressCount = 0;
        var rowsProcessed = 0;

        string? currentTable = null;
        Dictionary<string, int> columnIndex = new();

        await foreach (var row in DumpParser.ParseAsync(dumpStream, WantedTables, ct))
        {
            // Total is left null throughout — the dump is a decompressing stream, so there's no
            // cheap way to know its total row count up front (see TaskProgress's own doc).
            progress.Report(new TaskProgress(++rowsProcessed, null, "Importing r18.dev dump"));

            if (row.Table != currentTable)
            {
                currentTable = row.Table;
                columnIndex = row.Columns.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i, StringComparer.Ordinal);
            }

            string? Get(string column) => columnIndex.TryGetValue(column, out var i) && i < row.Values.Count ? row.Values[i] : null;

            switch (row.Table)
            {
                case "derived_video":
                    var dvdId = Get("dvd_id");
                    SetParam(insertVideo, "$content_id", Get("content_id"));
                    SetParam(insertVideo, "$dvd_id", dvdId);
                    SetParam(insertVideo, "$dvd_id_norm", CodeNormalization.Normalize(dvdId) is { Length: > 0 } norm ? norm : null);
                    SetParam(insertVideo, "$title_en", Get("title_en"));
                    SetParam(insertVideo, "$title_ja", Get("title_ja"));
                    SetParam(insertVideo, "$comment_en", Get("comment_en"));
                    SetParam(insertVideo, "$comment_ja", Get("comment_ja"));
                    SetParam(insertVideo, "$runtime_mins", Get("runtime_mins"));
                    SetParam(insertVideo, "$release_date", Get("release_date"));
                    SetParam(insertVideo, "$jacket_full_url", Get("jacket_full_url"));
                    SetParam(insertVideo, "$jacket_thumb_url", Get("jacket_thumb_url"));
                    SetParam(insertVideo, "$gallery_full_first", Get("gallery_full_first"));
                    SetParam(insertVideo, "$gallery_full_last", Get("gallery_full_last"));
                    SetParam(insertVideo, "$gallery_thumb_first", Get("gallery_thumb_first"));
                    SetParam(insertVideo, "$gallery_thumb_last", Get("gallery_thumb_last"));
                    SetParam(insertVideo, "$maker_id", Get("maker_id"));
                    SetParam(insertVideo, "$label_id", Get("label_id"));
                    SetParam(insertVideo, "$series_id", Get("series_id"));
                    videoCount += insertVideo.ExecuteNonQuery();
                    break;

                case "derived_actress":
                    SetParam(insertActress, "$id", Get("id"));
                    SetParam(insertActress, "$name_romaji", Get("name_romaji"));
                    SetParam(insertActress, "$name_kanji", Get("name_kanji"));
                    SetParam(insertActress, "$name_kana", Get("name_kana"));
                    SetParam(insertActress, "$image_url", Get("image_url"));
                    actressCount += insertActress.ExecuteNonQuery();
                    break;

                case "derived_video_actress":
                    SetParam(insertVideoActress, "$content_id", Get("content_id"));
                    SetParam(insertVideoActress, "$actress_id", Get("actress_id"));
                    SetParam(insertVideoActress, "$ordinality", Get("ordinality"));
                    insertVideoActress.ExecuteNonQuery();
                    break;

                case "derived_maker":
                    SetParam(insertMaker, "$id", Get("id"));
                    SetParam(insertMaker, "$name_en", Get("name_en"));
                    SetParam(insertMaker, "$name_ja", Get("name_ja"));
                    insertMaker.ExecuteNonQuery();
                    break;

                case "derived_label":
                    SetParam(insertLabel, "$id", Get("id"));
                    SetParam(insertLabel, "$name_en", Get("name_en"));
                    SetParam(insertLabel, "$name_ja", Get("name_ja"));
                    insertLabel.ExecuteNonQuery();
                    break;

                case "derived_series":
                    SetParam(insertSeries, "$id", Get("id"));
                    SetParam(insertSeries, "$name_en", Get("name_en"));
                    SetParam(insertSeries, "$name_ja", Get("name_ja"));
                    insertSeries.ExecuteNonQuery();
                    break;

                case "derived_director":
                    SetParam(insertDirector, "$id", Get("id"));
                    SetParam(insertDirector, "$name_romaji", Get("name_romaji"));
                    SetParam(insertDirector, "$name_kanji", Get("name_kanji"));
                    SetParam(insertDirector, "$name_kana", Get("name_kana"));
                    insertDirector.ExecuteNonQuery();
                    break;

                case "derived_video_director":
                    SetParam(insertVideoDirector, "$content_id", Get("content_id"));
                    SetParam(insertVideoDirector, "$director_id", Get("director_id"));
                    insertVideoDirector.ExecuteNonQuery();
                    break;

                case "derived_category":
                    SetParam(insertCategory, "$id", Get("id"));
                    SetParam(insertCategory, "$name_en", Get("name_en"));
                    SetParam(insertCategory, "$name_ja", Get("name_ja"));
                    insertCategory.ExecuteNonQuery();
                    break;

                case "derived_video_category":
                    SetParam(insertVideoCategory, "$content_id", Get("content_id"));
                    SetParam(insertVideoCategory, "$category_id", Get("category_id"));
                    insertVideoCategory.ExecuteNonQuery();
                    break;

                case "source_dmm_trailer":
                    SetParam(insertTrailer, "$content_id", Get("content_id"));
                    SetParam(insertTrailer, "$url", Get("url"));
                    insertTrailer.ExecuteNonQuery();
                    break;
            }

            if (++pendingInBatch >= batchSize)
            {
                await transaction.CommitAsync(ct);
                await transaction.DisposeAsync();
                transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
                foreach (var cmd in allCommands) cmd.Transaction = transaction;
                pendingInBatch = 0;
            }
        }

        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        return new RowCounts(videoCount, actressCount);
    }

    private static SqliteCommand PrepareInsert(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] paramNames)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var name in paramNames)
        {
            cmd.Parameters.Add(new SqliteParameter("$" + name, DbType.String));
        }
        cmd.Prepare();
        return cmd;
    }

    private static void SetParam(SqliteCommand cmd, string name, string? value) =>
        cmd.Parameters[name].Value = (object?)value ?? DBNull.Value;
}
