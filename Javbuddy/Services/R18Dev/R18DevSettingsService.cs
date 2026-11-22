using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

/// <summary>Reads the r18.dev environment override <c>R18Dev__Enabled</c> (true/false), which
/// overrides just its own Settings field.</summary>
public static class R18DevEnvConfig
{
    public static bool? GetEnabled(IConfiguration configuration) =>
        EnvConfig.GetBool(configuration, $"R18Dev:{nameof(R18DevSettings.Enabled)}");
}

public interface IR18DevSettingsService
{
    /// <summary>The settings in effect: <c>Enabled</c> from its environment variable when set, else the
    /// saved row, else the defaults.</summary>
    Task<R18DevSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<R18DevSettings> GetStoredAsync(CancellationToken ct = default);

    Task SaveAsync(R18DevSettings settings, CancellationToken ct = default);

    /// <summary>Whether <c>Enabled</c> is set by its environment variable, so the Settings form
    /// shows it read-only.</summary>
    bool IsEnabledSetByEnvironment { get; }
}

/// <summary>Settings &gt; Metadata's r18.dev tile: a single-row upsert onto <see cref="R18DevSettings"/>.
/// A save overwrites every field, including the import bookkeeping the page clears when the
/// source is disabled or its dump deleted.</summary>
public class R18DevSettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IR18DevSettingsService
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

    public async Task<R18DevSettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var settings = await GetStoredAsync(ct);
        if (R18DevEnvConfig.GetEnabled(configuration) is { } enabled) settings.Enabled = enabled;
        return settings;
    }

    public Task<R18DevSettings> GetStoredAsync(CancellationToken ct = default) => repository.GetAsync(() => new R18DevSettings(), ct);

    public Task SaveAsync(R18DevSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    public bool IsEnabledSetByEnvironment => R18DevEnvConfig.GetEnabled(configuration) is not null;
}
