using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Trickplay;

public sealed class TrickplayStoreTests : IDisposable
{
    private const string Identity = "0123456789abcdef";

    private readonly TestDbContextFactory factory = new();
    private readonly InMemoryObjectStoreProvider stores = new();

    public void Dispose() => factory.Dispose();

    private InMemoryObjectStore Objects => stores.Get(ObjectStoreArea.Trickplay);

    private TrickplayStore Store() => new(factory, stores);

    private static TrickplaySet NewSet(string identity = Identity) => new()
    {
        Identity = identity,
        Width = 320,
        Height = 180,
        TileWidth = 10,
        TileHeight = 10,
        ThumbnailCount = 150,
        IntervalMs = 10000,
        DurationSeconds = 1500,
        GeneratedAt = DateTime.UtcNow,
        FileName = "ABC-123.mp4",
    };

    private static List<Func<Stream>> Sheets(params byte[][] sheets) =>
        sheets.Select(bytes => (Func<Stream>)(() => new MemoryStream(bytes))).ToList();

    [Fact]
    public void Identity_SurvivesARemux_ButNotAReplacement()
    {
        var original = TrickplayIdentity.For("ABC-123.mp4", 7127.784, 1920, 1080);

        // A chapter write or repair remux: same name, frames and resolution, duration off by ms.
        Assert.Equal(original, TrickplayIdentity.For("abc-123.MP4", 7127.9, 1920, 1080));
        Assert.NotEqual(original, TrickplayIdentity.For("ABC-123.mp4", 7127.784, 3840, 2160));
        Assert.NotEqual(original, TrickplayIdentity.For("ABC-123.mp4", 6000, 1920, 1080));
        Assert.NotEqual(original, TrickplayIdentity.For("ABC-123.mkv", 7127.784, 1920, 1080));
        Assert.True(TrickplayIdentity.IsValid(original));
    }

    [Theory]
    [InlineData(null, 1920, 1080)]
    [InlineData(7127.0, null, 1080)]
    [InlineData(0.0, 1920, 1080)]
    public void Identity_NeedsAProbedDurationAndResolution(double? duration, int? width, int? height) =>
        Assert.Null(TrickplayIdentity.For("ABC-123.mp4", duration, width, height));

    [Theory]
    [InlineData("0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF", false)]
    [InlineData("0123456789abcde", false)]
    [InlineData("../../../../etc/", false)]
    [InlineData(null, false)]
    public void IsValid_AcceptsOnlyTheHashShape(string? identity, bool expected) =>
        Assert.Equal(expected, TrickplayIdentity.IsValid(identity));

    [Fact]
    public void CodeFolder_IsFilenameSafe() =>
        Assert.Equal("A_B 1", TrickplayStore.CodeFolder("a/b 1"));

    [Fact]
    public async Task SaveAsync_WritesTheSheetsThenTheRow()
    {
        var store = Store();

        await store.SaveAsync("abc-123", NewSet(), Sheets([1], [2]));

        Assert.Equal(["ABC-123/0123456789abcdef/0.webp", "ABC-123/0123456789abcdef/1.webp"], Objects.Keys.Order());
        var set = await store.GetSetAsync("ABC-123", Identity);
        Assert.NotNull(set);
        Assert.Equal("ABC-123", set.CodeFolder);
        Assert.Equal(150, set.ThumbnailCount);
        await using var sheet = await store.OpenSheetAsync("abc-123", Identity, 1);
        Assert.NotNull(sheet);
        Assert.Equal(2, sheet.Content.ReadByte());
        Assert.Null(await store.OpenSheetAsync("abc-123", Identity, 2));
    }

    [Fact]
    public async Task SaveAsync_ReplacesAnExistingSetAndItsSheets()
    {
        var store = Store();
        await store.SaveAsync("ABC-123", NewSet(), Sheets([1], [2], [3]));

        var replacement = NewSet();
        replacement.ThumbnailCount = 50;
        await store.SaveAsync("ABC-123", replacement, Sheets([9]));

        Assert.Equal(["ABC-123/0123456789abcdef/0.webp"], Objects.Keys);
        Assert.Equal([9], Objects["ABC-123/0123456789abcdef/0.webp"]);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(50, (await db.TrickplaySets.SingleAsync()).ThumbnailCount);
    }

    [Fact]
    public async Task SheetsWithoutARow_AreNotASet()
    {
        // An interrupted generation: the sheets were written but the row never was.
        await Objects.WriteAsync($"ABC-123/{Identity}/0.webp", new MemoryStream([1]));

        Assert.Null(await Store().GetSetAsync("ABC-123", Identity));
        Assert.Empty(await Store().ListSetsAsync());
    }

    [Fact]
    public async Task DeleteSetAsync_RemovesTheRowAndTheSheets()
    {
        var store = Store();
        await store.SaveAsync("ABC-123", NewSet(), Sheets([1]));
        await store.SaveAsync("ABC-123", NewSet("fedcba9876543210"), Sheets([2]));

        await store.DeleteSetAsync(new TrickplaySetLocation("ABC-123", Identity));

        Assert.Equal([new TrickplaySetLocation("ABC-123", "fedcba9876543210")], await store.ListSetsAsync());
        Assert.Equal(["ABC-123/fedcba9876543210/0.webp"], Objects.Keys);
    }

    [Fact]
    public async Task DeleteSetAsync_IgnoresAMalformedLocation()
    {
        var store = Store();
        await store.SaveAsync("ABC-123", NewSet(), Sheets([1]));

        await store.DeleteSetAsync(new TrickplaySetLocation("..", Identity));
        await store.DeleteSetAsync(new TrickplaySetLocation("ABC-123", "../x"));

        Assert.Single(await store.ListSetsAsync());
        Assert.Single(Objects.Keys);
    }

    [Fact]
    public async Task DeleteOrphanedSheetsAsync_DeletesOnlyOldSheetsWithoutARow()
    {
        var store = Store();
        await store.SaveAsync("ABC-123", NewSet(), Sheets([1]));
        Objects.SetLastModified($"ABC-123/{Identity}/0.webp", DateTimeOffset.UtcNow.AddDays(-3));

        // Left by a crash two days ago (including the old manifest-and-staging layout), and one
        // still being written.
        foreach (var key in new[] { "ABC-123/fedcba9876543210/0.webp", "ABC-123/fedcba9876543210/manifest.json", "XYZ-9/.tmp-0a1b/0.webp" })
        {
            await Objects.WriteAsync(key, new MemoryStream([1]));
            Objects.SetLastModified(key, DateTimeOffset.UtcNow.AddDays(-2));
        }
        await Objects.WriteAsync("XYZ-9/fedcba9876543210/0.webp", new MemoryStream([1]));

        var deleted = await store.DeleteOrphanedSheetsAsync(TimeSpan.FromDays(1));

        Assert.Equal(2, deleted);
        Assert.Equal([$"ABC-123/{Identity}/0.webp", "XYZ-9/fedcba9876543210/0.webp"], Objects.Keys.Order());
    }
}
