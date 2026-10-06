using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayTriggerTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();
    private readonly InMemoryObjectStoreProvider stores = new();
    private readonly TrickplayQueue queue = new(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System),
        TimeProvider.System,
        NullLogger<TrickplayQueue>.Instance);

    public void Dispose()
    {
        queue.Dispose();
        dbFactory.Dispose();
    }

    // The queue isn't started, so whatever is enqueued just stays counted.
    private TrickplayTrigger Trigger(Dictionary<string, string?>? env = null, TrickplayQueue? queue = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(env ?? [])
            .Build();
        return new TrickplayTrigger(dbFactory, new TrickplaySettingsService(dbFactory, configuration),
            new TrickplayStore(dbFactory, stores), queue ?? this.queue);
    }

    private async Task<int> AddMovieAsync(double? duration = 60, string code = "ABC-123")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = code, Status = MovieStatus.Got, LocalFileSizeBytes = 1 };
        movie.MovieFiles.Add(new MovieFile { FileName = code + ".mp4", IsPrimary = true, DurationSeconds = duration, Width = 1920, Height = 1080 });
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task ANewProbedFile_IsQueued()
    {
        await Trigger().OnVideoFilesChangedAsync(await AddMovieAsync());

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task WithTheSettingOff_OrNoProbe_NothingIsQueued()
    {
        await Trigger(new() { ["Trickplay:GenerateForNewFiles"] = "false" }).OnVideoFilesChangedAsync(await AddMovieAsync());
        await Trigger().OnVideoFilesChangedAsync(await AddMovieAsync(duration: null, code: "ABC-124"));

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task AFileThatAlreadyHasItsSet_IsNotQueued()
    {
        var movieId = await AddMovieAsync();
        var identity = TrickplayIdentity.For("ABC-123.mp4", 60, 1920, 1080)!;
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
            FileName = "ABC-123.mp4",
        };
        await new TrickplayStore(dbFactory, stores).SaveAsync("ABC-123", set, [() => new MemoryStream([1])]);

        await Trigger().OnVideoFilesChangedAsync(movieId);

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Rescan_QueuesAMissingSet_EvenWithTheSettingOff()
    {
        var queued = await Trigger(new() { ["Trickplay:GenerateForNewFiles"] = "false" }).OnRescanAsync(await AddMovieAsync());

        Assert.True(queued);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task Rescan_RetriesAFailedGeneration()
    {
        var movieId = await AddMovieAsync();
        var identity = TrickplayIdentity.For("ABC-123.mp4", 60, 1920, 1080)!;
        var generator = Substitute.For<ITrickplayGenerator>();
        generator.GenerateAsync(movieId, Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(TrickplayGenerateResult.Failed);
        using var failing = new TrickplayQueue(
            new ServiceCollection().AddSingleton(generator).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System),
            TimeProvider.System,
            NullLogger<TrickplayQueue>.Instance);
        await failing.StartAsync(CancellationToken.None);
        await Trigger(queue: failing).OnVideoFilesChangedAsync(movieId);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(failing.HasFailed(movieId, identity) && failing.Count == 0) && DateTime.UtcNow < deadline) await Task.Delay(10);
        await failing.StopAsync(CancellationToken.None);
        Assert.True(failing.HasFailed(movieId, identity));

        // The new-file trigger leaves a failed file alone; a manual rescan queues it again.
        await Trigger(queue: failing).OnVideoFilesChangedAsync(movieId);
        Assert.Equal(0, failing.Count);
        var queued = await Trigger(queue: failing).OnRescanAsync(movieId);

        Assert.True(queued);
        Assert.Equal(1, failing.Count);
        Assert.False(failing.HasFailed(movieId, identity));
    }

    [Fact]
    public async Task Rescan_WithoutAProbe_QueuesNothing()
    {
        Assert.False(await Trigger().OnRescanAsync(await AddMovieAsync(duration: null)));
        Assert.Equal(0, queue.Count);
    }
}
