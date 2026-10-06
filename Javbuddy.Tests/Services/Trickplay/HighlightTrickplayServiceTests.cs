using Javbuddy.Models;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Trickplay;

public class HighlightTrickplayServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();
    private readonly InMemoryObjectStoreProvider stores = new();
    private readonly ITrickplayGenerator generator = Substitute.For<ITrickplayGenerator>();
    // The queue's worker resolves workerTrickplay, so a test can see what was queued.
    private readonly IHighlightTrickplayService workerTrickplay = Substitute.For<IHighlightTrickplayService>();
    // An apex job queued after the test's own marks when the worker has got through those.
    private readonly IApexMediaService workerApex = Substitute.For<IApexMediaService>();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SceneMediaQueue queue;
    private readonly string fileIdentity = TrickplayIdentity.For("ABC-123.mp4", 3600, 1920, 1080)!;

    public HighlightTrickplayServiceTests()
    {
        workerApex.GenerateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            drained.TrySetResult();
            return (int?)null;
        });
        var workerServices = new ServiceCollection().AddSingleton(workerTrickplay).AddSingleton(workerApex).BuildServiceProvider();
        queue = new SceneMediaQueue(workerServices.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
    }

    public void Dispose()
    {
        queue.Dispose();
        dbFactory.Dispose();
    }

    private HighlightTrickplayService Service() => new(dbFactory, new TrickplayStore(dbFactory, stores), generator, queue);

    private async Task<(int MovieId, int HighlightId)> AddHighlightAsync(double start, double end)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, LocalFileSizeBytes = 1 };
        movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123.mp4", IsPrimary = true, DurationSeconds = 3600, Width = 1920, Height = 1080 });
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = start, EndSeconds = end };
        db.MovieHighlights.Add(highlight);
        await db.SaveChangesAsync();
        return (movie.Id, highlight.Id);
    }

    private async Task WriteClipSetAsync(double start, double end)
    {
        var set = new TrickplaySet
        {
            Identity = HighlightTrickplay.Identity(fileIdentity, start, end)!,
            Width = 320,
            Height = 180,
            TileWidth = 10,
            TileHeight = 10,
            ThumbnailCount = 30,
            IntervalMs = 1000,
            DurationSeconds = end - start,
            FileName = "ABC-123.mp4",
            SourceIdentity = fileIdentity,
            StartSeconds = start,
        };
        await new TrickplayStore(dbFactory, stores).SaveAsync("ABC-123", set, [() => new MemoryStream("tile"u8.ToArray())]);
    }

    private async Task<int[]> QueuedAsync()
    {
        // The queue's single worker takes jobs in order, so once it reaches this one, everything the
        // test queued before it was handed to workerTrickplay; no fixed wait that a slow run outlasts.
        queue.EnqueueApex(int.MaxValue, 0, new SceneMediaWindow(0, 0));
        await queue.StartAsync(CancellationToken.None);
        await drained.Task;
        await queue.StopAsync(CancellationToken.None);
        return workerTrickplay.ReceivedCalls().Select(call => (int)call.GetArguments()[0]!).ToArray();
    }

    [Fact]
    public async Task AGeneratedSet_IsTheLayout_StartingAtTheHighlight()
    {
        var (movieId, highlightId) = await AddHighlightAsync(100, 130);
        await WriteClipSetAsync(100, 130);

        var layout = await Service().GetAsync(highlightId);

        var identity = HighlightTrickplay.Identity(fileIdentity, 100, 130);
        Assert.Equal(new TrickplayLayout(320, 180, 10, 10, 30, 1000, 30, $"/trickplay/{movieId}/{identity}/{{index}}.webp", 100), layout);
        Assert.Empty(await QueuedAsync());
    }

    [Fact]
    public async Task NoSet_IsNotQueuedByPlaying()
    {
        // Generating while the highlight streams stalls it on 8K files.
        var (_, highlightId) = await AddHighlightAsync(100, 130);

        Assert.Null(await Service().GetAsync(highlightId));
        Assert.Empty(await QueuedAsync());
    }

    [Fact]
    public async Task EnsureQueued_QueuesOnlyHighlightsThatNeedASetAndLackOne()
    {
        var (movieId, stale) = await AddHighlightAsync(100, 130);
        // One for the highlight's range before it was edited.
        await WriteClipSetAsync(90, 130);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MovieHighlights.AddRange(
                new MovieHighlight { MovieId = movieId, StartSeconds = 200, EndSeconds = 230 },
                // Long enough for the movie's own set.
                new MovieHighlight { MovieId = movieId, StartSeconds = 0, EndSeconds = 1800 });
            await db.SaveChangesAsync();
        }
        await WriteClipSetAsync(200, 230);

        Assert.Equal(1, await Service().EnsureQueuedAsync(movieId));
        Assert.Equal([stale], await QueuedAsync());
    }

    [Fact]
    public async Task EnsureQueued_SkipsAMovieWhoseFileIsntProbed()
    {
        var (movieId, _) = await AddHighlightAsync(100, 130);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.MovieFiles.ExecuteUpdateAsync(s => s.SetProperty(f => f.DurationSeconds, (double?)null));
        }

        Assert.Equal(0, await Service().EnsureQueuedAsync(movieId));
        Assert.Empty(await QueuedAsync());
    }

    [Theory]
    [InlineData(TrickplayGenerateResult.Generated, true, false)]
    [InlineData(TrickplayGenerateResult.Failed, false, true)]
    [InlineData(TrickplayGenerateResult.NoFile, false, true)]
    [InlineData(TrickplayGenerateResult.NotNeeded, false, false)]
    public async Task Generate_ReportsTheMovie_AndDoesntRetryAFailure(TrickplayGenerateResult result, bool reportsMovie, bool markedFailed)
    {
        var (movieId, highlightId) = await AddHighlightAsync(100, 130);
        generator.GenerateClipAsync(movieId, 100, 130, Arg.Any<CancellationToken>()).Returns(result);

        Assert.Equal(reportsMovie ? movieId : null, await Service().GenerateAsync(highlightId));

        // A failed range isn't queued again until the app restarts.
        await Service().EnsureQueuedAsync(movieId);
        Assert.Equal(markedFailed ? [] : [highlightId], await QueuedAsync());
    }
}
