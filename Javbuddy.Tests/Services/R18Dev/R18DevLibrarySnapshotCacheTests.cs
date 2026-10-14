using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevLibrarySnapshotCacheTests
{
    private static async Task AddMovieAsync(TestDbContextFactory factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Movies.Add(new Movie { Code = code, Status = MovieStatus.Got });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAsync_ReusesTheSnapshotWithinTheLifetime_AndReloadsAfterIt()
    {
        using var factory = new TestDbContextFactory();
        var time = new FakeTimeProvider();
        using var cache = new R18DevLibrarySnapshotCache(factory, time);
        await AddMovieAsync(factory, "ABC-001");

        var first = await cache.GetAsync();
        await AddMovieAsync(factory, "ABC-002");
        var second = await cache.GetAsync();

        Assert.Same(first, second);
        Assert.Single(second.Movies);

        time.Advance(R18DevLibrarySnapshotCache.Lifetime);
        var third = await cache.GetAsync();

        Assert.NotSame(first, third);
        Assert.Equal(2, third.Movies.Count);
    }

    [Fact]
    public async Task GetAsync_ReloadsRightAfterTheLibraryChangeIsReported()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new MovieChangeNotifier();
        using var cache = new R18DevLibrarySnapshotCache(factory, new FakeTimeProvider(), notifier);
        await AddMovieAsync(factory, "ABC-001");
        var first = await cache.GetAsync();

        await AddMovieAsync(factory, "ABC-002");
        notifier.NotifyChanged();
        var second = await cache.GetAsync();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.Movies.Count);
    }

    [Fact]
    public async Task GetAsync_AfterDispose_NoLongerReactsToTheNotifier()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new MovieChangeNotifier();
        var cache = new R18DevLibrarySnapshotCache(factory, new FakeTimeProvider(), notifier);
        var first = await cache.GetAsync();

        cache.Dispose();
        notifier.NotifyChanged();

        Assert.Same(first, await cache.GetAsync());
    }

    [Fact]
    public async Task GetAsync_CancelledCallerDoesNotCancelTheSharedLoad()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new R18DevLibrarySnapshotCache(factory, new FakeTimeProvider());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Whether it throws depends on if the load already finished; either way the shared load must survive.
        var ex = await Record.ExceptionAsync(() => cache.GetAsync(cts.Token));
        Assert.True(ex is null or OperationCanceledException);

        Assert.NotNull(await cache.GetAsync());
    }
}
