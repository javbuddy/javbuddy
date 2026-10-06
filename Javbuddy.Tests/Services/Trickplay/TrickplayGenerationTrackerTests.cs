using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplayGenerationTrackerTests
{
    private readonly TrickplayGenerationTracker tracker = new();
    private readonly List<TrickplayTarget> changes = [];

    public TrickplayGenerationTrackerTests() => tracker.Changed += changes.Add;

    [Fact]
    public void ARun_IsGenerating_WithWholePercents_UntilDisposed()
    {
        var target = new TrickplayTarget(7);

        using (var run = tracker.Start(target))
        {
            Assert.Equal(new TrickplayGenerating(null), tracker.Get(target));
            run.Report(12.9);
            Assert.Equal(new TrickplayGenerating(12), tracker.Get(target));
            // ffmpeg reports several times a second; only a new whole percent is a change.
            run.Report(12.2);
            run.Report(140);
            Assert.Equal(new TrickplayGenerating(100), tracker.Get(target));
        }

        Assert.Null(tracker.Get(target));
        Assert.Equal([target, target, target, target], changes);
    }

    [Fact]
    public void AReportAfterTheRunEnded_DoesntBringItBack()
    {
        var target = new TrickplayTarget(7);
        var run = tracker.Start(target);
        run.Dispose();
        run.Report(50);
        run.Dispose();

        Assert.Null(tracker.Get(target));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void AMoviesSet_AndItsHighlightsSets_AreSeparate()
    {
        using var clip = tracker.Start(TrickplayTarget.ForHighlight(7, 100, 130.5));

        Assert.Null(tracker.Get(new TrickplayTarget(7)));
        Assert.NotNull(tracker.Get(TrickplayTarget.ForHighlight(7, 100, 130.5)));
        Assert.Null(tracker.Get(TrickplayTarget.ForHighlight(7, 100, 131)));
    }
}
