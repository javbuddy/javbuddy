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
/// upsert onto <see cref="WarashiSettings"/>.</summary>
public class WarashiSettingsService(IDbContextFactory<AppDbContext> dbFactory)
    : EnrichmentSourceSettingsService<WarashiSettings>(dbFactory, db => db.WarashiSettings), IWarashiSettingsService;
