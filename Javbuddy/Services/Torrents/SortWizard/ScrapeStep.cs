using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Steps 2-3 of the TorrentSort wizard: choosing the destination, starting the batch
/// scrape and polling javinizer-go until it finishes. Owns the scrape poll loop and its activity.</summary>
public sealed class ScrapeStep(
    WizardSession session,
    ITorrentSortService torrentSortService,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private CancellationTokenSource? pollCts;
    private long? scrapeActivityId;

    public IReadOnlyList<DestinationAlias> DestinationAliases { get; private set; } = [];
    public string? SelectedDestinationAliasName { get; private set; }
    public bool NoDestinationAliasesConfigured => DestinationAliases.Count == 0;
    public bool StartingScrape { get; private set; }
    public string? StartError { get; private set; }
    public string? ScrapeError { get; private set; }

    public void LoadDestinationAliases() =>
        DestinationAliases = JavinizerEnvConfig.GetDestinationAliases(configuration);

    /// <summary>Preselects the first configured alias and its path as the destination.</summary>
    public void ApplyDefaultDestination()
    {
        SelectedDestinationAliasName = DestinationAliases.FirstOrDefault()?.Name;
        session.Destination = DestinationAliases.FirstOrDefault()?.Path ?? "";
    }

    /// <summary>After resuming a job: highlights the alias matching the job's destination, falling
    /// back to the first alias (and its path when the job had no destination).</summary>
    public void MatchDestinationToAlias()
    {
        var matchedAlias = DestinationAliases.FirstOrDefault(a => a.Path == session.Destination);
        SelectedDestinationAliasName = matchedAlias?.Name ?? DestinationAliases.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(session.Destination))
        {
            session.Destination = DestinationAliases.FirstOrDefault()?.Path ?? "";
        }
    }

    public void SelectDestinationAlias(string name)
    {
        var alias = DestinationAliases.FirstOrDefault(a => a.Name == name);
        if (alias is null)
        {
            return;
        }

        SelectedDestinationAliasName = alias.Name;
        session.Destination = alias.Path;
        session.NotifyStateChanged();
    }

    public async Task ResumeFromExistingJobAsync(CancellationToken ct = default)
    {
        var poll = await torrentSortService.PollJobAsync(session.TorrentDownloadId, includeData: true, ct);
        if (!poll.Success || poll.Data is null)
        {
            return;
        }

        session.JobData = poll.Data;
        session.Destination = poll.Data.Destination ?? session.Destination;

        session.Step = poll.Data.Status switch
        {
            JavinizerJobStatus.Organized => WizardStep.Done,
            JavinizerJobStatus.Completed or JavinizerJobStatus.Failed => WizardStep.Review,
            _ => WizardStep.Scraping
        };

        session.NotifyStateChanged();

        if (session.Step == WizardStep.Scraping)
        {
            StartPolling();
        }
    }

    public async Task StartBatchScrapeAsync(IReadOnlyCollection<string> selectedFilePaths, CancellationToken ct = default)
    {
        if (selectedFilePaths.Count == 0) return;

        StartingScrape = true;
        StartError = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.StartBatchScrapeAsync(session.TorrentDownloadId, selectedFilePaths.ToList(), session.Destination, ct);
        if (!result.Success)
        {
            StartError = result.ErrorMessage ?? "Could not start the batch scrape.";
            StartingScrape = false;
            session.NotifyStateChanged();
            return;
        }

        StartingScrape = false;
        session.Step = WizardStep.Scraping;
        session.NotifyStateChanged();
        StartPolling();
    }

    public void RetryPolling()
    {
        ScrapeError = null;
        session.NotifyStateChanged();
        StartPolling();
    }

    public void StartPolling()
    {
        StartScrapeActivity();
        pollCts?.Cancel();
        var newPollCts = new CancellationTokenSource();
        pollCts = newPollCts;
        _ = PollLoopAsync(newPollCts);
    }

    private async Task PollLoopAsync(CancellationTokenSource source)
    {
        var ct = source.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), timeProvider);
        try
        {
            do
            {
                var poll = await torrentSortService.PollJobAsync(session.TorrentDownloadId, includeData: false, ct);
                if (!poll.Success || poll.Data is null)
                {
                    ScrapeError = poll.ErrorMessage ?? "Lost contact with javinizer-go.";
                    CompleteScrapeActivity(source, "Metadata scrape failed. Check the server logs for details.", failed: true);
                    session.NotifyStateChanged();
                    return;
                }

                session.JobData = poll.Data;

                if (poll.Data.Status is JavinizerJobStatus.Completed or JavinizerJobStatus.Failed or JavinizerJobStatus.Cancelled)
                {
                    var full = await torrentSortService.PollJobAsync(session.TorrentDownloadId, includeData: true, ct);
                    if (full.Success && full.Data is not null)
                    {
                        session.JobData = full.Data;
                    }

                    session.Step = WizardStep.Review;
                    CompleteScrapeActivity(source, poll.Data.Status switch
                    {
                        JavinizerJobStatus.Completed => "Metadata scrape complete.",
                        JavinizerJobStatus.Cancelled => "Metadata scrape cancelled.",
                        _ => "Metadata scrape finished with errors."
                    }, poll.Data.Status == JavinizerJobStatus.Failed);
                    session.NotifyStateChanged();
                    return;
                }

                session.NotifyStateChanged();
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            CompleteScrapeActivity(source, "Stopped monitoring metadata scrape.");
        }
    }

    private void StartScrapeActivity()
    {
        scrapeActivityId ??= session.StartActivity("Scraping metadata…");
    }

    private void CompleteScrapeActivity(CancellationTokenSource source, string message, bool failed = false)
    {
        if (!ReferenceEquals(pollCts, source)) return;
        if (scrapeActivityId is not { } activityId) return;
        session.CompleteActivity(activityId, message, failed);
        scrapeActivityId = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (pollCts is not null)
        {
            CompleteScrapeActivity(pollCts, "Stopped monitoring metadata scrape.");
            await pollCts.CancelAsync();
            pollCts.Dispose();
            pollCts = null;
        }
    }
}
