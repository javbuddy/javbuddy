using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

public sealed record ScheduledTaskRow(
    string Name, string Description, TimeSpan Interval, DateTime? LastExecution, TimeSpan? LastDuration, DateTime NextExecution);

public sealed record ScheduledTaskOverview(IReadOnlyList<ScheduledTaskRow> Rows, IReadOnlyList<ScheduledTaskRun> History);

/// <summary>A task's most recently queued run and its most recently finished one.</summary>
public sealed record ScheduledTaskRunState(ScheduledTaskRun? Latest, ScheduledTaskRun? LastCompleted);

public interface IScheduledTaskOverviewService
{
    Task<ScheduledTaskOverview> GetOverviewAsync(CancellationToken ct = default);

    /// <summary>The run of a task shown in the Tasks UI that is still unfinished (most recently started), or null.</summary>
    Task<ScheduledTaskRun?> GetActiveRunAsync(CancellationToken ct = default);

    Task<ScheduledTaskRunState> GetRunStateAsync(string taskName, CancellationToken ct = default);
}

/// <summary>System &gt; Tasks' read model: each task shown in the UI with its last finished run and
/// next due time, plus the 30 most recently queued runs of those tasks.</summary>
public class ScheduledTaskOverviewService(
    IDbContextFactory<AppDbContext> dbFactory,
    IEnumerable<IScheduledTask> scheduledTasks) : IScheduledTaskOverviewService
{
    private const int HistoryLimit = 30;

    public async Task<ScheduledTaskOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var visibleTasks = scheduledTasks.Where(t => t.ShowInTasksUi).ToList();

        var rows = new List<ScheduledTaskRow>();
        foreach (var task in visibleTasks)
        {
            var lastRun = await db.ScheduledTaskRuns
                .AsNoTracking()
                .Where(r => r.TaskName == task.Name && r.EndedAt != null)
                .OrderByDescending(r => r.EndedAt)
                .FirstOrDefaultAsync(ct);

            var interval = task.GetInterval();
            var lastExecution = lastRun?.EndedAt;
            var lastDuration = lastRun is { StartedAt: not null, EndedAt: not null }
                ? lastRun.EndedAt!.Value - lastRun.StartedAt!.Value
                : (TimeSpan?)null;
            var nextExecution = (lastExecution ?? DateTime.UtcNow) + interval;

            rows.Add(new ScheduledTaskRow(task.Name, task.Description, interval, lastExecution, lastDuration, nextExecution));
        }

        var visibleTaskNames = visibleTasks.Select(t => t.Name).ToList();
        var history = await db.ScheduledTaskRuns
            .AsNoTracking()
            .Where(r => visibleTaskNames.Contains(r.TaskName))
            .OrderByDescending(r => r.QueuedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        return new ScheduledTaskOverview(rows, history);
    }

    public async Task<ScheduledTaskRun?> GetActiveRunAsync(CancellationToken ct = default)
    {
        var visibleTaskNames = scheduledTasks.Where(t => t.ShowInTasksUi).Select(t => t.Name).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ScheduledTaskRuns
            .AsNoTracking()
            .Where(r => r.EndedAt == null && visibleTaskNames.Contains(r.TaskName))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ScheduledTaskRunState> GetRunStateAsync(string taskName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var latest = await db.ScheduledTaskRuns
            .AsNoTracking()
            .Where(r => r.TaskName == taskName)
            .OrderByDescending(r => r.QueuedAt)
            .FirstOrDefaultAsync(ct);
        var lastCompleted = await db.ScheduledTaskRuns
            .AsNoTracking()
            .Where(r => r.TaskName == taskName && r.EndedAt != null)
            .OrderByDescending(r => r.EndedAt)
            .FirstOrDefaultAsync(ct);
        return new ScheduledTaskRunState(latest, lastCompleted);
    }
}
