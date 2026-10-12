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
/// single-row upsert onto <see cref="MinnanoAvSettings"/>.</summary>
public class MinnanoAvSettingsService(IDbContextFactory<AppDbContext> dbFactory)
    : EnrichmentSourceSettingsService<MinnanoAvSettings>(dbFactory, db => db.MinnanoAvSettings), IMinnanoAvSettingsService;
