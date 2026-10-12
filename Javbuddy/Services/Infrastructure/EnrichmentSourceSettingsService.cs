using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Single-row settings persistence shared by the scraped actor-metadata sources (see
/// <see cref="SingleRowSettingsRepository{TSettings}"/>). A form save copies only the user-editable
/// fields; LastEnrichedAt/LastEnrichSummary are owned by the batch job
/// (<see cref="SaveEnrichmentOutcomeAsync"/>), so a form save from a page loaded before the batch
/// finished can't roll them back.</summary>
public abstract class EnrichmentSourceSettingsService<TSettings>(
    IDbContextFactory<AppDbContext> dbFactory,
    Func<AppDbContext, DbSet<TSettings>> selectDbSet) where TSettings : class, IEnrichmentSourceSettings, new()
{
    private readonly SingleRowSettingsRepository<TSettings> repository = new(dbFactory, selectDbSet, x => x.Id,
        (existing, form) =>
        {
            existing.Enabled = form.Enabled;
            existing.BaseUrl = form.BaseUrl;
            existing.RequestDelayMs = form.RequestDelayMs;
            existing.OverwriteExisting = form.OverwriteExisting;
        });

    public Task<TSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(() => new TSettings(), ct);

    public Task SaveAsync(TSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    /// <summary>Records a finished batch enrichment's outcome, touching only
    /// LastEnrichedAt/LastEnrichSummary so settings edited while the batch ran are kept.</summary>
    public Task SaveEnrichmentOutcomeAsync(DateTime enrichedAt, string summary, CancellationToken ct = default) =>
        repository.UpdateAsync(() => new TSettings(), s =>
        {
            s.LastEnrichedAt = enrichedAt;
            s.LastEnrichSummary = summary;
        }, ct);
}
