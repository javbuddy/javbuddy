using Javbuddy.Services.VideoRepair;

namespace Javbuddy.Components.Shared;

/// <summary>
/// Follows a background <see cref="VideoRepairJob"/> for a component: ticks while it runs
/// (progress lives on the job, written from the ffmpeg thread, so the host re-renders on a timer),
/// then reports how it ended as an error message, or null on success. Following a new job
/// abandons the previous one; the host owns the instance and disposes it.
/// </summary>
/// <param name="invokeAsync">The host's <c>InvokeAsync</c>, so callbacks run on its render context.</param>
/// <param name="interval">Time between ticks; one second by default.</param>
public sealed class VideoJobFollower(Func<Func<Task>, Task> invokeAsync, TimeSpan? interval = null) : IDisposable
{
    private CancellationTokenSource? cts;

    /// <summary>
    /// Starts following <paramref name="job"/>.
    /// </summary>
    /// <param name="job">The job to follow.</param>
    /// <param name="activity">What the job does, capitalised ("Scene detection"), for the failure messages.</param>
    /// <param name="onTick">Runs on the host's render context once per tick while the job runs.</param>
    /// <param name="onFinished">Runs on the host's render context when the job ends, with the error message or null on success.</param>
    public void Follow(VideoRepairJob job, string activity, Func<Task> onTick, Func<string?, Task> onFinished)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        _ = RunAsync(job, activity, onTick, onFinished, cts.Token);
    }

    private async Task RunAsync(VideoRepairJob job, string activity, Func<Task> onTick, Func<string?, Task> onFinished, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(1));
            while (job.Task is { IsCompleted: false } && await timer.WaitForNextTickAsync(ct))
            {
                await invokeAsync(onTick);
            }
            if (ct.IsCancellationRequested || job.Task is null) return;

            var error = await ErrorOfAsync(job.Task, activity);
            await invokeAsync(() => onFinished(error));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<string?> ErrorOfAsync(Task<VideoRepairResult> task, string activity)
    {
        try
        {
            var result = await task;
            return result.Success ? null : result.ErrorMessage ?? $"{activity} failed.";
        }
        catch (OperationCanceledException)
        {
            return $"{activity} was cancelled.";
        }
        catch (Exception)
        {
            return $"{activity} failed; see the server log.";
        }
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}
