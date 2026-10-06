using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.DeoVr;

/// <summary>Reads the DeoVR environment override <c>DeoVr__Enabled</c> (true/false), which
/// overrides just its own Settings field.</summary>
public static class DeoVrEnvConfig
{
    public static string? Get(IConfiguration configuration, string field) =>
        configuration[$"DeoVr:{field}"] is { Length: > 0 } value ? value : null;

    public static bool? GetBool(IConfiguration configuration, string field) =>
        bool.TryParse(Get(configuration, field), out var value) ? value : null;
}

public interface IDeoVrSettingsService
{
    /// <summary>The settings in effect: each field from its environment variable when set, else the
    /// saved row, else the defaults.</summary>
    Task<DeoVrSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<DeoVrSettings> GetStoredAsync(CancellationToken ct = default);

    Task SaveAsync(DeoVrSettings settings, CancellationToken ct = default);

    /// <summary>Whether a field (by its property name) is set by its environment variable, so
    /// the Settings form shows it read-only.</summary>
    bool IsSetByEnvironment(string field);
}

public sealed class DeoVrSettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IDeoVrSettingsService
{
    private readonly SingleRowSettingsRepository<DeoVrSettings> repository = new(dbFactory, db => db.DeoVrSettings, x => x.Id,
        (existing, form) => existing.Enabled = form.Enabled);

    public async Task<DeoVrSettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);
        return new DeoVrSettings
        {
            Id = stored.Id,
            Enabled = DeoVrEnvConfig.GetBool(configuration, nameof(DeoVrSettings.Enabled)) ?? stored.Enabled,
        };
    }

    public Task<DeoVrSettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new DeoVrSettings(), ct);

    public Task SaveAsync(DeoVrSettings settings, CancellationToken ct = default) =>
        repository.SaveAsync(settings, ct);

    public bool IsSetByEnvironment(string field) => field == nameof(DeoVrSettings.Enabled)
        ? DeoVrEnvConfig.GetBool(configuration, field) is not null
        : DeoVrEnvConfig.Get(configuration, field) is not null;
}
