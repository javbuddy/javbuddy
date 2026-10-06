using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class JellyfinMovieScanServiceTests
{
    [Fact]
    public async Task ScanAsync_ShowsRunningActivity_ThenLinksFirstMatchAndUpdatesCheckedTime()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedAsync(factory);
        var client = CreateClient();
        var pending = new TaskCompletionSource<MediaServerLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(pending.Task);
        var tracker = CreateTracker();
        var service = CreateService(factory, client, tracker);
        var before = DateTime.UtcNow;

        var scan = service.ScanAsync(movieId, "ABC-123");
        Assert.True(tracker.Current?.IsRunning);
        pending.SetResult(new MediaServerLookupResult(true,
        [
            new JellyfinItemDto { Id = "first", ServerId = "server", LibraryName = "Movies" },
            new JellyfinItemDto { Id = "second" },
        ], null));
        await scan;

        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal(MovieStatus.Got, movie.Status);
        Assert.Equal("first", movie.JellyfinItemId);
        Assert.Equal("server", movie.JellyfinServerId);
        Assert.Equal("Movies", movie.JellyfinLibraryName);
        Assert.InRange(movie.JellyfinCheckedAt!.Value, before, DateTime.UtcNow);
        Assert.Equal(new TaskActivity("Jellyfin · ABC-123", "Linked in Jellyfin.", false), tracker.Current);
    }

    [Theory]
    [InlineData("empty", "Not found in Jellyfin.", false)]
    [InlineData("null", "Lookup failed.", true)]
    [InlineData("error", "Server unavailable", true)]
    [InlineData("invalid", "Not found in Jellyfin.", false)]
    [InlineData("libraries", "No Jellyfin libraries selected", true)]
    [InlineData("exception", "Jellyfin scan failed.", true)]
    public async Task ScanAsync_UnsuccessfulLookup_PreservesExistingLinkAndClearsRunningState(string scenario, string message, bool failed)
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedAsync(factory);
        var client = CreateClient();
        var result = scenario switch
        {
            "null" => new MediaServerLookupResult(true, null, null),
            "error" => new MediaServerLookupResult(false, null, "Server unavailable"),
            "invalid" => new MediaServerLookupResult(true, [new JellyfinItemDto { Id = " " }, new JellyfinItemDto { Id = "valid" }], null),
            _ => new MediaServerLookupResult(true, [], null),
        };
        client.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(result);
        if (scenario == "libraries") client.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        if (scenario == "exception")
        {
            client.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>())
                .Returns(Task.FromException<MediaServerLookupResult>(new InvalidOperationException("Unexpected")));
        }
        var tracker = CreateTracker();
        await CreateService(factory, client, tracker).ScanAsync(movieId, "ABC-123");

        Assert.NotNull(tracker.Current);
        Assert.False(tracker.Current.IsRunning);
        Assert.Equal(failed, tracker.Current.Failed);
        Assert.Contains(message, tracker.Current.Message);
        await AssertUnchangedAsync(factory);
        if (scenario == "libraries")
        {
            await client.DidNotReceive().LookupInSelectedLibrariesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task ScanAsync_CancellationPropagates_AndClearsRunningState()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedAsync(factory);
        var client = CreateClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        client.LookupInSelectedLibrariesAsync("ABC-123", cancellation.Token)
            .Returns(Task.FromCanceled<MediaServerLookupResult>(cancellation.Token));
        var tracker = CreateTracker();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(factory, client, tracker).ScanAsync(movieId, "ABC-123", cancellation.Token));

        Assert.Equal(new TaskActivity("Jellyfin · ABC-123", "Scan cancelled.", false), tracker.Current);
        await AssertUnchangedAsync(factory);
    }

    [Fact]
    public async Task ScanAsync_Disabled_CompletesActivityWithDisabledMessage()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedAsync(factory);
        var client = CreateClient();
        client.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);
        var tracker = CreateTracker();
        var service = CreateService(factory, client, tracker);

        await service.ScanAsync(movieId, "ABC-123");

        Assert.NotNull(tracker.Current);
        Assert.False(tracker.Current.IsRunning);
        Assert.True(tracker.Current.Failed);
        Assert.Equal("Jellyfin integration is disabled.", tracker.Current.Message);
        await AssertUnchangedAsync(factory);
        await client.DidNotReceive().LookupInSelectedLibrariesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAsync_UnexpectedExceptionInMovieService_CompletesActivity()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await SeedAsync(factory);
        var client = CreateClient();
        client.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, [new JellyfinItemDto { Id = "first" }], null));

        var movieService = Substitute.For<IMovieService>();
        movieService.LinkJellyfinItemAsync(movieId, Arg.Any<JellyfinItemDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Database corruption")));

        var tracker = CreateTracker();
        var service = new JellyfinMovieScanService(client, movieService, tracker, NullLogger<JellyfinMovieScanService>.Instance);

        await service.ScanAsync(movieId, "ABC-123");

        Assert.NotNull(tracker.Current);
        Assert.False(tracker.Current.IsRunning);
        Assert.True(tracker.Current.Failed);
    }

    private static IJellyfinClient CreateClient()
    {
        var client = Substitute.For<IJellyfinClient>();
        client.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        client.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(["Movies"]);
        return client;
    }

    private static TaskActivityTracker CreateTracker() => new(new ScheduledTaskChangeNotifier(), new ManualActivityTimeProvider());

    private static JellyfinMovieScanService CreateService(TestDbContextFactory factory, IJellyfinClient client, TaskActivityTracker tracker) =>
        new(client, new MovieService(factory), tracker, NullLogger<JellyfinMovieScanService>.Instance);

    private static async Task<int> SeedAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing, JellyfinItemId = "old", JellyfinCheckedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private static async Task AssertUnchangedAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal(MovieStatus.Missing, movie.Status);
        Assert.Equal("old", movie.JellyfinItemId);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), movie.JellyfinCheckedAt);
    }
}
