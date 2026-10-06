using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class TrickplayBackfillTaskTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("javbuddy-trickplay-backfill-test-").FullName;
    private readonly TestDbContextFactory dbFactory = new();
    private readonly TrickplayStore store;
    private readonly TrickplayQueue queue = new(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System),
        TimeProvider.System,
        NullLogger<TrickplayQueue>.Instance);

    public TrickplayBackfillTaskTests()
    {
        store = new TrickplayStore(dbFactory, new ObjectStoreProvider(new FileSystemObjectStore(root), root));
    }

    public void Dispose()
    {
        queue.Dispose();
        dbFactory.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<int> AddMovieAsync(string code, double? duration, bool hasLocalVideo = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = code, Status = MovieStatus.Got, LocalFileSizeBytes = hasLocalVideo ? 1 : null };
        movie.MovieFiles.Add(new MovieFile { FileName = code + ".mp4", IsPrimary = true, DurationSeconds = duration, Width = 1920, Height = 1080 });
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private async Task<string> WriteSetAsync(string code, string identity, string? sourceIdentity = null)
    {
        var set = new TrickplaySet
        {
            Identity = identity,
            Width = 320,
            Height = 180,
            TileWidth = 10,
            TileHeight = 10,
            ThumbnailCount = 6,
            IntervalMs = 10000,
            DurationSeconds = 60,
            FileName = "x.mp4",
            SourceIdentity = sourceIdentity,
        };
        await store.SaveAsync(code, set, [() => new MemoryStream([1])]);
        return Path.Combine(root, "trickplay", code, identity);
    }

    private readonly IHighlightTrickplayService highlightTrickplay = Substitute.For<IHighlightTrickplayService>();

    private Task<string?> RunAsync() =>
        new TrickplayBackfillTask(dbFactory, store, queue, highlightTrickplay).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

    [Fact]
    public async Task QueuesEveryMovieWithoutTrickplay_WithoutWaitingForIt()
    {
        await AddMovieAsync("HAS-1", 60);
        await WriteSetAsync("HAS-1", TrickplayIdentity.For("HAS-1.mp4", 60, 1920, 1080)!);
        await AddMovieAsync("NEW-1", 60);
        await AddMovieAsync("NEW-2", 90);
        await AddMovieAsync("UNPROBED-1", null);
        await AddMovieAsync("MISSING-1", 60, hasLocalVideo: false);

        var summary = await RunAsync();

        Assert.Equal(2, queue.Count);
        Assert.Equal("queued 2 movies, 1 already had trickplay, 1 not probed yet", summary);
    }

    [Fact]
    public async Task QueuesHighlightSets_ForProbedMoviesWithHighlights()
    {
        // Highlights made before trickplay existed, or never viewed since, get theirs here.
        var withHighlight = await AddMovieAsync("HL-1", 3600);
        await AddMovieAsync("PLAIN-1", 3600);
        var unprobed = await AddMovieAsync("UNPROBED-1", null);
        var offline = await AddMovieAsync("OFFLINE-1", 3600, hasLocalVideo: false);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            foreach (var movieId in new[] { withHighlight, unprobed, offline })
            {
                db.MovieHighlights.Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });
            }
            await db.SaveChangesAsync();
        }
        highlightTrickplay.EnsureQueuedAsync(withHighlight, Arg.Any<CancellationToken>()).Returns(1);

        var summary = await RunAsync();

        await highlightTrickplay.Received(1).EnsureQueuedAsync(withHighlight, Arg.Any<CancellationToken>());
        await highlightTrickplay.ReceivedWithAnyArgs(1).EnsureQueuedAsync(default, default);
        Assert.Equal("queued 2 movies, 0 already had trickplay, 1 not probed yet, queued 1 highlight", summary);
    }

    [Fact]
    public async Task DeletesSetsOfGoneMoviesAndReplacedFiles_ButKeepsOnesWhoseFileIsUnreachable()
    {
        await AddMovieAsync("KEEP-1", 60);
        var current = await WriteSetAsync("KEEP-1", TrickplayIdentity.For("KEEP-1.mp4", 60, 1920, 1080)!);
        var replaced = await WriteSetAsync("KEEP-1", "00000000000000aa");
        var gone = await WriteSetAsync("GONE-1", "00000000000000bb");
        await AddMovieAsync("OFFLINE-1", 60, hasLocalVideo: false);
        var offline = await WriteSetAsync("OFFLINE-1", "00000000000000cc");

        var summary = await RunAsync();

        Assert.True(Directory.Exists(current));
        Assert.False(Directory.Exists(replaced));
        Assert.False(Directory.Exists(gone));
        Assert.False(Directory.Exists(Path.Combine(root, "trickplay", "GONE-1")));
        Assert.True(Directory.Exists(offline));
        Assert.EndsWith("deleted 2 unused trickplay sets", summary);
    }

    [Fact]
    public async Task AHighlightsSet_IsKeptWhileAHighlightMatchesIt_AndDeletedOnceNoneDoes()
    {
        var movieId = await AddMovieAsync("ABC-123", 3600, hasLocalVideo: false);
        var fileIdentity = TrickplayIdentity.For("ABC-123.mp4", 3600, 1920, 1080)!;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MovieHighlights.Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });
            await db.SaveChangesAsync();
        }
        var current = await WriteSetAsync("ABC-123", HighlightTrickplay.Identity(fileIdentity, 100, 130)!, fileIdentity);
        // The highlight's range before it was edited: stale even though the file isn't reachable.
        var moved = await WriteSetAsync("ABC-123", HighlightTrickplay.Identity(fileIdentity, 90, 130)!, fileIdentity);

        var summary = await RunAsync();

        Assert.True(Directory.Exists(current));
        Assert.False(Directory.Exists(moved));
        Assert.Contains("deleted 1 unused trickplay set", summary);
    }
}
