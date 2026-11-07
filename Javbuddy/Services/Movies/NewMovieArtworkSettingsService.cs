using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>Reads the new-movie artwork environment overrides: <c>NewMovieArtwork__GenerateExtraFanart</c> and
/// <c>NewMovieArtwork__GeneratePosterAndFanart</c> (true/false, each overriding just its own Settings field).</summary>
public static class NewMovieArtworkEnvConfig
{
    public static bool? GetBool(IConfiguration configuration, string field) =>
        EnvConfig.GetBool(configuration, $"NewMovieArtwork:{field}");
}

public interface INewMovieArtworkSettingsService
{
    /// <summary>The settings in effect: each field from its environment variable when set, else the saved row, else the defaults.</summary>
    Task<NewMovieArtworkSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The saved row (or the defaults), for the Settings form.</summary>
    Task<NewMovieArtworkSettings> GetStoredAsync(CancellationToken ct = default);

    Task SaveAsync(NewMovieArtworkSettings settings, CancellationToken ct = default);

    /// <summary>Whether a field (by its property name) is set by its environment variable, so the Settings form shows it read-only.</summary>
    bool IsSetByEnvironment(string field);
}

public sealed class NewMovieArtworkSettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : INewMovieArtworkSettingsService
{
    private readonly SingleRowSettingsRepository<NewMovieArtworkSettings> repository = new(dbFactory, db => db.NewMovieArtworkSettings, x => x.Id,
        (existing, form) =>
        {
            existing.GenerateExtraFanart = form.GenerateExtraFanart;
            existing.GeneratePosterAndFanart = form.GeneratePosterAndFanart;
        });

    public async Task<NewMovieArtworkSettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);
        return new NewMovieArtworkSettings
        {
            Id = stored.Id,
            GenerateExtraFanart = NewMovieArtworkEnvConfig.GetBool(configuration, nameof(NewMovieArtworkSettings.GenerateExtraFanart)) ?? stored.GenerateExtraFanart,
            GeneratePosterAndFanart = NewMovieArtworkEnvConfig.GetBool(configuration, nameof(NewMovieArtworkSettings.GeneratePosterAndFanart)) ?? stored.GeneratePosterAndFanart,
        };
    }

    public Task<NewMovieArtworkSettings> GetStoredAsync(CancellationToken ct = default) =>
        repository.GetAsync(() => new NewMovieArtworkSettings(), ct);

    public Task SaveAsync(NewMovieArtworkSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);

    public bool IsSetByEnvironment(string field) => NewMovieArtworkEnvConfig.GetBool(configuration, field) is not null;
}
