using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class NewMovieArtworkWorkerTests
{
    [Fact]
    public async Task RunsOnStartup_ThenAgainEachTimeItIsSignalled()
    {
        var calls = 0;
        var ran = new SemaphoreSlim(0);
        var service = Substitute.For<INewMovieArtworkService>();
        service.GeneratePendingAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref calls);
            ran.Release();
            return Task.FromResult(0);
        });
        await using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var signal = new NewMovieArtworkSignal();
        var worker = new NewMovieArtworkWorker(provider.GetRequiredService<IServiceScopeFactory>(), signal, NullLogger<NewMovieArtworkWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        Assert.True(await ran.WaitAsync(TimeSpan.FromSeconds(5)), "startup pass");
        signal.Signal();
        Assert.True(await ran.WaitAsync(TimeSpan.FromSeconds(5)), "signalled pass");
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AFailedPass_IsRetriedAfterAPause_WithoutStoppingTheWorker()
    {
        var calls = 0;
        var retried = new SemaphoreSlim(0);
        var service = Substitute.For<INewMovieArtworkService>();
        service.GeneratePendingAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("db down");
            retried.Release();
            return Task.FromResult(0);
        });
        await using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var worker = new NewMovieArtworkWorker(provider.GetRequiredService<IServiceScopeFactory>(), new NewMovieArtworkSignal(),
            NullLogger<NewMovieArtworkWorker>.Instance, retryDelay: TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        Assert.True(await retried.WaitAsync(TimeSpan.FromSeconds(5)), "the pass after the failure");
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
    }
}
