using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>Step 4 of the TorrentSort wizard: reviewing scraped results — fix match, exclude, and
/// the poster/cover/screenshot actions — plus re-pulling job data from javinizer-go.</summary>
public sealed class ReviewStep(WizardSession session, ITorrentSortService torrentSortService)
{
    public Dictionary<string, string> FixMatchInputs { get; } = new();
    public HashSet<string> Excluding { get; } = new();
    public HashSet<string> Rescraping { get; } = new();
    public HashSet<string> UpdatingScreenshots { get; } = new();
    public bool BulkExcluding { get; private set; }
    public bool ShowExcludedList { get; set; }
    public bool RefreshingJobData { get; private set; }

    public string GetFixMatchInput(string resultId) =>
        FixMatchInputs.GetValueOrDefault(resultId, "");

    public void SetFixMatchInput(string resultId, string val) =>
        FixMatchInputs[resultId] = val;

    public bool HasReviewableActiveResults()
    {
        if (session.JobData?.Results is null) return false;
        return session.JobData.Results.Values.Any(r => !session.IsExcluded(r));
    }

    public async Task FixMatchAsync(string resultId, CancellationToken ct = default)
    {
        var input = GetFixMatchInput(resultId);
        if (string.IsNullOrWhiteSpace(input)) return;

        Rescraping.Add(resultId);
        session.ReviewError = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.RescrapeAsync(session.TorrentDownloadId, resultId, input.Trim(), ct);
        if (!result.Success)
        {
            session.ReviewError = result.ErrorMessage ?? "Rescrape failed.";
        }
        else
        {
            await RefreshJobDataAsync(ct);
        }

        Rescraping.Remove(resultId);
        session.NotifyStateChanged();
    }

    public async Task ExcludeAsync(string resultId, CancellationToken ct = default)
    {
        Excluding.Add(resultId);
        session.ReviewError = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.ExcludeResultAsync(session.TorrentDownloadId, resultId, ct);
        if (!result.Success)
        {
            session.ReviewError = result.ErrorMessage ?? "Exclude failed.";
        }
        else
        {
            await RefreshJobDataAsync(ct);
        }

        Excluding.Remove(resultId);
        session.NotifyStateChanged();
    }

    public async Task ExcludeAllFailedAsync(CancellationToken ct = default)
    {
        if (session.JobData?.Results is null) return;
        BulkExcluding = true;
        session.ReviewError = null;
        session.NotifyStateChanged();

        var failedResults = session.JobData.Results.Values
            .Where(r => !session.IsExcluded(r) && (r.Status == JavinizerJobStatus.Failed || r.Movie is null))
            .ToList();

        foreach (var r in failedResults)
        {
            await torrentSortService.ExcludeResultAsync(session.TorrentDownloadId, r.ResultId, ct);
        }

        await RefreshJobDataAsync(ct);
        BulkExcluding = false;
        session.NotifyStateChanged();
    }

    /// <summary>Sets the poster via javinizer-go's own dedicated poster-from-url endpoint rather
    /// than the generic whole-movie PATCH used by the other screenshot actions. Confirmed directly
    /// against a real javinizer-go instance: the generic PATCH updates PosterUrl but leaves
    /// CroppedPosterUrl stale, and javinizer-go's own review UI displays CroppedPosterUrl in
    /// preference to PosterUrl — so a poster picked via the generic PATCH looked unchanged there even
    /// though PosterUrl itself was correct. The dedicated endpoint downloads the image server-side and
    /// regenerates CroppedPosterUrl (and clears ShouldCropPoster/crop geometry itself), matching what
    /// javinizer-go's own "set poster from URL" UI action does.</summary>
    public async Task SetPosterFromScreenshotAsync(string resultId, string screenshotUrl, CancellationToken ct = default)
    {
        var movie = session.JobData?.Results?.Values.FirstOrDefault(r => r.ResultId == resultId)?.Movie;
        if (movie is null) return;

        var busyKey = $"{resultId}:{screenshotUrl}";
        UpdatingScreenshots.Add(busyKey);
        session.ReviewError = null;
        session.NotifyStateChanged();

        var result = await torrentSortService.SetPosterFromUrlAsync(session.TorrentDownloadId, resultId, screenshotUrl, ct);
        if (!result.Success)
        {
            session.ReviewError = result.ErrorMessage ?? "Failed to update poster.";
        }
        else
        {
            await RefreshJobDataAsync(ct);
        }

        UpdatingScreenshots.Remove(busyKey);
        session.NotifyStateChanged();
    }

    public async Task SetCoverFromScreenshotAsync(string resultId, string screenshotUrl, CancellationToken ct = default) =>
        await ApplyScreenshotActionAsync(resultId, screenshotUrl, clone => clone.CoverUrl = screenshotUrl, ct);

    public async Task RemoveScreenshotAsync(string resultId, string screenshotUrl, CancellationToken ct = default) =>
        await ApplyScreenshotActionAsync(resultId, screenshotUrl, clone => clone.ScreenshotUrls?.Remove(screenshotUrl), ct);

    private async Task ApplyScreenshotActionAsync(string resultId, string screenshotUrl, Action<MovieViewDto> mutate, CancellationToken ct)
    {
        var movie = session.JobData?.Results?.Values.FirstOrDefault(r => r.ResultId == resultId)?.Movie;
        if (movie is null) return;

        var busyKey = $"{resultId}:{screenshotUrl}";
        UpdatingScreenshots.Add(busyKey);
        session.ReviewError = null;
        session.NotifyStateChanged();

        var clone = TorrentSortEditingHelper.Clone(movie);
        mutate(clone);

        var result = await torrentSortService.UpdateResultAsync(session.TorrentDownloadId, resultId, clone, ct);
        if (!result.Success)
        {
            session.ReviewError = result.ErrorMessage ?? "Failed to update screenshot.";
        }
        else
        {
            await RefreshJobDataAsync(ct);
        }

        UpdatingScreenshots.Remove(busyKey);
        session.NotifyStateChanged();
    }

    public async Task RefreshJobDataAsync(CancellationToken ct = default)
    {
        var poll = await torrentSortService.PollJobAsync(session.TorrentDownloadId, includeData: true, ct);
        if (poll.Success && poll.Data is not null)
        {
            session.JobData = poll.Data;
            session.NotifyStateChanged();
        }
    }

    /// <summary>Manually re-pulls job data from javinizer-go for the Review step's "Refresh" button,
    /// so external edits made via "Edit in javinizer-go" show up without restarting the wizard.
    /// Unlike RefreshJobDataAsync, this reports failures via ReviewError since it isn't chained after
    /// another action that already surfaces its own error.</summary>
    public async Task RefreshReviewAsync(CancellationToken ct = default)
    {
        RefreshingJobData = true;
        session.ReviewError = null;
        session.NotifyStateChanged();

        var poll = await torrentSortService.PollJobAsync(session.TorrentDownloadId, includeData: true, ct);
        if (poll.Success && poll.Data is not null)
        {
            session.JobData = poll.Data;
        }
        else
        {
            session.ReviewError = poll.ErrorMessage ?? "Could not refresh from javinizer-go.";
        }

        RefreshingJobData = false;
        session.NotifyStateChanged();
    }
}
