using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.R18Dev;

/// <summary>The catalog browse's store queries, against a dump built by the real importer
/// from a small synthetic pg_dump — so the facet columns and indexes R18DevCatalogFacets adds at import
/// are exercised along with the queries that read them.</summary>
[Collection(R18DevDumpImportCollection.Name)]
public class R18DevDumpStoreCatalogTests : IDisposable
{
    private readonly string tempDir;
    private readonly TestDbContextFactory dbFactory = new();
    private readonly IConfiguration configuration;

    public R18DevDumpStoreCatalogTests()
    {
        tempDir = Directory.CreateTempSubdirectory("r18devcatalogtest").FullName;
        configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(tempDir, "Test.db")}",
                ["R18Dev:DumpSourceOverride"] = Path.Combine(tempDir, "dump.sql"),
            })
            .Build();
    }

    public void Dispose()
    {
        dbFactory.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(tempDir, recursive: true);
    }

    // MIDE-002DOD is a distributor variant of MIDE-002; "Creampie" exists under two ids (as in the
    // real dump); SSIS-001 and VRKM-001 have no credited cast; OFJE-001 is in the Compilation category.
    private const string Dump = """
        COPY public.derived_maker (id, name_en) FROM stdin;
        1	Moodyz
        2	S1 NO.1 STYLE
        \.
        COPY public.derived_label (id, name_en) FROM stdin;
        10	Moodyz Label
        \.
        COPY public.derived_series (id, name_en) FROM stdin;
        20	Series A
        \.
        COPY public.derived_category (id, name_en) FROM stdin;
        100	Creampie
        101	VR
        102	Compilation
        103	Creampie
        104	Squirting
        105	Unused
        \.
        COPY public.derived_video (content_id, dvd_id, title_en, title_ja, release_date, maker_id, label_id, series_id, jacket_thumb_url) FROM stdin;
        mide00001	MIDE-001	Older Moodyz	\N	2020-01-01	1	10	20	digital/video/mide00001/mide00001ps
        mide00002	MIDE-002	Newer Moodyz	\N	2020-06-01	1	10	20	digital/video/mide00002/mide00002ps
        mide00002dod	MIDE-002DOD	Newer Moodyz DOD	\N	2020-06-01	1	10	20	\N
        midea00001	MIDEA-001	Different prefix, same letters	\N	2021-01-01	1	\N	\N	\N
        miaa00001	MIAA-001	Other Moodyz series	日本語のタイトル	2022-01-01	1	10	\N	\N
        ssis00001	SSIS-001	S1 release	\N	2021-03-01	2	\N	\N	\N
        vrkm00001	VRKM-001	A VR release	\N	2019-05-05	\N	\N	\N	\N
        ofje00001	OFJE-001	Best of	\N	2018-01-01	1	\N	\N	\N
        \.
        COPY public.derived_actress (id, name_romaji) FROM stdin;
        1	Actress One
        2	Actress Two
        \.
        COPY public.derived_video_actress (content_id, actress_id, ordinality) FROM stdin;
        mide00001	1	1
        mide00002	1	1
        mide00002	2	2
        miaa00001	2	1
        midea00001	1	1
        ofje00001	1	1
        \.
        COPY public.derived_video_category (content_id, category_id) FROM stdin;
        mide00001	100
        mide00002	103
        mide00002	104
        vrkm00001	101
        ofje00001	102
        \.
        """;

    private async Task<R18DevDumpStore> ImportAsync()
    {
        File.WriteAllText(Path.Combine(tempDir, "dump.sql"), Dump);
        var importer = new R18DevDumpImporter(Substitute.For<IHttpClientFactory>(), dbFactory, configuration);
        var result = await importer.ImportAsync(new ImmediateProgress<TaskProgress>(_ => { }));
        Assert.True(result.Success, result.ErrorMessage);
        return new R18DevDumpStore(configuration);
    }

    private static async Task<List<string>> CodesAsync(R18DevDumpStore store, R18DevCatalogFilter filter, R18DevCanonicalKeyFilter? keys = null) =>
        (await store.GetCatalogPageAsync(filter, keys, 0, 100)).Select(e => e.DvdId).ToList();

    [Fact]
    public async Task GetCatalogAvailabilityAsync_ImportedDump_IsReady()
    {
        var store = await ImportAsync();

        Assert.Equal(R18DevCatalogAvailability.Ready, await store.GetCatalogAvailabilityAsync());
    }

    [Fact]
    public async Task GetCatalogAvailabilityAsync_NoDump_IsNoDump()
    {
        var store = new R18DevDumpStore(configuration);

        Assert.Equal(R18DevCatalogAvailability.NoDump, await store.GetCatalogAvailabilityAsync());
    }

    [Fact]
    public async Task GetCatalogAvailabilityAsync_DumpWithoutCatalogVersion_NeedsReimport()
    {
        await ImportAsync();
        using (var connection = new SqliteConnection($"Data Source={R18DevDumpPaths.GetDbPath(configuration)}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE dump_meta SET catalog_version = NULL";
            cmd.ExecuteNonQuery();
        }
        // The availability is cached per file write time, which the UPDATE above may not move on a
        // coarse-grained file system.
        File.SetLastWriteTimeUtc(R18DevDumpPaths.GetDbPath(configuration), DateTime.UtcNow.AddMinutes(1));

        var store = new R18DevDumpStore(configuration);

        Assert.Equal(R18DevCatalogAvailability.NeedsReimport, await store.GetCatalogAvailabilityAsync());
    }

    [Fact]
    public async Task GetCatalogPageAsync_NoFilter_ReturnsTheWholeCatalogNewestFirstWithoutDistributorVariants()
    {
        var store = await ImportAsync();

        var codes = await CodesAsync(store, new R18DevCatalogFilter());

        Assert.Equal(new[] { "MIAA-001", "SSIS-001", "MIDEA-001", "MIDE-002", "MIDE-001", "VRKM-001", "OFJE-001" }, codes);
    }

    [Fact]
    public async Task GetCatalogPageAsync_OffsetAndLimit_PageThroughInOrder()
    {
        var store = await ImportAsync();

        var first = await store.GetCatalogPageAsync(new R18DevCatalogFilter(), null, 0, 3);
        var second = await store.GetCatalogPageAsync(new R18DevCatalogFilter(), null, 3, 3);

        Assert.Equal(new[] { "MIAA-001", "SSIS-001", "MIDEA-001" }, first.Select(e => e.DvdId));
        Assert.Equal(new[] { "MIDE-002", "MIDE-001", "VRKM-001" }, second.Select(e => e.DvdId));
    }

    [Fact]
    public async Task GetCatalogPageAsync_CodePrefix_MatchesOnlyThatPrefix()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "MIDE-002", "MIDE-001" }, await CodesAsync(store, new R18DevCatalogFilter { CodePrefix = "mide-" }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_SearchText_MatchesCodeFragmentsAndBothTitles()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "MIDE-002", "MIDE-001" }, await CodesAsync(store, new R18DevCatalogFilter { SearchText = "ide-00" }));
        Assert.Equal(new[] { "SSIS-001" }, await CodesAsync(store, new R18DevCatalogFilter { SearchText = "s1 RELEASE" }));
        Assert.Equal(new[] { "MIAA-001" }, await CodesAsync(store, new R18DevCatalogFilter { SearchText = "タイトル" }));
        Assert.Empty(await CodesAsync(store, new R18DevCatalogFilter { SearchText = "%" }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_Studios_MatchMakerNamesCaseInsensitively()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "SSIS-001" }, await CodesAsync(store, new R18DevCatalogFilter { Studios = ["s1 no.1 style"] }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_Genres_MatchAnySelectedIncludingDuplicateNamedCategories()
    {
        var store = await ImportAsync();

        var codes = await CodesAsync(store, new R18DevCatalogFilter { Genres = ["Creampie", "VR"] });

        Assert.Equal(new[] { "MIDE-002", "MIDE-001", "VRKM-001" }, codes);
    }

    [Fact]
    public async Task GetCatalogPageAsync_MatchAllGenres_NeedsEverySelectedGenre()
    {
        var store = await ImportAsync();

        var codes = await CodesAsync(store, new R18DevCatalogFilter { Genres = ["Creampie", "Squirting"], MatchAllGenres = true });

        Assert.Equal(new[] { "MIDE-002" }, codes);
    }

    [Fact]
    public async Task GetCatalogPageAsync_YearRange_IsInclusiveOfWholeYears()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "MIDE-002", "MIDE-001" }, await CodesAsync(store, new R18DevCatalogFilter { YearFrom = 2020, YearTo = 2020 }));
        Assert.Equal(new[] { "MIAA-001", "SSIS-001", "MIDEA-001" }, await CodesAsync(store, new R18DevCatalogFilter { YearFrom = 2021 }));
        Assert.Equal(new[] { "VRKM-001", "OFJE-001" }, await CodesAsync(store, new R18DevCatalogFilter { YearTo = 2019 }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_VrAndCompilationFlags_IncludeOrExclude()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "VRKM-001" }, await CodesAsync(store, new R18DevCatalogFilter { Vr = R18DevCatalogFlag.Only }));
        Assert.DoesNotContain("VRKM-001", await CodesAsync(store, new R18DevCatalogFilter { Vr = R18DevCatalogFlag.Exclude }));
        Assert.Equal(new[] { "OFJE-001" }, await CodesAsync(store, new R18DevCatalogFilter { Compilation = R18DevCatalogFlag.Only }));
        Assert.Equal(6, (await CodesAsync(store, new R18DevCatalogFilter { Compilation = R18DevCatalogFlag.Exclude })).Count);
    }

    [Fact]
    public async Task GetCatalogPageAsync_CastSizes_KeepUnknownCastApartFromSolo()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "MIAA-001", "MIDEA-001", "MIDE-001", "OFJE-001" }, await CodesAsync(store, new R18DevCatalogFilter { CastSizes = [R18DevCastSize.Solo] }));
        Assert.Equal(new[] { "MIDE-002" }, await CodesAsync(store, new R18DevCatalogFilter { CastSizes = [R18DevCastSize.Multiple] }));
        Assert.Equal(new[] { "SSIS-001", "VRKM-001" }, await CodesAsync(store, new R18DevCatalogFilter { CastSizes = [R18DevCastSize.Unknown] }));
        Assert.Equal(new[] { "SSIS-001", "MIDE-002", "VRKM-001" }, await CodesAsync(store, new R18DevCatalogFilter { CastSizes = [R18DevCastSize.Multiple, R18DevCastSize.Unknown] }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_SeriesAndLabel_MatchByDumpId()
    {
        var store = await ImportAsync();

        Assert.Equal(new[] { "MIDE-002", "MIDE-001" }, await CodesAsync(store, new R18DevCatalogFilter { Series = new R18DevCatalogRef(20, "Series A") }));
        Assert.Equal(new[] { "MIAA-001", "MIDE-002", "MIDE-001" }, await CodesAsync(store, new R18DevCatalogFilter { Label = new R18DevCatalogRef(10, "Moodyz Label") }));
    }

    [Fact]
    public async Task GetCatalogPageAsync_KeyFilter_IncludesOrExcludesCanonicalCodes()
    {
        var store = await ImportAsync();
        var keys = new[] { CodeNormalization.GetCanonicalKey("MIDE-002"), CodeNormalization.GetCanonicalKey("SSIS-001") };

        Assert.Equal(new[] { "SSIS-001", "MIDE-002" }, await CodesAsync(store, new R18DevCatalogFilter(), new R18DevCanonicalKeyFilter(keys, Exclude: false)));
        Assert.Equal(new[] { "MIAA-001", "MIDEA-001", "MIDE-001", "VRKM-001", "OFJE-001" }, await CodesAsync(store, new R18DevCatalogFilter(), new R18DevCanonicalKeyFilter(keys, Exclude: true)));
    }

    [Fact]
    public async Task CountCatalogAsync_CountsDistinctReleasesMatchingTheFilter()
    {
        var store = await ImportAsync();

        Assert.Equal(7, await store.CountCatalogAsync(new R18DevCatalogFilter(), null));
        Assert.Equal(2, await store.CountCatalogAsync(new R18DevCatalogFilter { CodePrefix = "MIDE" }, null));
        Assert.Equal(1, await store.CountCatalogAsync(new R18DevCatalogFilter { CodePrefix = "MIDE" }, new R18DevCanonicalKeyFilter([CodeNormalization.GetCanonicalKey("MIDE-001")], Exclude: true)));
    }

    [Fact]
    public async Task GetCategoriesAsync_MergesDuplicateNamesAndSkipsUnusedCategories()
    {
        var store = await ImportAsync();

        var categories = await store.GetCategoriesAsync();

        Assert.Equal(new[] { "Compilation", "Creampie", "Squirting", "VR" }, categories.Select(c => c.Name));
        Assert.Equal(2, categories.Single(c => c.Name == "Creampie").VideoCount);
    }

    [Fact]
    public async Task GetReleaseYearBoundsAsync_ReturnsOldestAndNewestYears()
    {
        var store = await ImportAsync();

        Assert.Equal((2018, 2022), await store.GetReleaseYearBoundsAsync());
    }

    [Fact]
    public async Task SuggestAsync_ExactCodeFirstThenNewestPrefixMatches()
    {
        var store = await ImportAsync();

        var exact = await store.SuggestAsync("mide-001", 5);
        var prefix = await store.SuggestAsync("MIDE", 5);

        Assert.Equal("MIDE-001", exact[0].DvdId);
        Assert.Equal(new[] { "MIDEA-001", "MIDE-002", "MIDE-001" }, prefix.Select(e => e.DvdId));
        Assert.Empty(await store.SuggestAsync("m", 5));
    }

    [Fact]
    public async Task GetMovieByCodeAsync_ReturnsSeriesAndLabelIds()
    {
        var store = await ImportAsync();

        var movie = await store.GetMovieByCodeAsync("MIDE-001");

        Assert.NotNull(movie);
        Assert.Equal(new R18DevCatalogRef(20, "Series A"), movie.SeriesRef);
        Assert.Equal(new R18DevCatalogRef(10, "Moodyz Label"), movie.LabelRef);
    }
}
