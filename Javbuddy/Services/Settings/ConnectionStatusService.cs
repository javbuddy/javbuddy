using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Settings;

/// <summary>One tile's worth of status for Settings &gt; Connections' status grid. Configured is
/// derived from settings presence (env var or DB row with required fields set) — this is not a
/// live connectivity check, which stays a deliberate "Test connection" click inside each
/// integration's own form.</summary>
public record ConnectionStatus(string Key, bool Configured, bool EnvConfigured, string Summary);

public interface IConnectionStatusService
{
    Task<List<ConnectionStatus>> GetStatusesAsync(CancellationToken ct = default);

    /// <summary>Local Library's tile status for Settings &gt; Metadata — kept out of
    /// <see cref="GetStatusesAsync"/> since Connections lists only external services.</summary>
    Task<ConnectionStatus> GetLocalLibraryStatusAsync(CancellationToken ct = default);
}

/// <summary>Backs the Settings &gt; Connections status grid: one read-only status per
/// integration tile, reusing each integration's existing XxxEnvConfig.IsSet/GetXxx accessors
/// rather than re-deriving "is this configured" logic.</summary>
public class ConnectionStatusService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration, IPathMappingService pathMappingService) : IConnectionStatusService
{
    public async Task<List<ConnectionStatus>> GetStatusesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var javinizer = await db.JavinizerSettings.ReadSingleRowAsync(ct);
        var prowlarr = await db.ProwlarrSettings.ReadSingleRowAsync(ct);
        var jellyfin = await db.JellyfinSettings.ReadSingleRowAsync(ct);
        var qbittorrent = await db.QBittorrentSettings.ReadSingleRowAsync(ct);
        var pathMappings = await pathMappingService.GetAllAsync(ct);

        return
        [
            BuildJavinizerStatus(javinizer),
            BuildProwlarrStatus(prowlarr),
            BuildJellyfinStatus(jellyfin),
            BuildQBittorrentStatus(qbittorrent),
            BuildPathMappingsStatus(pathMappings)
        ];
    }

    private ConnectionStatus BuildJavinizerStatus(JavinizerSettings? db)
    {
        var envConfigured = JavinizerEnvConfig.IsSet(configuration);
        var baseUrl = envConfigured ? JavinizerEnvConfig.GetBaseUrl(configuration) : db?.BaseUrl;
        var configured = envConfigured || !string.IsNullOrWhiteSpace(db?.BaseUrl) && !string.IsNullOrWhiteSpace(db?.ApiToken);
        return new ConnectionStatus("javinizer", configured, envConfigured, configured ? baseUrl ?? "Configured" : "Not configured");
    }

    private ConnectionStatus BuildProwlarrStatus(ProwlarrSettings? db)
    {
        var envConfigured = ProwlarrEnvConfig.IsSet(configuration);
        var baseUrl = envConfigured ? ProwlarrEnvConfig.GetBaseUrl(configuration) : db?.BaseUrl;
        var configured = envConfigured || !string.IsNullOrWhiteSpace(db?.BaseUrl) && !string.IsNullOrWhiteSpace(db?.ApiKey);
        return new ConnectionStatus("prowlarr", configured, envConfigured, configured ? baseUrl ?? "Configured" : "Not configured");
    }

    private ConnectionStatus BuildJellyfinStatus(JellyfinSettings? db)
    {
        var envConfigured = JellyfinEnvConfig.IsSet(configuration);
        var envEnabled = JellyfinEnvConfig.GetEnabled(configuration);
        var isEnabled = envEnabled ?? (db?.Enabled ?? true);

        if (!isEnabled)
        {
            return new ConnectionStatus("jellyfin", false, envEnabled.HasValue, "Disabled");
        }

        var baseUrl = envConfigured ? JellyfinEnvConfig.GetBaseUrl(configuration) : db?.BaseUrl;
        var configured = envConfigured || !string.IsNullOrWhiteSpace(db?.BaseUrl) && !string.IsNullOrWhiteSpace(db?.ApiKey);

        var envLibraryNames = JellyfinEnvConfig.GetSelectedLibraryNames(configuration);
        var selectedLibraryCount = envLibraryNames.Count > 0
            ? envLibraryNames.Count
            : JellyfinClient.ParseLibraryNames(db?.SelectedLibraryNames).Count;

        string summary;
        if (!configured)
        {
            summary = "Not configured";
        }
        else if (selectedLibraryCount == 0)
        {
            summary = "No libraries selected";
        }
        else
        {
            summary = $"{selectedLibraryCount} librar{(selectedLibraryCount == 1 ? "y" : "ies")} selected";
        }

        return new ConnectionStatus("jellyfin", configured, envConfigured, summary);
    }

    public async Task<ConnectionStatus> GetLocalLibraryStatusAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var localLibrary = await db.LocalLibrarySettings.ReadSingleRowAsync(ct);
        return BuildLocalLibraryStatus(localLibrary);
    }

    private ConnectionStatus BuildLocalLibraryStatus(LocalLibrarySettings? db)
    {
        var envConfigured = LocalLibraryEnvConfig.IsSet(configuration);
        var rootPathCount = envConfigured
            ? LocalLibraryEnvConfig.GetRootPaths(configuration).Count
            : (db?.RootPaths ?? "").Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var configured = rootPathCount > 0;
        return new ConnectionStatus("locallibrary", configured, envConfigured, configured ? $"{rootPathCount} root path{(rootPathCount == 1 ? "" : "s")} configured" : "Not configured");
    }

    private ConnectionStatus BuildQBittorrentStatus(QBittorrentSettings? db)
    {
        var envConfigured = QBittorrentEnvConfig.IsSet(configuration);
        var baseUrl = envConfigured ? QBittorrentEnvConfig.GetBaseUrl(configuration) : db?.BaseUrl;
        var configured = envConfigured || !string.IsNullOrWhiteSpace(db?.BaseUrl) && !string.IsNullOrWhiteSpace(db?.Username) && !string.IsNullOrWhiteSpace(db?.Password);
        return new ConnectionStatus("qbittorrent", configured, envConfigured, configured ? baseUrl ?? "Configured" : "Not configured");
    }

    private static ConnectionStatus BuildPathMappingsStatus(List<PathMapping> pathMappings)
    {
        var count = pathMappings.Count;
        return new ConnectionStatus("pathmappings", count > 0, false, count > 0 ? $"{count} rule{(count == 1 ? "" : "s")}" : "No mappings");
    }
}
