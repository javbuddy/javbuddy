using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Warashi;

namespace Javbuddy.Services.ActorEnrichment;

/// <summary>Queues Settings &gt; Metadata's "Enrich all actors now" batches on
/// <see cref="BackgroundJobRunner"/>, so a batch keeps running (and reporting to the sidebar via
/// <see cref="TaskActivityTracker"/>) after the user navigates away. Deliberately not an
/// <see cref="IScheduledTask"/>, so it doesn't show up on System &gt; Tasks. On completion only the
/// provider's LastEnrichedAt/LastEnrichSummary are written, never the rest of its settings.</summary>
public sealed class ActorBatchEnrichmentLauncher(BackgroundJobRunner jobs, TaskActivityTracker activities)
{
    private readonly BackgroundJobRunner jobs = jobs;
    private readonly TaskActivityTracker activities = activities;

    public Task QueueWarashi(bool overwriteExisting) =>
        Queue("WAPdB enrichment", WarashiActorMetadataSource.SourceNameConstant, overwriteExisting,
            (services, enrichedAt, summary, ct) =>
                services.GetRequiredService<IWarashiSettingsService>().SaveEnrichmentOutcomeAsync(enrichedAt, summary, ct));

    public Task QueueMinnanoAv(bool overwriteExisting) =>
        Queue("minnano-av.com enrichment", MinnanoAvActorMetadataSource.SourceNameConstant, overwriteExisting,
            (services, enrichedAt, summary, ct) =>
                services.GetRequiredService<IMinnanoAvSettingsService>().SaveEnrichmentOutcomeAsync(enrichedAt, summary, ct));

    public static string BatchProgressLabel(TaskProgress? progress)
    {
        if (progress is not { } p) return "Enriching…";

        var name = string.IsNullOrWhiteSpace(p.Stage) ? null : p.Stage;
        if (p.Total is { } total && total > 0)
        {
            var percentage = Math.Clamp((int)Math.Round((double)p.Current / total * 100), 0, 100);
            return name is not null
                ? $"Enriching actor {p.Current} of {total} ({name})… ({percentage}%)"
                : $"Enriching actor {p.Current} of {total}… ({percentage}%)";
        }

        return name is not null ? $"Enriching {name}…" : "Enriching…";
    }

    private Task Queue(
        string activityName,
        string source,
        bool overwriteExisting,
        Func<IServiceProvider, DateTime, string, CancellationToken, Task> saveOutcome) =>
        jobs.Enqueue(activityName, async (services, ct) =>
        {
            var options = new ActorEnrichmentOptions { Source = source, OverwriteExisting = overwriteExisting };
            var activityId = activities.Start(activityName, "Starting…");
            var progress = new Progress<TaskProgress>(p => activities.Update(activityId, BatchProgressLabel(p)));

            try
            {
                var res = await services.GetRequiredService<IActorEnrichmentService>().EnrichAllAsync(options, progress, ct);
                var summary = $"Enriched {res.Succeeded} of {res.TotalProcessed} actors ({res.TotalFieldsUpdated} fields updated).";

                await saveOutcome(services, DateTime.UtcNow, summary, ct);
                activities.Complete(activityId, summary);
            }
            catch (Exception ex)
            {
                activities.Complete(activityId, $"Batch enrichment failed: {ex.Message}", failed: true);
                throw;
            }
        });
}
