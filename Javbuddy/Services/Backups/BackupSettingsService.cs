using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Backups;

/// <summary>Reads the backup environment overrides: <c>Backup__Enabled</c>, <c>Backup__RetentionDays</c> and
/// <c>Backup__FrequencyDays</c>, each overriding just its own Settings field.</summary>
public static class BackupEnvConfig
{
    public static bool? GetBool(IConfiguration configuration, string field) =>
        EnvConfig.GetBool(configuration, $"Backup:{field}");

    public static int? GetDays(IConfiguration configuration, string field) =>
        int.TryParse(configuration[$"Backup:{field}"], out var days) && days >= 1 ? days : null;
}

public interface IBackupSettingsService
{
    /// <summary>The settings in effect: each field from its environment variable when set, else the saved row, else
    /// the defaults.</summary>
    Task<BackupSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The settings in effect without a query once read (the scheduler asks every minute); refreshed on save.</summary>
    BackupSettings GetEffectiveCached();

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<BackupSettings> GetStoredAsync(CancellationToken ct = default);

    Task SaveAsync(BackupSettings settings, CancellationToken ct = default);

    /// <summary>Whether a field (by its property name) is set by its environment variable, so the form shows it read-only.</summary>
    bool IsSetByEnvironment(string field);
}

public sealed class BackupSettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IBackupSettingsService
{
    private readonly SingleRowSettingsRepository<BackupSettings> repository = new(dbFactory, db => db.BackupSettings, x => x.Id,
        (existing, form) =>
        {
            existing.Enabled = form.Enabled;
            existing.RetentionDays = Math.Max(1, form.RetentionDays);
            existing.FrequencyDays = Math.Max(1, form.FrequencyDays);
        });

    private volatile BackupSettings? cached;

    public async Task<BackupSettings> GetEffectiveAsync(CancellationToken ct = default) =>
        cached = Apply(await GetStoredAsync(ct));

    public BackupSettings GetEffectiveCached()
    {
        if (cached is { } known)
        {
            return known;
        }

        using var db = dbFactory.CreateDbContext();
        return cached = Apply(db.BackupSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefault() ?? new BackupSettings());
    }

    public Task<BackupSettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new BackupSettings(), ct);

    public async Task SaveAsync(BackupSettings settings, CancellationToken ct = default)
    {
        // Clamped here rather than only in the repository's copy, which an insert into an empty table bypasses.
        await repository.SaveAsync(new BackupSettings
        {
            Id = settings.Id,
            Enabled = settings.Enabled,
            RetentionDays = Math.Max(1, settings.RetentionDays),
            FrequencyDays = Math.Max(1, settings.FrequencyDays),
        }, ct);
        cached = null;
    }

    public bool IsSetByEnvironment(string field) => field == nameof(BackupSettings.Enabled)
        ? BackupEnvConfig.GetBool(configuration, field) is not null
        : BackupEnvConfig.GetDays(configuration, field) is not null;

    private BackupSettings Apply(BackupSettings stored) => new()
    {
        Id = stored.Id,
        Enabled = BackupEnvConfig.GetBool(configuration, nameof(BackupSettings.Enabled)) ?? stored.Enabled,
        RetentionDays = BackupEnvConfig.GetDays(configuration, nameof(BackupSettings.RetentionDays)) ?? Math.Max(1, stored.RetentionDays),
        FrequencyDays = BackupEnvConfig.GetDays(configuration, nameof(BackupSettings.FrequencyDays)) ?? Math.Max(1, stored.FrequencyDays),
    };
}
