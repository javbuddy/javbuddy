using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.MovieDiscovery;

/// <summary>A registered discovery source and whether the scan includes it.</summary>
public sealed record DiscoverySourceStatus(string SourceName, string? LogoUrl, bool Enabled);

public interface IDiscoverySettingsService
{
    /// <summary>Every registered source with its enabled flag (disabled unless a row says otherwise).</summary>
    Task<IReadOnlyList<DiscoverySourceStatus>> GetSourcesAsync(CancellationToken ct = default);

    /// <summary>Whether discovery is on, i.e. at least one registered source is enabled. Discovery is off until the
    /// user enables a source; the Discover entry points and the scan follow this.</summary>
    Task<bool> IsAnyEnabledAsync(CancellationToken ct = default);

    /// <summary>Saves the enabled flag of each named source; names that aren't registered are ignored.</summary>
    Task SaveAsync(IReadOnlyDictionary<string, bool> enabledBySource, CancellationToken ct = default);
}

/// <summary>Settings &gt; Metadata &gt; Discovery: which studio sources the discovery scan runs.</summary>
public class DiscoverySettingsService(IEnumerable<IStudioDiscoverySource> sources, IDbContextFactory<AppDbContext> dbFactory) : IDiscoverySettingsService
{
    private readonly List<IStudioDiscoverySource> sources = sources.ToList();

    public async Task<IReadOnlyList<DiscoverySourceStatus>> GetSourcesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var enabled = await EnabledSourceNamesAsync(db, ct);
        return sources.Select(s => new DiscoverySourceStatus(s.SourceName, s.Logo?.Url, enabled.Contains(s.SourceName))).ToList();
    }

    public async Task<bool> IsAnyEnabledAsync(CancellationToken ct = default) =>
        (await GetSourcesAsync(ct)).Any(s => s.Enabled);

    public async Task SaveAsync(IReadOnlyDictionary<string, bool> enabledBySource, CancellationToken ct = default)
    {
        var known = sources.Select(s => s.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.DiscoverySourceSettings.ToListAsync(ct);
        foreach (var (name, enabled) in enabledBySource)
        {
            if (!known.Contains(name)) continue;

            var row = rows.FirstOrDefault(r => string.Equals(r.SourceName, name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                db.DiscoverySourceSettings.Add(new DiscoverySourceSetting { SourceName = name, Enabled = enabled });
            }
            else
            {
                row.Enabled = enabled;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Names of sources switched on; the scan runs exactly these (a source with no row is off).</summary>
    public static async Task<HashSet<string>> EnabledSourceNamesAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.DiscoverySourceSettings.AsNoTracking().Where(s => s.Enabled).Select(s => s.SourceName).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
