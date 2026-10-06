using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;

namespace Javbuddy.Services.Nfo;

/// <summary>Queues the Movies page's "Write Javbuddy metadata to .nfo" bulk push on
/// <see cref="BackgroundJobRunner"/>, so it keeps running (and reporting to the sidebar via
/// <see cref="TaskActivityTracker"/>) after the user navigates away — the same pattern as
/// ActorBatchEnrichmentLauncher. One push at a time: a second request while one is running is
/// refused rather than queued, since both would race to write the same files.</summary>
public sealed class NfoDriftPushLauncher(BackgroundJobRunner jobs, TaskActivityTracker activities, MovieChangeNotifier movieChanges)
{
    public const string ActivityName = "Write Javbuddy metadata to .nfo";

    private readonly BackgroundJobRunner jobs = jobs;
    private readonly TaskActivityTracker activities = activities;
    private readonly MovieChangeNotifier movieChanges = movieChanges;
    private int running;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    /// <summary>Starts the push over <paramref name="movieIds"/> (a snapshot taken when the user
    /// confirmed) and returns the job, or null when a push is already running.</summary>
    public Task? TryQueue(IReadOnlyList<int> movieIds, bool includeExternal)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return null;

        var activityId = activities.Start(ActivityName, "Starting…");
        return jobs.Enqueue(ActivityName, async (services, ct) =>
        {
            var progress = new Progress<TaskProgress>(p =>
                activities.Update(activityId, p.Total is { } total ? $"Writing .nfo {p.Current} of {total}…" : "Writing .nfo files…"));
            try
            {
                var result = await services.GetRequiredService<INfoSyncService>()
                    .PushNfoDriftAsync(movieIds, includeExternal, progress, ct);
                activities.Complete(activityId, Summary(result));
            }
            catch (Exception ex)
            {
                activities.Complete(activityId, $"Writing .nfo files failed: {ex.Message}", failed: true);
                throw;
            }
            finally
            {
                Volatile.Write(ref running, 0);
                movieChanges.NotifyChanged();
            }
        });
    }

    public static string Summary(NfoDriftPushResult result)
    {
        var parts = new List<string> { $"Wrote {result.Written} .nfo file(s)" };
        if (result.SkippedExternal > 0) parts.Add($"skipped {result.SkippedExternal} with outside edits");
        if (result.StillDrifting > 0) parts.Add($"{result.StillDrifting} still drifting");
        if (result.Failed > 0) parts.Add($"{result.Failed} failed");
        return string.Join("; ", parts) + ".";
    }
}
