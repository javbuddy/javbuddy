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
        int.TryParse(configuration[$"Backup:{field}"], out var days) && days >= 1 ? Math.Min(days, BackupSettings.MaxDays) : null;
}

public interface IBackupSettingsService
{
    /// <summary>The settings in effect: each field from its environment variable when set, else the saved row, else
    /// the defaults.</summary>
    Task<BackupSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The settings in effect, never querying the database (the scheduler asks every minute on its own loop):
    /// the last ones read or saved, seeded by <see cref="GetEffectiveAsync"/> at startup. Until then the defaults with
    /// the environment overrides.</summary>
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
            existing.RetentionDays = ClampDays(form.RetentionDays);
            existing.FrequencyDays = ClampDays(form.FrequencyDays);
        });

    private readonly Lock cacheGate = new();
    private volatile BackupSettings? cached;
    private int version;

    public async Task<BackupSettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var versionBefore = Volatile.Read(ref version);
        var effective = Apply(await GetStoredAsync(ct));
        // A save that landed while this read was in flight already cached newer values; don't overwrite them.
        lock (cacheGate)
        {
            if (versionBefore == version) cached = effective;
        }
        return effective;
    }

    public BackupSettings GetEffectiveCached()
    {
        if (cached is { } known)
        {
            return known;
        }

        return Apply(new BackupSettings());
    }

    public Task<BackupSettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new BackupSettings(), ct);

    public async Task SaveAsync(BackupSettings settings, CancellationToken ct = default)
    {
        // Clamped here rather than only in the repository's copy, which an insert into an empty table bypasses.
        var clamped = new BackupSettings
        {
            Id = settings.Id,
            Enabled = settings.Enabled,
            RetentionDays = ClampDays(settings.RetentionDays),
            FrequencyDays = ClampDays(settings.FrequencyDays),
        };
        await repository.SaveAsync(clamped, ct);
        lock (cacheGate)
        {
            version++;
            cached = Apply(clamped);
        }
    }

    public bool IsSetByEnvironment(string field) => field == nameof(BackupSettings.Enabled)
        ? BackupEnvConfig.GetBool(configuration, field) is not null
        : BackupEnvConfig.GetDays(configuration, field) is not null;

    private static int ClampDays(int days) => Math.Clamp(days, 1, BackupSettings.MaxDays);

    private BackupSettings Apply(BackupSettings stored) => new()
    {
        Id = stored.Id,
        Enabled = BackupEnvConfig.GetBool(configuration, nameof(BackupSettings.Enabled)) ?? stored.Enabled,
        RetentionDays = BackupEnvConfig.GetDays(configuration, nameof(BackupSettings.RetentionDays)) ?? ClampDays(stored.RetentionDays),
        FrequencyDays = BackupEnvConfig.GetDays(configuration, nameof(BackupSettings.FrequencyDays)) ?? ClampDays(stored.FrequencyDays),
    };
}
