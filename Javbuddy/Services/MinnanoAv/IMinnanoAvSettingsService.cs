using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.MinnanoAv;

public interface IMinnanoAvSettingsService
{
    Task<MinnanoAvSettings> GetAsync(CancellationToken ct = default);

    Task SaveAsync(MinnanoAvSettings settings, CancellationToken ct = default);

    /// <summary>Records a finished batch enrichment's outcome, touching only
    /// LastEnrichedAt/LastEnrichSummary so settings edited while the batch ran are kept.</summary>
    Task SaveEnrichmentOutcomeAsync(DateTime enrichedAt, string summary, CancellationToken ct = default);
}

/// <summary>Backs the Settings &gt; Metadata page's minnano-av.com "Save settings" button — a
/// single-row upsert onto <see cref="MinnanoAvSettings"/> (see <see cref="SingleRowSettingsRepository{TSettings}"/>).</summary>
public class MinnanoAvSettingsService(IDbContextFactory<AppDbContext> dbFactory) : IMinnanoAvSettingsService
{
    private readonly SingleRowSettingsRepository<MinnanoAvSettings> repository = new(dbFactory, db => db.MinnanoAvSettings, x => x.Id,
        (existing, form) =>
        {
            existing.Enabled = form.Enabled;
            existing.BaseUrl = form.BaseUrl;
            existing.RequestDelayMs = form.RequestDelayMs;
            existing.OverwriteExisting = form.OverwriteExisting;
            // LastEnrichedAt/LastEnrichSummary are owned by the batch job (SaveEnrichmentOutcomeAsync),
            // so a form save from a page loaded before the batch finished can't roll them back.
        });

    public Task<MinnanoAvSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(() => new MinnanoAvSettings(), ct);

    public Task SaveAsync(MinnanoAvSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    public Task SaveEnrichmentOutcomeAsync(DateTime enrichedAt, string summary, CancellationToken ct = default) =>
        repository.UpdateAsync(() => new MinnanoAvSettings(), s =>
        {
            s.LastEnrichedAt = enrichedAt;
            s.LastEnrichSummary = summary;
        }, ct);
}
