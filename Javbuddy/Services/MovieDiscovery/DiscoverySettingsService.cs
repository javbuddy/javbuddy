using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.MovieDiscovery;

/// <summary>A registered discovery source and whether the scan includes it.</summary>
public sealed record DiscoverySourceStatus(string SourceName, string? LogoUrl, bool Enabled);

public interface IDiscoverySettingsService
{
    /// <summary>Every registered source with its enabled flag (enabled unless a row says otherwise).</summary>
    Task<IReadOnlyList<DiscoverySourceStatus>> GetSourcesAsync(CancellationToken ct = default);

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
        var disabled = await DisabledSourceNamesAsync(db, ct);
        return sources.Select(s => new DiscoverySourceStatus(s.SourceName, s.Logo?.Url, !disabled.Contains(s.SourceName))).ToList();
    }

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

    /// <summary>Names of sources switched off; the scan skips exactly these.</summary>
    public static async Task<HashSet<string>> DisabledSourceNamesAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.DiscoverySourceSettings.AsNoTracking().Where(s => !s.Enabled).Select(s => s.SourceName).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
