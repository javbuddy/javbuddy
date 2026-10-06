using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Nfo;

public class NfoDriftPushLauncherTests
{
    private sealed record Harness(NfoDriftPushLauncher Launcher, TaskActivityTracker Tracker, INfoSyncService NfoSync, List<int> Notifications);

    private static Harness CreateHarness()
    {
        var nfoSync = Substitute.For<INfoSyncService>();
        var services = new ServiceCollection();
        services.AddSingleton(nfoSync);
        var provider = services.BuildServiceProvider();

        var runner = new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        var tracker = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var notifier = new MovieChangeNotifier();
        var notifications = new List<int>();
        notifier.Changed += () => notifications.Add(1);
        return new Harness(new NfoDriftPushLauncher(runner, tracker, notifier), tracker, nfoSync, notifications);
    }

    [Fact]
    public async Task TryQueue_RunsThePush_ReportsTheSummary_AndRefreshesTheGrid()
    {
        var h = CreateHarness();
        var push = new TaskCompletionSource<NfoDriftPushResult>();
        h.NfoSync.PushNfoDriftAsync(Arg.Any<IReadOnlyList<int>>(), true, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(push.Task);

        var job = h.Launcher.TryQueue([1, 2, 3], includeExternal: true);

        Assert.NotNull(job);
        Assert.True(h.Launcher.IsRunning);
        // A second push can't start while one is running.
        Assert.Null(h.Launcher.TryQueue([4], includeExternal: false));

        push.SetResult(new NfoDriftPushResult(Written: 2, SkippedExternal: 0, Failed: 1, StillDrifting: 1));
        await job;

        Assert.False(h.Launcher.IsRunning);
        Assert.Equal(new TaskActivity("Write Javbuddy metadata to .nfo", "Wrote 2 .nfo file(s); 1 still drifting; 1 failed.", IsRunning: false), h.Tracker.Current);
        Assert.NotEmpty(h.Notifications);
        await h.NfoSync.Received(1).PushNfoDriftAsync(
            Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 1, 2, 3 })), true, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryQueue_AFailedPush_IsReportedAndFreesTheLauncher()
    {
        var h = CreateHarness();
        h.NfoSync.PushNfoDriftAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<bool>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns<NfoDriftPushResult>(_ => throw new InvalidOperationException("boom"));

        await h.Launcher.TryQueue([1], includeExternal: false)!;

        Assert.False(h.Launcher.IsRunning);
        Assert.Equal(new TaskActivity("Write Javbuddy metadata to .nfo", "Writing .nfo files failed: boom", IsRunning: false, Failed: true), h.Tracker.Current);
    }

    [Theory]
    [InlineData(3, 0, 0, 0, "Wrote 3 .nfo file(s).")]
    [InlineData(1, 4, 0, 0, "Wrote 1 .nfo file(s); skipped 4 with outside edits.")]
    [InlineData(0, 0, 2, 1, "Wrote 0 .nfo file(s); 1 still drifting; 2 failed.")]
    public void Summary_ListsOnlyNonZeroExtras(int written, int skipped, int failed, int still, string expected) =>
        Assert.Equal(expected, NfoDriftPushLauncher.Summary(new NfoDriftPushResult(written, skipped, failed, still)));
}
