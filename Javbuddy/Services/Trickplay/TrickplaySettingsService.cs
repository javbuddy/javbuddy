using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>Reads the trickplay environment overrides: <c>Trickplay__GenerateForNewFiles</c>,
/// <c>Trickplay__KeyframeOnly</c> and <c>Trickplay__JellyfinFallback</c> (true/false, each
/// overriding just its own Settings field).</summary>
public static class TrickplayEnvConfig
{
    public static bool? GetBool(IConfiguration configuration, string field) =>
        bool.TryParse(configuration[$"Trickplay:{field}"], out var value) ? value : null;
}

public interface ITrickplaySettingsService
{
    /// <summary>The settings in effect: each field from its environment variable when set, else the
    /// saved row, else the defaults.</summary>
    Task<TrickplaySettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<TrickplaySettings> GetStoredAsync(CancellationToken ct = default);

    Task SaveAsync(TrickplaySettings settings, CancellationToken ct = default);

    /// <summary>Whether a field (by its property name) is set by its environment variable, so
    /// the Settings form shows it read-only.</summary>
    bool IsSetByEnvironment(string field);
}

public sealed class TrickplaySettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : ITrickplaySettingsService
{
    private readonly SingleRowSettingsRepository<TrickplaySettings> repository = new(dbFactory, db => db.TrickplaySettings, x => x.Id,
        (existing, form) =>
        {
            existing.GenerateForNewFiles = form.GenerateForNewFiles;
            existing.KeyframeOnly = form.KeyframeOnly;
            existing.JellyfinFallback = form.JellyfinFallback;
        });

    public async Task<TrickplaySettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);
        return new TrickplaySettings
        {
            Id = stored.Id,
            GenerateForNewFiles = TrickplayEnvConfig.GetBool(configuration, nameof(TrickplaySettings.GenerateForNewFiles)) ?? stored.GenerateForNewFiles,
            KeyframeOnly = TrickplayEnvConfig.GetBool(configuration, nameof(TrickplaySettings.KeyframeOnly)) ?? stored.KeyframeOnly,
            JellyfinFallback = TrickplayEnvConfig.GetBool(configuration, nameof(TrickplaySettings.JellyfinFallback)) ?? stored.JellyfinFallback,
        };
    }

    public Task<TrickplaySettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new TrickplaySettings(), ct);

    public Task SaveAsync(TrickplaySettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    public bool IsSetByEnvironment(string field) => TrickplayEnvConfig.GetBool(configuration, field) is not null;
}
