using System.Text;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.R18Dev;

/// <summary>The old browse capped raw dump rows before collapsing them, so duplicate,
/// retailer-suffixed and distributor-prefixed rows used up the cap and a series search stopped partway
/// (MIDE ended around MIDE-511). Pages through a dump built by the real importer, where every release
/// has those extra rows, with the real store and browse service the way "Show more" does.</summary>
[Collection(R18DevDumpImportCollection.Name)]
public class R18DevCatalogPagingTests : IDisposable
{
    private const int Releases = 250;

    private readonly string tempDir;
    private readonly TestDbContextFactory dbFactory = new();
    private readonly IConfiguration configuration;

    public R18DevCatalogPagingTests()
    {
        tempDir = Directory.CreateTempSubdirectory("r18devpagingtest").FullName;
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

    // Each ABC-NNN release is in the dump four times, as MIDE releases are in the real one: under a
    // second content id, as a retailer-suffixed ABC-NNNBOD and as a distributor-prefixed 4ABC-NNN.
    // 1,000 rows, of which the two plain copies alone are more than two pages.
    private async Task<R18DevReleaseBrowseService> ImportAsync()
    {
        var dump = new StringBuilder("COPY public.derived_video (content_id, dvd_id, release_date) FROM stdin;\n");
        for (var i = 1; i <= Releases; i++)
        {
            var date = new DateOnly(2000, 1, 1).AddDays(i).ToString("yyyy-MM-dd");
            dump.Append($"abc{i:D3}\tABC-{i:D3}\t{date}\n");
            dump.Append($"abc00{i:D3}\tABC-{i:D3}\t{date}\n");
            dump.Append($"abc{i:D3}bod\tABC-{i:D3}BOD\t{date}\n");
            dump.Append($"4abc{i:D3}\t4ABC-{i:D3}\t{date}\n");
        }
        dump.Append("\\.\n");
        File.WriteAllText(Path.Combine(tempDir, "dump.sql"), dump.ToString());

        var importer = new R18DevDumpImporter(Substitute.For<IHttpClientFactory>(), dbFactory, configuration);
        var result = await importer.ImportAsync(new ImmediateProgress<TaskProgress>(_ => { }));
        Assert.True(result.Success, result.ErrorMessage);

        using (var db = dbFactory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.Movies.Add(new Movie { Code = "ABC-100", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        return new R18DevReleaseBrowseService(dbFactory, new R18DevDumpStore(configuration), TimeProvider.System);
    }

    private static async Task<List<R18DevReleaseRow>> BrowseEveryPageAsync(R18DevReleaseBrowseService service, R18DevCatalogFilter filter)
    {
        var rows = new List<R18DevReleaseRow>();
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        while (true)
        {
            var page = await service.BrowseAsync(filter, offset, shown);
            rows.AddRange(page.Rows);
            shown.UnionWith(page.Rows.Select(r => r.CanonicalKey));
            if (!page.HasMore) return rows;
            offset = page.NextOffset;
        }
    }

    private static IEnumerable<string> NewestFirst() => Enumerable.Range(1, Releases).Reverse().Select(i => $"ABC-{i:D3}");

    [Theory]
    [InlineData("ABC", null)]
    [InlineData(null, "ABC")]
    public async Task SeriesSearch_PagesThroughEveryReleaseOnceDownToTheOldest(string? codePrefix, string? searchText)
    {
        var service = await ImportAsync();
        var filter = new R18DevCatalogFilter { CodePrefix = codePrefix, SearchText = searchText };

        var rows = await BrowseEveryPageAsync(service, filter);
        var counts = await service.CountAsync(filter);

        Assert.Equal(NewestFirst(), rows.Select(r => r.Movie.DvdId));
        Assert.Equal("got", rows.Single(r => r.Movie.DvdId == "ABC-100").StatusClass);
        Assert.Equal(new R18DevCatalogCounts(Releases, Releases - 1, 0, 1), counts);
    }

    [Fact]
    public async Task UntrackedSearch_SkipsOnlyTheTrackedRelease()
    {
        var service = await ImportAsync();

        var rows = await BrowseEveryPageAsync(service, new R18DevCatalogFilter { SearchText = "ABC", Status = R18DevCatalogStatus.Untracked });

        Assert.Equal(NewestFirst().Where(code => code != "ABC-100"), rows.Select(r => r.Movie.DvdId));
    }
}
