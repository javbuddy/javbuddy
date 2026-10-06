using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>One execution of a scheduled task — powers both the "Scheduled" table (latest run
/// per task) and the "Queue" history table on the System &gt; Tasks page.</summary>
public class ScheduledTaskRun
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string TaskName { get; set; } = "";

    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool Success { get; set; }

    // Set (throttled) while the task is running via IScheduledTask.RunAsync's IProgress<TaskProgress>
    // — see ScheduledTaskRunner. ProgressTotal is null when the task can't cheaply know its total
    // item count up front. ProgressStage is null for a task that never reports one (TaskProgress.Stage
    // left default). All three stay at their last-reported value once the run ends.
    public int? ProgressCurrent { get; set; }
    public int? ProgressTotal { get; set; }

    [StringLength(200)]
    public string? ProgressStage { get; set; }

    [StringLength(2000)]
    public string? ResultSummary { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }
}
