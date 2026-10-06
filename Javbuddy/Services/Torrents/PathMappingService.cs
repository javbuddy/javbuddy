using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents;

/// <summary>The single env/appsettings-configured path mapping — see <see cref="PathMappingEnvConfig"/>.
/// Only one is supported via env vars (unlike the DB-backed Settings page, which allows several) —
/// a deployment only ever has one qBittorrent/javinizer-go path-layout pairing to configure.</summary>
public record PathMappingEntry(string QBittorrentPrefix, string JavinizerPrefix, string? AppPrefix);

/// <summary>Reads the env/appsettings-configured path mapping ("PathMapping:QBittorrentPrefix",
/// "PathMapping:JavinizerPrefix", "PathMapping:AppPrefix" — standard .NET env form
/// "PathMapping__QBittorrentPrefix" etc.), which takes precedence over the Settings page when set.</summary>
public static class PathMappingEnvConfig
{
    public static PathMappingEntry? GetMapping(IConfiguration configuration)
    {
        var qbittorrentPrefix = configuration["PathMapping:QBittorrentPrefix"];
        var javinizerPrefix = configuration["PathMapping:JavinizerPrefix"];
        if (string.IsNullOrWhiteSpace(qbittorrentPrefix) || string.IsNullOrWhiteSpace(javinizerPrefix)) return null;

        var appPrefix = configuration["PathMapping:AppPrefix"];
        return new PathMappingEntry(qbittorrentPrefix, javinizerPrefix, string.IsNullOrWhiteSpace(appPrefix) ? null : appPrefix);
    }

    public static bool IsSet(IConfiguration configuration) => GetMapping(configuration) is not null;
}

public interface IPathMappingService
{
    Task<List<PathMapping>> GetAllAsync(CancellationToken ct = default);
    Task<PathMapping> AddAsync(string qbittorrentPrefix, string javinizerPrefix, string? appPrefix, CancellationToken ct = default);
    Task UpdateAsync(int id, string qbittorrentPrefix, string javinizerPrefix, string? appPrefix, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Rewrites path using the longest matching configured prefix (qBittorrent-side →
    /// javinizer-go-side), matched on a path-segment boundary so e.g. "/downloads/javinizarr"
    /// doesn't also match "/downloads/javinizarr2/...". Returns path unchanged if no configured
    /// prefix matches.</summary>
    Task<string> TranslateAsync(string path, CancellationToken ct = default);

    /// <summary>Rewrites path using the longest matching configured prefix (qBittorrent-side →
    /// Javbuddy's own process view), for callers that need to open the file directly (see the VR
    /// part-merge step). Returns path unchanged if no configured mapping has an AppPrefix.</summary>
    Task<string> TranslateToAppPathAsync(string path, CancellationToken ct = default);

    /// <summary>Rewrites path using the longest matching configured prefix (javinizer-go-side →
    /// Javbuddy's own process view), for callers matching an organize destination against a
    /// filesystem path from another service (see the post-organize Jellyfin library sync).
    /// Returns path unchanged if no configured mapping has an AppPrefix.</summary>
    Task<string> TranslateJavinizerPathToAppPathAsync(string path, CancellationToken ct = default);
}

/// <summary>Manages user-configured path-prefix rewrite rules bridging qBittorrent's filesystem
/// view of a download to javinizer-go's own filesystem view of the same file — the two commonly
/// run on different hosts/containers with different mounts.</summary>
public class PathMappingService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IPathMappingService
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;

    public async Task<List<PathMapping>> GetAllAsync(CancellationToken ct = default)
    {
        var envMapping = PathMappingEnvConfig.GetMapping(configuration);
        if (envMapping is not null)
        {
            return
            [
                new PathMapping
                {
                    QBittorrentPrefix = TrimTrailingSlashes(envMapping.QBittorrentPrefix),
                    JavinizerPrefix = TrimTrailingSlashes(envMapping.JavinizerPrefix),
                    AppPrefix = string.IsNullOrWhiteSpace(envMapping.AppPrefix) ? null : TrimTrailingSlashes(envMapping.AppPrefix)
                }
            ];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PathMappings.AsNoTracking().OrderBy(m => m.QBittorrentPrefix).ToListAsync(ct);
    }

    public async Task<PathMapping> AddAsync(string qbittorrentPrefix, string javinizerPrefix, string? appPrefix, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mapping = new PathMapping
        {
            QBittorrentPrefix = TrimTrailingSlashes(qbittorrentPrefix),
            JavinizerPrefix = TrimTrailingSlashes(javinizerPrefix),
            AppPrefix = string.IsNullOrWhiteSpace(appPrefix) ? null : TrimTrailingSlashes(appPrefix)
        };
        db.PathMappings.Add(mapping);
        await db.SaveChangesAsync(ct);
        return mapping;
    }

    public async Task UpdateAsync(int id, string qbittorrentPrefix, string javinizerPrefix, string? appPrefix, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.PathMappings.FindAsync([id], ct);
        if (existing is null) return;

        existing.QBittorrentPrefix = TrimTrailingSlashes(qbittorrentPrefix);
        existing.JavinizerPrefix = TrimTrailingSlashes(javinizerPrefix);
        existing.AppPrefix = string.IsNullOrWhiteSpace(appPrefix) ? null : TrimTrailingSlashes(appPrefix);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.PathMappings.FindAsync(new object[] { id }, ct);
        if (existing is null) return;

        db.PathMappings.Remove(existing);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> TranslateAsync(string path, CancellationToken ct = default) =>
        await TranslateAsync(path, m => m.QBittorrentPrefix, m => m.JavinizerPrefix, ct);

    public async Task<string> TranslateToAppPathAsync(string path, CancellationToken ct = default) =>
        await TranslateAsync(path, m => m.QBittorrentPrefix, m => m.AppPrefix, ct);

    public async Task<string> TranslateJavinizerPathToAppPathAsync(string path, CancellationToken ct = default) =>
        await TranslateAsync(path, m => m.JavinizerPrefix, m => m.AppPrefix, ct);

    private async Task<string> TranslateAsync(
        string path,
        Func<PathMapping, string?> sourcePrefixSelector,
        Func<PathMapping, string?> targetPrefixSelector,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        var mappings = await GetAllAsync(ct);
        PathMapping? best = null;
        string? bestSourcePrefix = null;

        foreach (var mapping in mappings)
        {
            var prefix = sourcePrefixSelector(mapping);
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(targetPrefixSelector(mapping))) continue;

            var isSegmentMatch = path.Length == prefix.Length || (prefix.Length < path.Length && path[prefix.Length] is '/' or '\\');
            if (!isSegmentMatch || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            if (best is null || prefix.Length > bestSourcePrefix!.Length)
            {
                best = mapping;
                bestSourcePrefix = prefix;
            }
        }

        if (best is null) return path;

        var remainder = path[bestSourcePrefix!.Length..];
        return targetPrefixSelector(best) + remainder;
    }

    private static string TrimTrailingSlashes(string value) => value.TrimEnd('/', '\\');
}
