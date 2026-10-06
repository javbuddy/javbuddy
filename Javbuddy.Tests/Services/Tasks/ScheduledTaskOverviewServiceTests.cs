using Javbuddy.Models;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class ScheduledTaskOverviewServiceTests
{
    private static IScheduledTask Task(string name, TimeSpan interval, bool show = true)
    {
        var task = Substitute.For<IScheduledTask>();
        task.Name.Returns(name);
        task.Description.Returns($"{name} description");
        task.ShowInTasksUi.Returns(show);
        task.GetInterval().Returns(interval);
        return task;
    }

    private static async Task AddRunAsync(TestDbContextFactory factory, string task, DateTime queuedAt, DateTime? startedAt = null, DateTime? endedAt = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledTaskRuns.Add(new ScheduledTaskRun { TaskName = task, QueuedAt = queuedAt, StartedAt = startedAt, EndedAt = endedAt });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetOverviewAsync_RowUsesLastFinishedRunForLastExecutionDurationAndNextDue()
    {
        using var factory = new TestDbContextFactory();
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(factory, "sync", t0.AddHours(-3), t0.AddHours(-3), t0.AddHours(-3).AddMinutes(2));
        await AddRunAsync(factory, "sync", t0, t0, t0.AddMinutes(5));
        await AddRunAsync(factory, "sync", t0.AddHours(1));
        var service = new ScheduledTaskOverviewService(factory, [Task("sync", TimeSpan.FromHours(6))]);

        var overview = await service.GetOverviewAsync();

        var row = Assert.Single(overview.Rows);
        Assert.Equal("sync description", row.Description);
        Assert.Equal(t0.AddMinutes(5), row.LastExecution);
        Assert.Equal(TimeSpan.FromMinutes(5), row.LastDuration);
        Assert.Equal(t0.AddMinutes(5).AddHours(6), row.NextExecution);
    }

    [Fact]
    public async Task GetOverviewAsync_NeverRunTask_HasNoLastExecutionAndIsDueOneIntervalFromNow()
    {
        using var factory = new TestDbContextFactory();
        var service = new ScheduledTaskOverviewService(factory, [Task("new", TimeSpan.FromHours(1))]);

        var before = DateTime.UtcNow;
        var row = Assert.Single((await service.GetOverviewAsync()).Rows);

        Assert.Null(row.LastExecution);
        Assert.Null(row.LastDuration);
        Assert.InRange(row.NextExecution, before.AddHours(1), DateTime.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task GetOverviewAsync_HidesTasksNotShownInUi_AndLimitsHistoryToNewest30()
    {
        using var factory = new TestDbContextFactory();
        var t0 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 35; i++)
        {
            await AddRunAsync(factory, "visible", t0.AddMinutes(i));
        }
        await AddRunAsync(factory, "hidden", t0.AddDays(1));
        var service = new ScheduledTaskOverviewService(factory, [Task("visible", TimeSpan.FromHours(1)), Task("hidden", TimeSpan.FromHours(1), show: false)]);

        var overview = await service.GetOverviewAsync();

        Assert.Equal(["visible"], overview.Rows.Select(r => r.Name));
        Assert.Equal(30, overview.History.Count);
        Assert.All(overview.History, r => Assert.Equal("visible", r.TaskName));
        Assert.Equal(t0.AddMinutes(34), overview.History[0].QueuedAt);
    }

    [Fact]
    public async Task GetActiveRunAsync_ReturnsTheMostRecentlyStartedUnfinishedRunOfAVisibleTask()
    {
        using var factory = new TestDbContextFactory();
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(factory, "visible", t0, t0.AddMinutes(1));
        await AddRunAsync(factory, "visible", t0, t0.AddMinutes(5));
        await AddRunAsync(factory, "visible", t0, t0, t0.AddMinutes(2));
        await AddRunAsync(factory, "hidden", t0, t0.AddMinutes(9));
        var service = new ScheduledTaskOverviewService(factory, [Task("visible", TimeSpan.FromHours(1)), Task("hidden", TimeSpan.FromHours(1), show: false)]);

        var active = await service.GetActiveRunAsync();

        Assert.Equal(t0.AddMinutes(5), active?.StartedAt);
        Assert.Equal("visible", active?.TaskName);
    }

    [Fact]
    public async Task GetRunStateAsync_ReturnsLatestQueuedAndLastCompletedRunOfThatTask()
    {
        using var factory = new TestDbContextFactory();
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(factory, "sync", t0, t0, t0.AddMinutes(1));
        await AddRunAsync(factory, "sync", t0.AddHours(1));
        await AddRunAsync(factory, "other", t0.AddHours(5), t0.AddHours(5), t0.AddHours(6));
        var service = new ScheduledTaskOverviewService(factory, []);

        var state = await service.GetRunStateAsync("sync");

        Assert.Equal(t0.AddHours(1), state.Latest?.QueuedAt);
        Assert.Null(state.Latest?.EndedAt);
        Assert.Equal(t0.AddMinutes(1), state.LastCompleted?.EndedAt);
    }
}
