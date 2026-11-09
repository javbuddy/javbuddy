using Javbuddy.Models;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();
    private readonly InMemoryObjectStoreProvider stores = new();

    public void Dispose() => dbFactory.Dispose();

    private TrickplayService Service() => new(dbFactory, new TrickplayStore(dbFactory, stores));

    private async Task<int> AddMovieAsync(params MovieFile[] files)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got };
        foreach (var file in files) movie.MovieFiles.Add(file);
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private static MovieFile File1080(string name = "ABC-123.mp4", bool primary = true) =>
        new() { FileName = name, IsPrimary = primary, DurationSeconds = 7127.8, Width = 1920, Height = 1080 };

    private async Task<string> WriteLocalSetAsync(MovieFile file, int thumbnailCount = 713)
    {
        var identity = TrickplayIdentity.For(file.FileName, file.DurationSeconds, file.Width, file.Height)!;
        var set = new TrickplaySet
        {
            Identity = identity,
            Width = 320,
            Height = 180,
            TileWidth = 10,
            TileHeight = 10,
            ThumbnailCount = thumbnailCount,
            IntervalMs = 10000,
            DurationSeconds = 7127.8,
            GeneratedAt = DateTime.UtcNow,
            FileName = file.FileName,
        };
        await new TrickplayStore(dbFactory, stores).SaveAsync("ABC-123", set, [() => new MemoryStream("tile"u8.ToArray())]);
        return identity;
    }

    [Fact]
    public async Task LocalTrickplay_IsUsed()
    {
        var file = File1080();
        var movieId = await AddMovieAsync(file);
        var identity = await WriteLocalSetAsync(file);

        var layout = await Service().GetAsync(movieId);

        Assert.Equal(new TrickplayLayout(320, 180, 10, 10, 713, 10000, 7127.8, $"/trickplay/{movieId}/{identity}/{{index}}.webp"), layout);
    }

    [Fact]
    public async Task AReplacedFile_NoLongerUsesTheOldFilesTrickplay()
    {
        var movieId = await AddMovieAsync(File1080());
        await WriteLocalSetAsync(new MovieFile { FileName = "ABC-123.mp4", DurationSeconds = 6000, Width = 1920, Height = 1080 });

        Assert.Null(await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task TheMainFile_IsThePrimaryVersion()
    {
        var primary = File1080("ABC-123-4K.mp4", primary: true);
        primary.Width = 3840;
        primary.Height = 2160;
        var movieId = await AddMovieAsync(File1080("ABC-123.mp4", primary: false), primary);
        var identity = await WriteLocalSetAsync(primary);

        Assert.Equal($"/trickplay/{movieId}/{identity}/{{index}}.webp", (await Service().GetAsync(movieId))?.TileUrlTemplate);
    }

    [Fact]
    public async Task OpenTileAsync_FindsALocalSheet_AndNothingElse()
    {
        var file = File1080();
        var movieId = await AddMovieAsync(file);
        var identity = await WriteLocalSetAsync(file);
        var service = Service();

        await using (var tile = await service.OpenTileAsync(movieId, identity, 0))
        {
            Assert.Equal($"ABC-123/{identity}/0.webp", tile?.Key);
        }
        Assert.Null(await service.OpenTileAsync(movieId, identity, 1));
        Assert.Null(await service.OpenTileAsync(movieId, "../../etc/passwd", 0));
        Assert.Null(await service.OpenTileAsync(movieId + 1, identity, 0));
    }
}
