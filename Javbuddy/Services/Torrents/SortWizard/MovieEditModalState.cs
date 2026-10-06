using Javbuddy.Data;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents.SortWizard;

/// <summary>The Review step's metadata editor drawer. Edits a clone of the
/// result's structured MovieViewDto in place — the cast and genre chips add or remove whole
/// ActressViewDto/GenreViewDto entries, so nothing is flattened to text and re-parsed. After a
/// save it asks the review step (via <paramref name="refreshJobData"/>) to re-pull job data, so it
/// doesn't need to know how.</summary>
public sealed class MovieEditModalState(
    WizardSession session,
    ITorrentSortService torrentSortService,
    IDbContextFactory<AppDbContext> dbFactory,
    ISortEditorLookupService lookup,
    TimeProvider time,
    Func<CancellationToken, Task> refreshJobData)
{
    public string? EditingResultId { get; private set; }
    public MovieViewDto? EditingMovie { get; private set; }
    public bool SavingMovie { get; private set; }
    public string? EditModalError { get; private set; }

    /// <summary>The buffer has edits that a re-scrape would discard.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Close was requested with unsaved edits; the editor asks before discarding.</summary>
    public bool ConfirmingDiscard { get; private set; }

    /// <summary>Organize preview of the unsaved buffer (folder and file names).</summary>
    public OrganizePreviewResponseDto? LivePreview { get; private set; }
    public string? LivePreviewError { get; private set; }
    public bool LivePreviewLoading { get; private set; }

    private CancellationTokenSource? previewCts;
    private int previewGeneration;

    /// <summary>Bumped on every open/close, so an await that outlives the editor it started in
    /// (the drawer closed, or another result opened) can tell and drop its late result.</summary>
    private int editGeneration;

    private Dictionary<string, string> fieldSources = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> actressSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which scraper provided each field (javinizer-go field_sources).</summary>
    public IReadOnlyDictionary<string, string> FieldSources => fieldSources;

    /// <summary>Which scraper provided each actress (javinizer-go actress_sources), keyed by
    /// <see cref="SortSourceHelper.ActressSourceKeys"/>.</summary>
    public IReadOnlyDictionary<string, string> ActressSources => actressSources;

    /// <summary>Every scraper's raw values for this result, for comparing a field across sources.</summary>
    public IReadOnlyList<ScraperSourceResultDto> Sources { get; private set; } = [];

    /// <summary>Enabled scrapers offered by "Re-scrape with".</summary>
    public IReadOnlyList<ScraperInfoDto> Scrapers { get; private set; } = [];

    /// <summary>A failed Media action (poster, fanart, screenshot) in the drawer. Those actions are
    /// the Review step's and report through its error, which the page shows behind the overlay.</summary>
    public string? MediaError => session.ReviewError;

    public string? SourcesError { get; private set; }
    public bool SourcesBusy { get; private set; }

    public DateOnly? EditingReleaseDate
    {
        get => EditingMovie?.ReleaseDate is { } d ? DateOnly.FromDateTime(d) : null;
        set
        {
            if (EditingMovie is null) return;
            EditingMovie.ReleaseDate = value?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            EditingMovie.ReleaseYear = value?.Year;
            MarkDirty();
        }
    }

    /// <summary>Called by the editor's plain-field inputs (title, maker, …) after a change.</summary>
    public void MarkDirty()
    {
        IsDirty = true;
        session.NotifyStateChanged();
    }

    public void OpenEditModal(string resultId, MovieViewDto? movie)
    {
        if (movie is null) return;
        editGeneration++;
        EditingResultId = resultId;
        EditingMovie = TorrentSortEditingHelper.Clone(movie);
        EditModalError = null;
        IsDirty = false;
        ConfirmingDiscard = false;
        ResetPreview();
        fieldSources = new(StringComparer.OrdinalIgnoreCase);
        actressSources = new(StringComparer.OrdinalIgnoreCase);
        Sources = [];
        Scrapers = [];
        SourcesError = null;
        SourcesBusy = false;
        // Media actions in the drawer report through ReviewError, which the drawer shows: don't
        // carry an older Review-page error into it.
        session.ReviewError = null;
        session.NotifyStateChanged();
    }

    /// <summary>Esc / the drawer's close button: closes at once when there's nothing to lose,
    /// otherwise asks first; asking again (a second Esc) discards.</summary>
    public void RequestClose()
    {
        if (!IsDirty || ConfirmingDiscard)
        {
            CloseEditModal();
            return;
        }

        ConfirmingDiscard = true;
        session.NotifyStateChanged();
    }

    public void KeepEditing()
    {
        ConfirmingDiscard = false;
        session.NotifyStateChanged();
    }

    public void CloseEditModal()
    {
        editGeneration++;
        EditingResultId = null;
        EditingMovie = null;
        EditModalError = null;
        SourcesBusy = false;
        IsDirty = false;
        ConfirmingDiscard = false;
        ResetPreview();
        session.NotifyStateChanged();
    }

    /// <summary>Debounced (500 ms) organize preview of the unsaved buffer. Each call supersedes the
    /// previous one: a response from an older call is discarded even when it arrives last.</summary>
    public async Task SchedulePreviewAsync()
    {
        if (EditingResultId is null || EditingMovie is null) return;
        previewCts?.Cancel();
        using var cts = previewCts = new CancellationTokenSource();
        var generation = ++previewGeneration;
        var resultId = EditingResultId;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), time, cts.Token);
            if (EditingMovie is null || generation != previewGeneration) return;
            LivePreviewLoading = true;
            session.NotifyStateChanged();

            // A snapshot, so typing while the request is in flight can't change its body.
            var snapshot = TorrentSortEditingHelper.Clone(EditingMovie);
            var result = await torrentSortService.PreviewWithEditsAsync(session.TorrentDownloadId, resultId, session.Destination, snapshot, cts.Token);
            if (generation != previewGeneration) return;
            LivePreview = result.Success ? result.Data : null;
            LivePreviewError = result.Success ? null : result.ErrorMessage ?? "Preview failed.";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(previewCts, cts)) previewCts = null;
            if (generation == previewGeneration && LivePreviewLoading)
            {
                LivePreviewLoading = false;
                session.NotifyStateChanged();
            }
        }
    }

    /// <summary>Loads provenance for the open result: the field-to-source map from the job result,
    /// each source's raw values and the enabled scrapers. Failures only set
    /// <see cref="SourcesError"/>; editing keeps working.</summary>
    public async Task LoadSourcesAsync(BatchFileResultDto? result, CancellationToken ct = default)
    {
        if (EditingResultId is null) return;
        var generation = editGeneration;
        fieldSources = SortSourceHelper.ParseSources(result?.FieldSources);
        actressSources = SortSourceHelper.ParseSources(result?.ActressSources);
        var sourcesTask = torrentSortService.GetResultSourcesAsync(session.TorrentDownloadId, EditingResultId, ct);
        var scrapersTask = torrentSortService.GetScrapersAsync(ct);
        var sources = await sourcesTask;
        var scrapers = await scrapersTask;
        if (generation != editGeneration) return;
        Sources = sources.Results ?? [];
        Scrapers = scrapers.Scrapers?.Where(s => s.Enabled).ToList() ?? [];
        SourcesError = !sources.Success ? sources.ErrorMessage : !scrapers.Success ? scrapers.ErrorMessage : null;
        session.NotifyStateChanged();
    }

    /// <summary>Takes <paramref name="field"/>'s value from <paramref name="source"/>. javinizer-go
    /// persists an override immediately (Cancel doesn't undo it); only that field is copied into
    /// the buffer, so other unsaved edits survive.</summary>
    public async Task<bool> ApplyFieldOverrideAsync(string field, string source, CancellationToken ct = default)
    {
        if (EditingResultId is null || EditingMovie is null) return false;
        var generation = editGeneration;
        SourcesBusy = true;
        SourcesError = null;
        session.NotifyStateChanged();
        try
        {
            var result = await torrentSortService.OverrideFieldAsync(session.TorrentDownloadId, EditingResultId, field, source, ct);
            if (result.Success)
            {
                // The override is already saved: refresh job data so the Review card, a later
                // reopen and the next save's revision all see it, even if the editor moved on.
                await refreshJobData(ct);
            }

            if (generation != editGeneration) return false;
            if (!result.Success || result.Data?.Movie is null || EditingMovie is null)
            {
                SourcesError = result.ErrorMessage ?? "Could not use that source's value.";
                return false;
            }

            SortSourceHelper.CopyField(field, result.Data.Movie, EditingMovie);
            if (result.Data.FieldSources is { } fs) fieldSources = new Dictionary<string, string>(fs, StringComparer.OrdinalIgnoreCase);
            if (result.Data.ActressSources is { } acs) actressSources = new Dictionary<string, string>(acs, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        finally
        {
            if (generation == editGeneration) SourcesBusy = false;
            session.NotifyStateChanged();
        }
    }

    /// <summary>Re-runs the scrape limited to <paramref name="scrapers"/> and reloads the buffer from
    /// the refreshed job, discarding unsaved edits (the editor asks first).</summary>
    public async Task<bool> RescrapeWithAsync(IReadOnlyList<string> scrapers, CancellationToken ct = default)
    {
        if (EditingResultId is null || EditingMovie is null || scrapers.Count == 0) return false;
        var resultId = EditingResultId;
        var generation = editGeneration;
        SourcesBusy = true;
        SourcesError = null;
        session.NotifyStateChanged();
        try
        {
            var code = EditingMovie.Id ?? EditingMovie.Code ?? "";
            var result = await torrentSortService.RescrapeWithScrapersAsync(session.TorrentDownloadId, resultId, code, scrapers, ct);
            if (!result.Success)
            {
                if (generation == editGeneration) SourcesError = result.ErrorMessage ?? "Re-scrape failed.";
                return false;
            }

            await refreshJobData(ct);
            // The drawer was closed or moved to another result meanwhile: don't reopen it.
            if (generation != editGeneration || EditingMovie is null) return false;
            var fresh = session.JobData?.Results?.Values.FirstOrDefault(r => r.ResultId == resultId);
            OpenEditModal(resultId, fresh?.Movie ?? EditingMovie);
            generation = editGeneration;
            await LoadSourcesAsync(fresh, ct);
            return generation == editGeneration;
        }
        finally
        {
            if (generation == editGeneration) SourcesBusy = false;
            session.NotifyStateChanged();
        }
    }

    /// <summary>Sets the preview panel's content directly (component tests).</summary>
    public void SetLivePreview(OrganizePreviewResponseDto? preview, string? error)
    {
        LivePreview = preview;
        LivePreviewError = error;
        session.NotifyStateChanged();
    }

    private void ResetPreview()
    {
        previewCts?.Cancel();
        previewGeneration++;
        LivePreview = null;
        LivePreviewError = null;
        LivePreviewLoading = false;
    }

    /// <summary>Adds <paramref name="actress"/> unless the same person is already in the cast
    /// (<see cref="TorrentSortEditingHelper.IsSameActress"/>).</summary>
    public bool AddActress(ActressViewDto actress)
    {
        if (EditingMovie is null) return false;
        var list = EditingMovie.Actresses ??= [];
        if (list.Any(a => TorrentSortEditingHelper.IsSameActress(a, actress))) return false;
        list.Add(actress);
        MarkDirty();
        return true;
    }

    public async Task<bool> AddTrackedActorAsync(int actorId, CancellationToken ct = default)
    {
        var dto = await lookup.ToActressDtoAsync(actorId, ct);
        return dto is not null && AddActress(dto);
    }

    public void RemoveActressAt(int index)
    {
        if (EditingMovie?.Actresses is not { } list || index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        MarkDirty();
    }

    /// <summary>Adds a genre by name; blank or already-present (case-insensitive) names are ignored.</summary>
    public bool AddGenre(string name)
    {
        if (EditingMovie is null || string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        var list = EditingMovie.Genres ??= [];
        if (list.Any(g => string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase))) return false;
        list.Add(new GenreViewDto { Name = trimmed });
        MarkDirty();
        return true;
    }

    public void RemoveGenre(string name)
    {
        if (EditingMovie?.Genres is not { } list) return;
        if (list.RemoveAll(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) > 0) MarkDirty();
    }

    public async Task SaveEditedMovieAsync(CancellationToken ct = default)
    {
        // An override or re-scrape in flight could be reverted by this PATCH racing it.
        if (EditingResultId is null || EditingMovie is null || SavingMovie || SourcesBusy) return;
        var generation = editGeneration;
        SavingMovie = true;
        EditModalError = null;
        session.NotifyStateChanged();

        try
        {
            // Normalize and merge into a copy: a failed save leaves the drawer showing what was typed.
            var toSave = TorrentSortEditingHelper.Clone(EditingMovie);
            if (toSave.Genres is { Count: > 0 })
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var normalizedNames = await TagNormalization.NormalizeRawValuesAsync(db, toSave.Genres.Select(g => g.Name), ct);
                toSave.Genres = normalizedNames.Select(n => new GenreViewDto { Name = n }).ToList();
            }

            // Media actions in the drawer already went to javinizer-go; take those fields (and the
            // CAS revision they bumped) from the live result rather than the buffer cloned at open.
            var resultId = EditingResultId;
            var live = session.JobData?.Results?.Values.FirstOrDefault(r => r.ResultId == resultId);
            if (live?.Movie is { } liveMovie)
            {
                TorrentSortEditingHelper.CopyMediaFields(liveMovie, toSave);
            }

            var result = await torrentSortService.UpdateResultAsync(session.TorrentDownloadId, resultId, toSave, ct, live?.Revision);
            if (!result.Success)
            {
                if (generation == editGeneration) EditModalError = result.ErrorMessage ?? "Failed to save metadata update.";
                return;
            }

            await refreshJobData(ct);
            if (generation == editGeneration) CloseEditModal();
        }
        finally
        {
            SavingMovie = false;
            session.NotifyStateChanged();
        }
    }
}
