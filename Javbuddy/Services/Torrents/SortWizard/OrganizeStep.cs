namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 6 of the TorrentSort wizard: running the organize call with a live elapsed-time
/// ticker. Owns the ticker loop.</summary>
public sealed class OrganizeStep(
    WizardSession session,
    ITorrentSortService torrentSortService,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private CancellationTokenSource? organizeTickerCts;

    public bool Organizing { get; private set; }
    public string? OrganizeError { get; private set; }
    public DateTime? OrganizeStartedAt { get; private set; }
    public TimeSpan? OrganizeElapsed => OrganizeStartedAt is null ? null : timeProvider.GetUtcNow().UtcDateTime - OrganizeStartedAt.Value;

    public void ClearError() => OrganizeError = null;

    public async Task OrganizeAsync(CancellationToken ct = default)
    {
        var activityId = session.StartActivity("Organizing files…");
        var activityCompleted = false;
        Organizing = true;
        OrganizeError = null;
        OrganizeStartedAt = timeProvider.GetUtcNow().UtcDateTime;
        session.NotifyStateChanged();

        var tickerCts = new CancellationTokenSource();
        organizeTickerCts = tickerCts;
        var tickerTask = OrganizeTickerLoopAsync(tickerCts.Token);

        try
        {
            var result = await torrentSortService.OrganizeAsync(session.TorrentDownloadId, session.Destination, ct);
            if (!result.Success)
            {
                OrganizeError = result.ErrorMessage ?? "Organize failed.";
                session.CompleteActivity(activityId, OrganizeError, failed: true);
                activityCompleted = true;
                return;
            }

            session.Step = WizardStep.Done;
            session.CompleteActivity(activityId, "Files organized.");
            activityCompleted = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            session.CompleteActivity(activityId, "File organization cancelled.");
            activityCompleted = true;
            throw;
        }
        catch
        {
            session.CompleteActivity(activityId, "File organization failed. Check the server logs for details.", failed: true);
            activityCompleted = true;
            throw;
        }
        finally
        {
            await tickerCts.CancelAsync();
            tickerCts.Dispose();
            if (ReferenceEquals(organizeTickerCts, tickerCts)) organizeTickerCts = null;
            OrganizeStartedAt = null;
            Organizing = false;
            if (!activityCompleted)
            {
                session.CompleteActivity(activityId, "File organization interrupted or failed.", failed: true);
            }
            session.NotifyStateChanged();
        }
    }

    /// <summary>Ticks once a second while an organize call is in flight so the wizard can show a
    /// live elapsed-time readout — javinizer-go's organize job reports no progress percentage,
    /// only a terminal organized/failed status, so this is the only feedback available.</summary>
    private async Task OrganizeTickerLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
            while (await timer.WaitForNextTickAsync(ct))
            {
                session.NotifyStateChanged();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (organizeTickerCts is not null)
        {
            await organizeTickerCts.CancelAsync();
            organizeTickerCts.Dispose();
            organizeTickerCts = null;
        }
    }
}
