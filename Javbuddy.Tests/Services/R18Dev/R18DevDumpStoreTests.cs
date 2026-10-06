using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.R18Dev;

/// <summary>Exercises GetFilmographyForActorAsync against a real (temp-file) sidecar SQLite
/// database shaped like R18DevDumpImporter's schema — the actress-identity bootstrap logic runs
/// hand-written SQL directly against that file, so an in-memory EF context can't stand in for it.</summary>
public class R18DevDumpStoreTests : IDisposable
{
    private readonly string tempDir;
    private readonly IConfiguration configuration;

    public R18DevDumpStoreTests()
    {
        tempDir = Directory.CreateTempSubdirectory("r18devtest").FullName;
        var mainDbPath = Path.Combine(tempDir, "Test.db");
        var sidecarDbPath = Path.Combine(tempDir, "Test.r18dev.db");

        configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={mainDbPath}"
            })
            .Build();

        Assert.Equal(sidecarDbPath, R18DevDumpPaths.GetDbPath(configuration));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(tempDir, recursive: true);
    }

    private string SidecarDbPath => R18DevDumpPaths.GetDbPath(configuration);

    private SqliteConnection OpenSidecarForSeeding()
    {
        var connection = new SqliteConnection($"Data Source={SidecarDbPath}");
        connection.Open();
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
            CREATE TABLE actresses (id INTEGER PRIMARY KEY, name_romaji TEXT, name_kanji TEXT);
            CREATE TABLE video_actresses (content_id TEXT NOT NULL, actress_id INTEGER NOT NULL, ordinality INTEGER, PRIMARY KEY (content_id, actress_id));
            CREATE TABLE directors (id INTEGER PRIMARY KEY, name_romaji TEXT, name_kanji TEXT);
            CREATE TABLE video_directors (content_id TEXT NOT NULL, director_id INTEGER NOT NULL, PRIMARY KEY (content_id, director_id));
            CREATE TABLE makers (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE labels (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE series (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE categories (id INTEGER PRIMARY KEY, name_en TEXT, name_ja TEXT);
            CREATE TABLE video_categories (content_id TEXT NOT NULL, category_id INTEGER NOT NULL, PRIMARY KEY (content_id, category_id));
            CREATE TABLE trailers (content_id TEXT PRIMARY KEY, url TEXT);
            """;
        cmd.ExecuteNonQuery();
        return connection;
    }

    private static void InsertActress(SqliteConnection connection, int id, string nameRomaji)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO actresses (id, name_romaji) VALUES ($id, $name)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", nameRomaji);
        cmd.ExecuteNonQuery();
    }

    private static void InsertVideo(SqliteConnection connection, string dvdId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO videos (content_id, dvd_id, dvd_id_norm) VALUES ($id, $dvdId, $norm)";
        cmd.Parameters.AddWithValue("$id", dvdId);
        cmd.Parameters.AddWithValue("$dvdId", dvdId);
        cmd.Parameters.AddWithValue("$norm", CodeNormalization.Normalize(dvdId));
        cmd.ExecuteNonQuery();
    }

    private static void LinkActress(SqliteConnection connection, string contentId, int actressId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO video_actresses (content_id, actress_id) VALUES ($contentId, $actressId)";
        cmd.Parameters.AddWithValue("$contentId", contentId);
        cmd.Parameters.AddWithValue("$actressId", actressId);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task GetFilmographyForActorAsync_LinkedMovieIsACollab_DoesNotPullInCoStarsUnrelatedFilmography()
    {
        using (var seed = OpenSidecarForSeeding())
        {
            InsertActress(seed, 1, "Miyashita Lena");
            InsertActress(seed, 2, "Some Other");

            // The user's one locally-linked movie for "Miyashita Lena" happens to be a collab —
            // both actresses are credited on it in the dump.
            InsertVideo(seed, "COLLAB-001");
            LinkActress(seed, "COLLAB-001", 1);
            LinkActress(seed, "COLLAB-001", 2);

            // Miyashita Lena's own solo work — not locally owned, should still surface as "missing".
            InsertVideo(seed, "SOLO-001");
            LinkActress(seed, "SOLO-001", 1);

            // The co-star's own unrelated solo work — must NOT leak into Miyashita Lena's filmography.
            InsertVideo(seed, "OTHER-001");
            LinkActress(seed, "OTHER-001", 2);
        }

        var store = new R18DevDumpStore(configuration);
        var result = await store.GetFilmographyForActorAsync(
            linkedMovieCodes: ["COLLAB-001"],
            actorName: "Miyashita Lena",
            r18DevNameOverride: null);

        Assert.True(result.DumpAvailable);
        var codes = result.Movies.Select(m => m.DvdId).ToList();
        Assert.Contains("COLLAB-001", codes);
        Assert.Contains("SOLO-001", codes);
        Assert.DoesNotContain("OTHER-001", codes);
    }

    [Fact]
    public async Task GetFilmographyForActorAsync_SoloLinkedMovie_StillFindsFullFilmography()
    {
        using (var seed = OpenSidecarForSeeding())
        {
            InsertActress(seed, 1, "Miyashita Lena");

            InsertVideo(seed, "SOLO-001");
            LinkActress(seed, "SOLO-001", 1);

            InsertVideo(seed, "SOLO-002");
            LinkActress(seed, "SOLO-002", 1);
        }

        var store = new R18DevDumpStore(configuration);
        var result = await store.GetFilmographyForActorAsync(
            linkedMovieCodes: ["SOLO-001"],
            actorName: "Miyashita Lena",
            r18DevNameOverride: null);

        Assert.True(result.DumpAvailable);
        Assert.Equal(["SOLO-001", "SOLO-002"], result.Movies.Select(m => m.DvdId).OrderBy(c => c));
    }

    [Fact]
    public async Task GetFilmographyForActorAsync_AmbiguousCollabWithNoNameMatch_FallsBackToFullBootstrapSet()
    {
        using (var seed = OpenSidecarForSeeding())
        {
            // Neither actress's dump name textually matches actorName below (simulating a
            // romanization the name-order heuristic can't bridge) — with no way to disambiguate,
            // the bootstrap set should still be returned rather than coming back empty.
            InsertActress(seed, 1, "Completely Different Spelling");
            InsertActress(seed, 2, "Another Different Spelling");

            InsertVideo(seed, "COLLAB-001");
            LinkActress(seed, "COLLAB-001", 1);
            LinkActress(seed, "COLLAB-001", 2);
        }

        var store = new R18DevDumpStore(configuration);
        var result = await store.GetFilmographyForActorAsync(
            linkedMovieCodes: ["COLLAB-001"],
            actorName: "Miyashita Lena",
            r18DevNameOverride: null);

        Assert.True(result.DumpAvailable);
        Assert.Single(result.Movies);
        Assert.Equal("COLLAB-001", result.Movies[0].DvdId);
    }

    [Fact]
    public void DeleteDump_DumpAndJournalExist_DeletesBoth()
    {
        using (OpenSidecarForSeeding()) { }
        File.WriteAllText(SidecarDbPath + "-journal", string.Empty);

        var store = new R18DevDumpStore(configuration);
        Assert.True(store.IsAvailable());

        store.DeleteDump();

        Assert.False(store.IsAvailable());
        Assert.False(File.Exists(SidecarDbPath + "-journal"));
    }

    [Fact]
    public void DeleteDump_NoDumpPresent_DoesNotThrow()
    {
        var store = new R18DevDumpStore(configuration);
        Assert.False(store.IsAvailable());

        var exception = Record.Exception(() => store.DeleteDump());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("digital/video/mgmj00077/mgmj00077-1", "digital/video/mgmj00077/mgmj00077jp-1")]
    [InlineData("digital/video/mgmj00077/mgmj00077-20", "digital/video/mgmj00077/mgmj00077jp-20")]
    [InlineData("digital/video/mgmj00012/mgmj00012jp-1", "digital/video/mgmj00012/mgmj00012jp-1")]
    [InlineData("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-1.jpg", "https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg", "https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg")]
    [InlineData("digital/video/433mbd00160-1/433mbd00160-1-1", "digital/video/433mbd00160-1/433mbd00160-1jp-1")]
    [InlineData("digital/amateur/bzdc020/bzdc020js-001", "digital/amateur/bzdc020/bzdc020jp-001")]
    [InlineData("digital/amateur/bzdc020/bzdc020jp-001", "digital/amateur/bzdc020/bzdc020jp-001")]
    [InlineData("other/sample-1", "other/sample-1")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeFullGalleryPath_NormalizesDmmGalleryThumbPathsToFullPreviewVariant(string? input, string? expected)
    {
        var result = R18DevDumpStore.NormalizeFullGalleryPath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetMovieByCodeAsync_DmmDigitalVideoWithThumbPathInFullRange_NormalizesGalleryUrlsAndPopulatesThumbs()
    {
        using (var seed = OpenSidecarForSeeding())
        {
            using var cmd = seed.CreateCommand();
            cmd.CommandText = """
                INSERT INTO videos (content_id, dvd_id, dvd_id_norm, gallery_full_first, gallery_full_last, gallery_thumb_first, gallery_thumb_last)
                VALUES ($cid, $dvd, $norm, $gff, $gfl, $gtf, $gtl);
                """;
            cmd.Parameters.AddWithValue("$cid", "mgmj077");
            cmd.Parameters.AddWithValue("$dvd", "MGMJ-077");
            cmd.Parameters.AddWithValue("$norm", CodeNormalization.Normalize("MGMJ-077"));
            cmd.Parameters.AddWithValue("$gff", "digital/video/mgmj00077/mgmj00077-1");
            cmd.Parameters.AddWithValue("$gfl", "digital/video/mgmj00077/mgmj00077-3");
            cmd.Parameters.AddWithValue("$gtf", "digital/video/mgmj00077/mgmj00077-1");
            cmd.Parameters.AddWithValue("$gtl", "digital/video/mgmj00077/mgmj00077-3");
            cmd.ExecuteNonQuery();
        }

        var store = new R18DevDumpStore(configuration);
        var movie = await store.GetMovieByCodeAsync("MGMJ-077");

        Assert.NotNull(movie);
        Assert.Equal(3, movie.GalleryUrls.Count);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg", movie.GalleryUrls[0]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-2.jpg", movie.GalleryUrls[1]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-3.jpg", movie.GalleryUrls[2]);

        Assert.NotNull(movie.GalleryThumbUrls);
        Assert.Equal(3, movie.GalleryThumbUrls.Count);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-1.jpg", movie.GalleryThumbUrls[0]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-2.jpg", movie.GalleryThumbUrls[1]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-3.jpg", movie.GalleryThumbUrls[2]);
    }

    [Fact]
    public async Task GetMovieByCodeAsync_MissingFullRange_FallsBackToNormalizedThumbRange()
    {
        using (var seed = OpenSidecarForSeeding())
        {
            using var cmd = seed.CreateCommand();
            cmd.CommandText = """
                INSERT INTO videos (content_id, dvd_id, dvd_id_norm, gallery_full_first, gallery_full_last, gallery_thumb_first, gallery_thumb_last)
                VALUES ($cid, $dvd, $norm, NULL, NULL, $gtf, $gtl);
                """;
            cmd.Parameters.AddWithValue("$cid", "mgmj077");
            cmd.Parameters.AddWithValue("$dvd", "MGMJ-077");
            cmd.Parameters.AddWithValue("$norm", CodeNormalization.Normalize("MGMJ-077"));
            cmd.Parameters.AddWithValue("$gtf", "digital/video/mgmj00077/mgmj00077-1");
            cmd.Parameters.AddWithValue("$gtl", "digital/video/mgmj00077/mgmj00077-2");
            cmd.ExecuteNonQuery();
        }

        var store = new R18DevDumpStore(configuration);
        var movie = await store.GetMovieByCodeAsync("MGMJ-077");

        Assert.NotNull(movie);
        Assert.Equal(2, movie.GalleryUrls.Count);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg", movie.GalleryUrls[0]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-2.jpg", movie.GalleryUrls[1]);

        Assert.NotNull(movie.GalleryThumbUrls);
        Assert.Equal(2, movie.GalleryThumbUrls.Count);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-1.jpg", movie.GalleryThumbUrls[0]);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-2.jpg", movie.GalleryThumbUrls[1]);
    }
}
