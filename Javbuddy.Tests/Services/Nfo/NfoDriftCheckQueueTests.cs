using Javbuddy.Services.Nfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Nfo;

public sealed class NfoDriftCheckQueueTests
{
    [Fact]
    public async Task Enqueue_ChecksEachMovieOnceInTheBackground()
    {
        var nfo = Substitute.For<INfoSyncService>();
        var done = new TaskCompletionSource();
        var checkedIds = new List<int>();
        nfo.CheckMovieNfoConflictAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (checkedIds)
            {
                checkedIds.Add(call.Arg<int>());
                if (checkedIds.Count == 3) done.TrySetResult();
            }
            return Task.FromResult<ActorNfoConflictCheckResult>(null!);
        });
        var services = new ServiceCollection().AddSingleton(nfo).BuildServiceProvider();
        using var queue = new NfoDriftCheckQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<NfoDriftCheckQueue>.Instance);

        queue.Enqueue([1, 2, 2, 3]);
        await queue.StartAsync(CancellationToken.None);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], checkedIds.Order());
    }
}
