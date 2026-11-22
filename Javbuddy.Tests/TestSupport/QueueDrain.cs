using Javbuddy.Services.SceneMedia;
using NSubstitute;

namespace Javbuddy.Tests.TestSupport;

/// <summary>Lets a test know a <see cref="SceneMediaQueue"/> has finished everything queued before a point, without
/// sleeping. The queue's single consumer works through its jobs in order, so once a sentinel job of another kind has
/// run, every job queued ahead of it has run too. Register <see cref="HighlightService"/> or <see cref="ApexService"/>
/// in the queue's service provider (whichever kind the test itself does not use), then await <see cref="DrainAsync"/>.</summary>
public sealed class QueueDrain
{
    private static readonly SceneMediaWindow Window = new(0, 1);
    private readonly TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IHighlightMediaService HighlightService { get; }
    public IApexMediaService ApexService { get; }

    public QueueDrain()
    {
        HighlightService = Substitute.For<IHighlightMediaService>();
        HighlightService.GenerateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => { ran.TrySetResult(); return (int?)null; });
        ApexService = Substitute.For<IApexMediaService>();
        ApexService.GenerateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => { ran.TrySetResult(); return (int?)null; });
    }

    public async Task DrainAsync(SceneMediaQueue queue, MediaKind sentinelKind = MediaKind.Highlight)
    {
        if (sentinelKind == MediaKind.Apex) queue.EnqueueApex(int.MaxValue, 0, Window);
        else queue.EnqueueHighlight(int.MaxValue, 0, Window);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
