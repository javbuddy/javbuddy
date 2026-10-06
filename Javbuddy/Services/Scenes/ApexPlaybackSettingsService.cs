using System.Globalization;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Reads the apex playback environment overrides: <c>ApexPlayback__LeadInSeconds</c>
/// and <c>ApexPlayback__TailSeconds</c> (seconds, each overriding just its own Settings field). A value
/// that isn't a number or fails ApexRanges.ValidateWindow is ignored.</summary>
public static class ApexPlaybackEnvConfig
{
    public static double? GetSeconds(IConfiguration configuration, string field)
    {
        if (!double.TryParse(configuration[$"ApexPlayback:{field}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        var error = field == nameof(ApexPlaybackSettings.TailSeconds)
            ? ApexRanges.ValidateWindow(null, value)
            : ApexRanges.ValidateWindow(value, null);
        return error is null ? value : null;
    }
}

public interface IApexPlaybackSettingsService
{
    /// <summary>The default apex window in effect: each side from its environment variable when set, else
    /// the saved row, else <see cref="ApexWindow.Default"/>.</summary>
    Task<ApexWindow> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<ApexPlaybackSettings> GetStoredAsync(CancellationToken ct = default);

    /// <summary>Saves the row; returns an error message when the window is invalid, else null.</summary>
    Task<string?> SaveAsync(ApexPlaybackSettings settings, CancellationToken ct = default);

    /// <summary>Whether a field (by its property name) is set by its environment variable, so the
    /// Settings form shows it read-only.</summary>
    bool IsSetByEnvironment(string field);
}

public sealed class ApexPlaybackSettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IApexPlaybackSettingsService
{
    private readonly SingleRowSettingsRepository<ApexPlaybackSettings> repository = new(dbFactory, db => db.ApexPlaybackSettings, x => x.Id,
        (existing, form) =>
        {
            existing.LeadInSeconds = form.LeadInSeconds;
            existing.TailSeconds = form.TailSeconds;
        });

    public async Task<ApexWindow> GetEffectiveAsync(CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);
        return new ApexWindow(
            ApexPlaybackEnvConfig.GetSeconds(configuration, nameof(ApexPlaybackSettings.LeadInSeconds)) ?? stored.LeadInSeconds,
            ApexPlaybackEnvConfig.GetSeconds(configuration, nameof(ApexPlaybackSettings.TailSeconds)) ?? stored.TailSeconds);
    }

    public Task<ApexPlaybackSettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new ApexPlaybackSettings(), ct);

    public async Task<string?> SaveAsync(ApexPlaybackSettings settings, CancellationToken ct = default)
    {
        if (ApexRanges.ValidateWindow(settings.LeadInSeconds, settings.TailSeconds) is { } error) return error;
        await repository.SaveAsync(settings, ct);
        return null;
    }

    public bool IsSetByEnvironment(string field) => ApexPlaybackEnvConfig.GetSeconds(configuration, field) is not null;
}
