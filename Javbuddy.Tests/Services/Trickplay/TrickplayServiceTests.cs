using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayServiceTests : IDisposable
{
    private static readonly TrickplayLayout JellyfinLayout = new(320, 180, 10, 10, 100, 10000, 1000, "https://jf/Videos/i/Trickplay/320/{index}.jpg?api_key=k");

    private readonly TestDbContextFactory dbFactory = new();
    private readonly InMemoryObjectStoreProvider stores = new();
    private readonly IJellyfinClient jellyfinClient = Substitute.For<IJellyfinClient>();

    public TrickplayServiceTests()
    {
        jellyfinClient.GetTrickplayAsync("jf-1", Arg.Any<CancellationToken>()).Returns(JellyfinLayout);
    }

    public void Dispose() => dbFactory.Dispose();

    private IConfiguration Config(Dictionary<string, string?>? extra = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(extra ?? [])
        .Build();

    private TrickplayService Service(IConfiguration? configuration = null)
    {
        configuration ??= Config();
        return new TrickplayService(
            dbFactory,
            new TrickplayStore(dbFactory, stores),
            new TrickplaySettingsService(dbFactory, configuration),
            jellyfinClient);
    }

    private async Task<int> AddMovieAsync(string? jellyfinItemId, params MovieFile[] files)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = jellyfinItemId };
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
    public async Task LocalTrickplay_IsPreferred_OverJellyfins()
    {
        var file = File1080();
        var movieId = await AddMovieAsync("jf-1", file);
        var identity = await WriteLocalSetAsync(file);

        var layout = await Service().GetAsync(movieId);

        Assert.Equal(new TrickplayLayout(320, 180, 10, 10, 713, 10000, 7127.8, $"/trickplay/{movieId}/{identity}/{{index}}.webp"), layout);
        await jellyfinClient.DidNotReceiveWithAnyArgs().GetTrickplayAsync(default!, default);
    }

    [Fact]
    public async Task WithoutLocalTrickplay_FallsBackToJellyfins_ByDefault()
    {
        var movieId = await AddMovieAsync("jf-1", File1080());

        Assert.Equal(JellyfinLayout, await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task WithTheFallbackTurnedOff_ThereIsNoTrickplay()
    {
        var movieId = await AddMovieAsync("jf-1", File1080());
        await new TrickplaySettingsService(dbFactory, Config()).SaveAsync(new TrickplaySettings { JellyfinFallback = false });

        Assert.Null(await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task TheFallbackEnvironmentVariable_OverridesTheSavedSetting()
    {
        var movieId = await AddMovieAsync("jf-1", File1080());
        await new TrickplaySettingsService(dbFactory, Config()).SaveAsync(new TrickplaySettings { JellyfinFallback = true });

        Assert.Null(await Service(Config(new() { ["Trickplay:JellyfinFallback"] = "false" })).GetAsync(movieId));
    }

    [Fact]
    public async Task AMovieNotMatchedInJellyfin_WithoutLocalTrickplay_HasNone()
    {
        var movieId = await AddMovieAsync(null, File1080());

        Assert.Null(await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task AReplacedFile_NoLongerUsesTheOldFilesTrickplay()
    {
        var movieId = await AddMovieAsync(null, File1080());
        await WriteLocalSetAsync(new MovieFile { FileName = "ABC-123.mp4", DurationSeconds = 6000, Width = 1920, Height = 1080 });

        Assert.Null(await Service().GetAsync(movieId));
    }

    [Fact]
    public async Task TheMainFile_IsThePrimaryVersion()
    {
        var primary = File1080("ABC-123-4K.mp4", primary: true);
        primary.Width = 3840;
        primary.Height = 2160;
        var movieId = await AddMovieAsync(null, File1080("ABC-123.mp4", primary: false), primary);
        var identity = await WriteLocalSetAsync(primary);

        Assert.Equal($"/trickplay/{movieId}/{identity}/{{index}}.webp", (await Service().GetAsync(movieId))?.TileUrlTemplate);
    }

    [Fact]
    public async Task OpenTileAsync_FindsALocalSheet_AndNothingElse()
    {
        var file = File1080();
        var movieId = await AddMovieAsync(null, file);
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
