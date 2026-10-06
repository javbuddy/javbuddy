using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.E2ETests.Support;

/// <summary>Writes a tiny r18.dev sidecar dump (the file <see cref="R18DevDumpStore"/> reads,
/// shaped like R18DevDumpImporter's schema) into the running app's data directory, so a flow test
/// can browse "all releases" without the real multi-GB import. Every table the importer creates is
/// here, since Movie Detail now looks a tracked movie up in the dump too (its related releases). The file's mere existence is what
/// flips <see cref="IR18DevDumpStore.IsAvailable"/>, and other suites assert the not-imported
/// state — so callers must <see cref="Delete"/> it when done.</summary>
public static class R18DevDumpSeeding
{
    public sealed record Release(string DvdId, string MakerName, string ReleaseDate, string Title);

    public static string PathFor(IServiceProvider appServices) =>
        R18DevDumpPaths.GetDbPath(appServices.GetRequiredService<IConfiguration>());

    public static void Write(IServiceProvider appServices, IEnumerable<Release> releases)
    {
        using var connection = new SqliteConnection($"Data Source={PathFor(appServices)}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE videos (
                    content_id TEXT PRIMARY KEY, dvd_id TEXT, dvd_id_norm TEXT, title_en TEXT, title_ja TEXT,
                    comment_en TEXT, comment_ja TEXT, runtime_mins INTEGER, release_date TEXT,
                    jacket_full_url TEXT, jacket_thumb_url TEXT, gallery_full_first TEXT, gallery_full_last TEXT,
                    gallery_thumb_first TEXT, gallery_thumb_last TEXT, maker_id INTEGER, label_id INTEGER, series_id INTEGER
                );
                CREATE INDEX idx_videos_dvd_id_norm ON videos(dvd_id_norm);
                CREATE TABLE actresses (id INTEGER PRIMARY KEY, name_romaji TEXT, name_kanji TEXT);
                CREATE TABLE video_actresses (content_id TEXT NOT NULL, actress_id INTEGER NOT NULL, ordinality INTEGER, PRIMARY KEY (content_id, actress_id));
                CREATE TABLE makers (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
                CREATE TABLE categories (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
                CREATE TABLE video_categories (content_id TEXT NOT NULL, category_id INTEGER NOT NULL, PRIMARY KEY (content_id, category_id));
                CREATE TABLE directors (id INTEGER PRIMARY KEY, name_romaji TEXT, name_kanji TEXT, name_kana TEXT);
                CREATE TABLE video_directors (content_id TEXT NOT NULL, director_id INTEGER NOT NULL, PRIMARY KEY (content_id, director_id));
                CREATE TABLE trailers (content_id TEXT PRIMARY KEY, url TEXT);
                CREATE TABLE labels (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
                CREATE TABLE series (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
                CREATE TABLE dump_meta (id INTEGER PRIMARY KEY CHECK (id = 1), source TEXT NOT NULL, source_date TEXT, imported_at TEXT NOT NULL);
                INSERT INTO dump_meta (id, source, imported_at) VALUES (1, 'e2e', '2026-01-01T00:00:00Z');
                """;
            create.ExecuteNonQuery();
        }

        var makerIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in releases)
        {
            if (!makerIds.TryGetValue(release.MakerName, out var makerId))
            {
                makerId = makerIds.Count + 1;
                makerIds[release.MakerName] = makerId;
                using var maker = connection.CreateCommand();
                maker.CommandText = "INSERT INTO makers (id, name_en) VALUES ($id, $name)";
                maker.Parameters.AddWithValue("$id", makerId);
                maker.Parameters.AddWithValue("$name", release.MakerName);
                maker.ExecuteNonQuery();
            }

            using var video = connection.CreateCommand();
            video.CommandText = "INSERT INTO videos (content_id, dvd_id, dvd_id_norm, title_en, release_date, maker_id) VALUES ($content, $dvd, $norm, $title, $date, $maker)";
            video.Parameters.AddWithValue("$content", release.DvdId.ToLowerInvariant());
            video.Parameters.AddWithValue("$dvd", release.DvdId);
            video.Parameters.AddWithValue("$norm", CodeNormalization.Normalize(release.DvdId));
            video.Parameters.AddWithValue("$title", release.Title);
            video.Parameters.AddWithValue("$date", release.ReleaseDate);
            video.Parameters.AddWithValue("$maker", makerId);
            video.ExecuteNonQuery();
        }

        // What the importer adds once the rows are in; the catalog browse needs it.
        R18DevCatalogFacets.Build(connection);
    }

    public static void Delete(IServiceProvider appServices)
    {
        SqliteConnection.ClearAllPools();
        var path = PathFor(appServices);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Adds an enabled r18.dev settings row and returns its id, so a test can remove
    /// exactly that row again with <see cref="RemoveSourceAsync"/>.</summary>
    public static async Task<int> EnableSourceAsync(IDbContextFactory<AppDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = new R18DevSettings { Enabled = true };
        db.R18DevSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings.Id;
    }

    public static async Task RemoveSourceAsync(IDbContextFactory<AppDbContext> dbFactory, int settingsId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.R18DevSettings.Where(s => s.Id == settingsId).ExecuteDeleteAsync();
    }
}
