using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;

namespace Javbuddy.Services.Tags;

public enum TagLibraryPass
{
    /// <summary>Reprocess every movie's genres: the initial discovery/backfill pass.</summary>
    Discovery,

    /// <summary>Apply the replacement rules to every movie.</summary>
    ApplyRules,

    /// <summary>Strip the ignore list's tags from every movie.</summary>
    StripIgnored,
}

/// <summary>Runs the Tags page's whole-library passes (discovery, "Apply Rules to Library", "Strip Ignored Tags")
/// on <see cref="BackgroundJobRunner"/> instead of inside the click handler, so the work keeps going — and reports
/// to the sidebar via <see cref="TaskActivityTracker"/> — after the user leaves the page. One pass at a time: a
/// second request while one is running is refused, since both would rewrite the same movies' tags.</summary>
public sealed class TagLibraryPassLauncher(BackgroundJobRunner jobs, TaskActivityTracker activities)
{
    private readonly BackgroundJobRunner jobs = jobs;
    private readonly TaskActivityTracker activities = activities;
    private int running;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    public static string ActivityName(TagLibraryPass pass) => pass switch
    {
        TagLibraryPass.Discovery => "Tag discovery",
        TagLibraryPass.ApplyRules => "Apply tag rules to library",
        _ => "Strip ignored tags from library",
    };

    /// <summary>Starts the pass and returns a task for the number of movies reprocessed (faulted when the pass
    /// failed), or null when a pass is already running.</summary>
    public Task<int>? TryQueue(TagLibraryPass pass)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return null;

        var name = ActivityName(pass);
        var activityId = activities.Start(name, "Running…");
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = jobs.Enqueue(name, async (services, ct) =>
        {
            try
            {
                var rules = services.GetRequiredService<ITagRuleService>();
                var count = pass switch
                {
                    TagLibraryPass.Discovery => await rules.RunDiscoveryAsync(ct),
                    TagLibraryPass.ApplyRules => await rules.ApplyReplacementRulesToLibraryAsync(ct),
                    _ => await rules.StripIgnoredTagsFromLibraryAsync(ct),
                };
                activities.Complete(activityId, $"{count} movie{(count == 1 ? "" : "s")} reprocessed.");
                Volatile.Write(ref running, 0);
                result.SetResult(count);
            }
            catch (Exception ex)
            {
                activities.Complete(activityId, $"{name} failed: {ex.Message}", failed: true);
                Volatile.Write(ref running, 0);
                result.SetException(ex);
                throw;
            }
        });
        return result.Task;
    }
}
