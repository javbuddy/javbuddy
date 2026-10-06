using Javbuddy.Data;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Monitoring;

public sealed record ConfiguredIntegrations(bool Javinizer, bool Prowlarr, bool QBittorrent, bool Jellyfin)
{
    public static ConfiguredIntegrations None { get; } = new(false, false, false, false);
}

public sealed record SubServiceHealth(string Name, bool Configured, bool Success, string Message, string? Url, string? Version = null);

public sealed record SystemHealthReport(string MigrationVersion, IReadOnlyList<string> Messages, ConfiguredIntegrations Configured);

public interface ISystemStatusService
{
    Task<SystemHealthReport> GetHealthReportAsync(CancellationToken ct = default);
    Task<ConfiguredIntegrations> GetConfiguredAsync(CancellationToken ct = default);
    Task<List<SubServiceHealth>> GetSubServiceHealthAsync(ConfiguredIntegrations configured, CancellationToken ct = default);
}

/// <summary>System &gt; Status' checks: migration state, which integrations are configured (from
/// <see cref="IConnectionStatusService"/>, so "configured" has one definition) and a live
/// connection test of each configured one.</summary>
public class SystemStatusService(
    IDbContextFactory<AppDbContext> dbFactory,
    IConnectionStatusService connectionStatusService,
    IJavinizerClient javinizerClient,
    IProwlarrClient prowlarrClient,
    IQBittorrentClient qBittorrentClient,
    IJellyfinClient jellyfinClient) : ISystemStatusService
{
    public async Task<SystemHealthReport> GetHealthReportAsync(CancellationToken ct = default)
    {
        var messages = new List<string>();

        string migrationVersion;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            migrationVersion = appliedMigrations.Count == 0 ? "none applied" : appliedMigrations.Count.ToString();

            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pendingMigrations.Count > 0)
            {
                messages.Add($"{pendingMigrations.Count} pending EF Core migration(s) have not been applied: {string.Join(", ", pendingMigrations)}.");
            }
        }

        var configured = await GetConfiguredAsync(ct);

        if (!configured.Javinizer)
        {
            messages.Add("javinizer-go is not configured — metadata lookups will fail until you add a Base URL and API token in Settings.");
        }
        if (!configured.Prowlarr)
        {
            messages.Add("Prowlarr is not configured — release search will not work until you configure it in Settings.");
        }
        if (!configured.QBittorrent)
        {
            messages.Add("qBittorrent is not configured — grabbing releases will fail until you configure it in Settings.");
        }

        if (configured.Jellyfin)
        {
            var libraryNames = await jellyfinClient.GetSelectedLibraryNamesAsync(ct);
            if (libraryNames.Count == 0)
            {
                messages.Add("No Jellyfin libraries are selected — Jellyfin lookups are skipped until you choose at least one library in Settings.");
            }
        }

        return new SystemHealthReport(migrationVersion, messages, configured);
    }

    public async Task<ConfiguredIntegrations> GetConfiguredAsync(CancellationToken ct = default)
    {
        var statuses = await connectionStatusService.GetStatusesAsync(ct);
        bool IsConfigured(string key) => statuses.Any(s => s.Key == key && s.Configured);

        return new ConfiguredIntegrations(
            IsConfigured("javinizer"), IsConfigured("prowlarr"), IsConfigured("qbittorrent"), IsConfigured("jellyfin"));
    }

    public async Task<List<SubServiceHealth>> GetSubServiceHealthAsync(ConfiguredIntegrations configured, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(
            CheckAsync("javinizer-go", configured.Javinizer, async () =>
            {
                var result = javinizerClient.TestConnectionAsync(ct);
                var url = javinizerClient.GetExternalUrlAsync(ct);
                await Task.WhenAll(result, url);
                return new SubServiceHealth("javinizer-go", true, result.Result.Success, result.Result.Message, url.Result, result.Result.Version);
            }),
            CheckAsync("Prowlarr", configured.Prowlarr, async () =>
            {
                var result = prowlarrClient.TestConnectionAsync(ct);
                var url = prowlarrClient.GetExternalUrlAsync(ct);
                await Task.WhenAll(result, url);
                return new SubServiceHealth("Prowlarr", true, result.Result.Success, result.Result.Message, url.Result, result.Result.Version);
            }),
            CheckAsync("qBittorrent", configured.QBittorrent, async () =>
            {
                var result = qBittorrentClient.TestConnectionAsync(ct);
                var url = qBittorrentClient.GetExternalUrlAsync(ct);
                await Task.WhenAll(result, url);
                return new SubServiceHealth("qBittorrent", true, result.Result.Success, result.Result.Message, url.Result, result.Result.Version);
            }),
            CheckAsync("Jellyfin", configured.Jellyfin, async () =>
            {
                var result = jellyfinClient.TestConnectionAsync(ct);
                var url = jellyfinClient.GetExternalUrlAsync(ct);
                await Task.WhenAll(result, url);
                return new SubServiceHealth("Jellyfin", true, result.Result.Success, result.Result.Message, url.Result, result.Result.Version);
            }));

        return [.. results];
    }

    private static Task<SubServiceHealth> CheckAsync(string name, bool configured, Func<Task<SubServiceHealth>> test) =>
        configured
            ? test()
            : Task.FromResult(new SubServiceHealth(name, false, false, "Not configured.", null));
}
