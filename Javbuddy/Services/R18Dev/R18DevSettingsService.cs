using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

public interface IR18DevSettingsService
{
    Task<R18DevSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(R18DevSettings settings, CancellationToken ct = default);
}

/// <summary>Settings &gt; Metadata's r18.dev tile: a single-row upsert onto <see cref="R18DevSettings"/>.
/// A save overwrites every field, including the import bookkeeping the page clears when the
/// source is disabled or its dump deleted.</summary>
public class R18DevSettingsService(IDbContextFactory<AppDbContext> dbFactory) : IR18DevSettingsService
{
    private readonly SingleRowSettingsRepository<R18DevSettings> repository = new(dbFactory, db => db.R18DevSettings, x => x.Id,
        (existing, form) =>
        {
            existing.Enabled = form.Enabled;
            existing.DumpSourceOverride = form.DumpSourceOverride;
            existing.LastImportedAt = form.LastImportedAt;
            existing.LastImportSourceDate = form.LastImportSourceDate;
            existing.LastImportSummary = form.LastImportSummary;
        });

    public Task<R18DevSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(() => new R18DevSettings(), ct);

    public Task SaveAsync(R18DevSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);
}
