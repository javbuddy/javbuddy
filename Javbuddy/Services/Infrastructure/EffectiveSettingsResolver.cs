using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Collapses the "environment variables override the Settings page, else fall back to
/// the single-row DB settings table (or null if required fields are blank)" pattern that was
/// otherwise reimplemented per integration. <paramref name="buildFromEnv"/> and
/// <paramref name="hasRequiredFields"/> use the same field set: if the env-built settings already
/// satisfy <paramref name="hasRequiredFields"/>, env wins outright; otherwise the DB row is used,
/// subject to the same required-fields check.</summary>
public sealed class EffectiveSettingsResolver<TSettings>(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    Func<IConfiguration, TSettings> buildFromEnv,
    Func<AppDbContext, CancellationToken, Task<TSettings?>> queryDb,
    Func<TSettings, bool> hasRequiredFields) where TSettings : class
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;
    private readonly Func<IConfiguration, TSettings> buildFromEnv = buildFromEnv;
    private readonly Func<AppDbContext, CancellationToken, Task<TSettings?>> queryDb = queryDb;
    private readonly Func<TSettings, bool> hasRequiredFields = hasRequiredFields;

    public async Task<TSettings?> ResolveAsync(CancellationToken ct)
    {
        var envSettings = buildFromEnv(configuration);
        if (hasRequiredFields(envSettings))
        {
            return envSettings;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await queryDb(db, ct);
        return settings is not null && hasRequiredFields(settings) ? settings : null;
    }
}
