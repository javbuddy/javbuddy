using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The worker fills stale movies on start and again whenever it's signalled.</summary>
public sealed class ClipActorRefreshWorkerTests : IDisposable
{
    // The worker queries from its own thread, so each context gets its own connection.
    private readonly TestDbContextFactory factory = TestDbContextFactory.WithConnectionPerContext();

    public void Dispose() => factory.Dispose();

    private async Task<bool> IsStaleAsync(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Movies.Where(m => m.Id == movieId).Select(m => m.ClipActorsStale).SingleAsync();
    }

    private async Task WaitUntilFreshAsync(int movieId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await IsStaleAsync(movieId))
        {
            Assert.True(DateTime.UtcNow < deadline, $"movie {movieId} still stale");
            await Task.Delay(20);
        }
    }

    private async Task<int> AddMovieAsync(string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task Worker_RefreshesOnStart_AndOnSignal()
    {
        var first = await AddMovieAsync("WRK-001");
        using var worker = new ClipActorRefreshWorker(factory, factory.ClipActorSignal, NullLogger<ClipActorRefreshWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilFreshAsync(first);

        var second = await AddMovieAsync("WRK-002");
        factory.ClipActorSignal.Signal();
        await WaitUntilFreshAsync(second);

        await worker.StopAsync(CancellationToken.None);
    }
}
