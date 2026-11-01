using Javbuddy.Services.Tasks;

namespace Javbuddy.Tests.Services.Tasks;

public class ScheduledTaskDueTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    [Fact]
    public void ARunTaskIsDueOnceItsIntervalHasPassedSinceTheLastRun()
    {
        Assert.False(ScheduledTaskHostedService.IsDue(Now - TimeSpan.FromHours(23), Now, Now - TimeSpan.FromDays(9), Day, false));
        Assert.True(ScheduledTaskHostedService.IsDue(Now - TimeSpan.FromHours(25), Now, Now - TimeSpan.FromMinutes(1), Day, false));
    }

    [Fact]
    public void ANeverRunTaskWaitsAnIntervalFromProcessStart_UnlessItRunsImmediately()
    {
        var justStarted = Now - TimeSpan.FromMinutes(2);

        Assert.False(ScheduledTaskHostedService.IsDue(null, Now, justStarted, Day, runImmediatelyWhenNeverRun: false));
        Assert.True(ScheduledTaskHostedService.IsDue(null, Now, justStarted, Day, runImmediatelyWhenNeverRun: true));
        Assert.True(ScheduledTaskHostedService.IsDue(null, Now, Now - TimeSpan.FromDays(2), Day, runImmediatelyWhenNeverRun: false));
    }

    [Fact]
    public void RunImmediatelyDoesNotOverrideAnIntervalThatHasNotPassedSinceTheLastRun()
    {
        Assert.False(ScheduledTaskHostedService.IsDue(Now - TimeSpan.FromHours(1), Now, Now - TimeSpan.FromMinutes(1), Day, runImmediatelyWhenNeverRun: true));
    }
}
