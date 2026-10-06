using Javbuddy.Models;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentHistoryServiceTests
{
    private static async Task SeedAsync(TestDbContextFactory factory, params (string Title, DateTime GrabbedAt, bool Finished)[] rows)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "AAA-1", Status = MovieStatus.Missing };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        foreach (var (title, grabbedAt, finished) in rows)
        {
            db.TorrentDownloads.Add(new TorrentDownload
            {
                MovieId = movie.Id,
                MovieCode = movie.Code,
                ReleaseTitle = title,
                GrabbedAt = grabbedAt,
                RemovedFromClientAt = finished ? grabbedAt : null,
            });
        }
        await db.SaveChangesAsync();
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPageAsync_EmptyHistory_ReturnsOneEmptyPage()
    {
        using var factory = new TestDbContextFactory();

        var page = await new TorrentHistoryService(factory).GetPageAsync(1, 10);

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task GetPageAsync_ExcludesActiveDownloads_AndPagesNewestFirstCoveringEveryEntryOnce()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory,
            ("e1", T0.AddMinutes(1), true), ("e2", T0.AddMinutes(2), true), ("e3", T0.AddMinutes(3), true),
            ("e4", T0.AddMinutes(4), true), ("e5", T0.AddMinutes(5), true), ("active", T0.AddMinutes(9), false));
        var service = new TorrentHistoryService(factory);

        var first = await service.GetPageAsync(1, 2);
        var second = await service.GetPageAsync(2, 2);
        var third = await service.GetPageAsync(3, 2);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(["e5", "e4"], first.Rows.Select(r => r.ReleaseTitle));
        Assert.Equal(["e3", "e2"], second.Rows.Select(r => r.ReleaseTitle));
        Assert.Equal(["e1"], third.Rows.Select(r => r.ReleaseTitle));
    }

    [Fact]
    public async Task GetPageAsync_EqualTimestamps_PageDeterministicallyByIdDescending()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory, ("a", T0, true), ("b", T0, true), ("c", T0, true), ("d", T0, true));
        var service = new TorrentHistoryService(factory);

        var titles = (await service.GetPageAsync(1, 2)).Rows.Concat((await service.GetPageAsync(2, 2)).Rows).Select(r => r.ReleaseTitle);

        Assert.Equal(["d", "c", "b", "a"], titles);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(99, 2)]
    public async Task GetPageAsync_OutOfRangePage_IsClampedToAValidPage(int requested, int expected)
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory, ("a", T0, true), ("b", T0.AddMinutes(1), true), ("c", T0.AddMinutes(2), true));

        var page = await new TorrentHistoryService(factory).GetPageAsync(requested, 2);

        Assert.Equal(expected, page.Page);
        Assert.NotEmpty(page.Rows);
    }
}
