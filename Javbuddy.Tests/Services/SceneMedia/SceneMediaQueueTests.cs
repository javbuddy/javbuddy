using Javbuddy.Services.SceneMedia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.SceneMedia;

public class SceneMediaQueueTests
{
    private static readonly SceneMediaWindow Window = new(1_000, 2_000);

    private static (SceneMediaQueue Queue, ISceneMediaService Scenes, IHighlightMediaService Highlights) Create()
    {
        var scenes = Substitute.For<ISceneMediaService>();
        var highlights = Substitute.For<IHighlightMediaService>();
        var services = new ServiceCollection().AddSingleton(scenes).AddSingleton(highlights).BuildServiceProvider();
        return (new SceneMediaQueue(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance), scenes, highlights);
    }

    [Fact]
    public async Task HighlightJobs_RunTheHighlightService_AndReportTheMovie()
    {
        var (queue, scenes, highlights) = Create();
        highlights.GenerateAsync(5, Arg.Any<CancellationToken>()).Returns(9);
        var generated = new TaskCompletionSource<int>();
        queue.Generated += id => generated.TrySetResult(id);
        await queue.StartAsync(CancellationToken.None);

        queue.EnqueueHighlight(5, 9, Window);

        Assert.Equal(9, await generated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await scenes.DidNotReceive().GenerateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await queue.StopAsync(CancellationToken.None);
        queue.Dispose();
    }

    [Fact]
    public async Task SameIdOfDifferentKinds_AreSeparateJobs_FailureMemoryIsPerKind()
    {
        var (queue, scenes, highlights) = Create();
        var done = new CountdownEvent(2);
        scenes.GenerateAsync(5, Arg.Any<CancellationToken>()).Returns(_ => { done.Signal(); return (int?)null; });
        highlights.GenerateAsync(5, Arg.Any<CancellationToken>()).Returns(_ => { done.Signal(); return (int?)null; });
        queue.MarkFailed(5, new SceneMediaWindow(0, 1)); // a failed *scene* 5 at another range
        queue.MarkHighlightFailed(7, Window);
        await queue.StartAsync(CancellationToken.None);

        queue.Enqueue(5, 9, Window);
        queue.EnqueueHighlight(5, 9, Window);
        queue.EnqueueHighlight(7, 9, Window); // remembered as failed for this range: skipped

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        await Task.Delay(200);
        await highlights.DidNotReceive().GenerateAsync(7, Arg.Any<CancellationToken>());
        await queue.StopAsync(CancellationToken.None);
        queue.Dispose();
    }
}
