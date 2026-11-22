using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tags;

public class TagLibraryPassLauncherTests
{
    private static (TagLibraryPassLauncher Launcher, TaskActivityTracker Tracker, ITagRuleService Rules) CreateHarness()
    {
        var rules = Substitute.For<ITagRuleService>();
        var provider = new ServiceCollection().AddSingleton(rules).BuildServiceProvider();
        var runner = new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        var tracker = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        return (new TagLibraryPassLauncher(runner, tracker), tracker, rules);
    }

    [Fact]
    public async Task TryQueue_RunsTheChosenPass_ReportsItAndRefusesASecondWhileRunning()
    {
        var (launcher, tracker, rules) = CreateHarness();
        var pass = new TaskCompletionSource<int>();
        rules.ApplyReplacementRulesToLibraryAsync(Arg.Any<CancellationToken>()).Returns(pass.Task);

        var job = launcher.TryQueue(TagLibraryPass.ApplyRules);

        Assert.NotNull(job);
        Assert.True(launcher.IsRunning);
        Assert.Null(launcher.TryQueue(TagLibraryPass.Discovery));

        pass.SetResult(3);
        Assert.Equal(3, await job);
        Assert.False(launcher.IsRunning);
        Assert.Equal(new TaskActivity("Apply tag rules to library", "3 movies reprocessed.", IsRunning: false), tracker.Current);
        await rules.DidNotReceiveWithAnyArgs().RunDiscoveryAsync(default);
    }

    [Theory]
    [InlineData(TagLibraryPass.Discovery)]
    [InlineData(TagLibraryPass.StripIgnored)]
    public async Task TryQueue_RunsTheMatchingServiceMethod(TagLibraryPass kind)
    {
        var (launcher, _, rules) = CreateHarness();
        rules.RunDiscoveryAsync(Arg.Any<CancellationToken>()).Returns(1);
        rules.StripIgnoredTagsFromLibraryAsync(Arg.Any<CancellationToken>()).Returns(1);

        Assert.Equal(1, await launcher.TryQueue(kind)!);

        if (kind == TagLibraryPass.Discovery) await rules.Received(1).RunDiscoveryAsync(Arg.Any<CancellationToken>());
        else await rules.Received(1).StripIgnoredTagsFromLibraryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryQueue_AFailedPass_FaultsTheTask_IsReportedAndFreesTheLauncher()
    {
        var (launcher, tracker, rules) = CreateHarness();
        rules.RunDiscoveryAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.TryQueue(TagLibraryPass.Discovery)!);

        Assert.False(launcher.IsRunning);
        Assert.Equal(new TaskActivity("Tag discovery", "Tag discovery failed: boom", IsRunning: false, Failed: true), tracker.Current);
    }
}
