using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Warashi;

public interface IWarashiSettingsService
{
    Task<WarashiSettings> GetAsync(CancellationToken ct = default);

    Task SaveAsync(WarashiSettings settings, CancellationToken ct = default);

    /// <summary>Records a finished batch enrichment's outcome, touching only
    /// LastEnrichedAt/LastEnrichSummary so settings edited while the batch ran are kept.</summary>
    Task SaveEnrichmentOutcomeAsync(DateTime enrichedAt, string summary, CancellationToken ct = default);
}

/// <summary>Backs the Settings &gt; Metadata page's WAPdB "Save settings" button — a single-row
/// upsert onto <see cref="WarashiSettings"/> (see <see cref="SingleRowSettingsRepository{TSettings}"/>).</summary>
public class WarashiSettingsService(IDbContextFactory<AppDbContext> dbFactory) : IWarashiSettingsService
{
    private readonly SingleRowSettingsRepository<WarashiSettings> repository = new(dbFactory, db => db.WarashiSettings, x => x.Id,
        (existing, form) =>
        {
            existing.Enabled = form.Enabled;
            existing.BaseUrl = form.BaseUrl;
            existing.RequestDelayMs = form.RequestDelayMs;
            existing.OverwriteExisting = form.OverwriteExisting;
            // LastEnrichedAt/LastEnrichSummary are owned by the batch job (SaveEnrichmentOutcomeAsync),
            // so a form save from a page loaded before the batch finished can't roll them back.
        });

    public Task<WarashiSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(() => new WarashiSettings(), ct);

    public Task SaveAsync(WarashiSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    public Task SaveEnrichmentOutcomeAsync(DateTime enrichedAt, string summary, CancellationToken ct = default) =>
        repository.UpdateAsync(() => new WarashiSettings(), s =>
        {
            s.LastEnrichedAt = enrichedAt;
            s.LastEnrichSummary = summary;
        }, ct);
}
