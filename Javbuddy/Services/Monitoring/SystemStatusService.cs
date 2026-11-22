using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Monitoring;

public sealed record ConfiguredIntegrations(bool Javinizer, bool Prowlarr, bool QBittorrent, bool Jellyfin, bool ChallengeSolver)
{
    public static ConfiguredIntegrations None { get; } = new(false, false, false, false, false);
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
    IJellyfinClient jellyfinClient,
    IChallengeSolverClient challengeSolverClient) : ISystemStatusService
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
        if (configured.Prowlarr)
        {
            foreach (var indexer in await prowlarrClient.GetDisabledIndexersAsync(ct))
            {
                messages.Add($"Prowlarr indexer {indexer.Name} is {indexer.Reason} — release searches skip it.");
            }
        }
        if (!configured.QBittorrent)
        {
            messages.Add("qBittorrent is not configured — grabbing releases will fail until you configure it in Settings.");
        }

        return new SystemHealthReport(migrationVersion, messages, configured);
    }

    public async Task<ConfiguredIntegrations> GetConfiguredAsync(CancellationToken ct = default)
    {
        var statuses = await connectionStatusService.GetStatusesAsync(ct);
        bool IsConfigured(string key) => statuses.Any(s => s.Key == key && s.Configured);

        return new ConfiguredIntegrations(
            IsConfigured("javinizer"), IsConfigured("prowlarr"), IsConfigured("qbittorrent"), IsConfigured("jellyfin"),
            IsConfigured("flaresolverr"));
    }

    public async Task<List<SubServiceHealth>> GetSubServiceHealthAsync(ConfiguredIntegrations configured, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(
            CheckClientAsync("javinizer-go", configured.Javinizer, () => javinizerClient.TestConnectionAsync(ct), () => javinizerClient.GetExternalUrlAsync(ct), r => (r.Success, r.Message, r.Version)),
            CheckClientAsync("Prowlarr", configured.Prowlarr, () => prowlarrClient.TestConnectionAsync(ct), () => prowlarrClient.GetExternalUrlAsync(ct), r => (r.Success, r.Message, r.Version)),
            CheckClientAsync("qBittorrent", configured.QBittorrent, () => qBittorrentClient.TestConnectionAsync(ct), () => qBittorrentClient.GetExternalUrlAsync(ct), r => (r.Success, r.Message, r.Version)),
            CheckClientAsync("Jellyfin", configured.Jellyfin, () => jellyfinClient.TestConnectionAsync(ct), () => jellyfinClient.GetExternalUrlAsync(ct), r => (r.Success, r.Message, r.Version)),
            CheckClientAsync("Challenge solver", configured.ChallengeSolver, () => challengeSolverClient.TestConnectionAsync(ct), () => challengeSolverClient.GetExternalUrlAsync(ct), r => (r.Success, r.Message, r.Version)));

        return [.. results];
    }

    /// <summary>Tests one integration (and fetches its external URL) in parallel, or reports it as
    /// not configured without calling it. <paramref name="unpack"/> adapts each client's own result record.</summary>
    private static async Task<SubServiceHealth> CheckClientAsync<TResult>(
        string name,
        bool configured,
        Func<Task<TResult>> test,
        Func<Task<string?>> externalUrl,
        Func<TResult, (bool Success, string Message, string? Version)> unpack)
    {
        if (!configured)
        {
            return new SubServiceHealth(name, false, false, "Not configured.", null);
        }

        var result = test();
        var url = externalUrl();
        await Task.WhenAll(result, url);
        var (success, message, version) = unpack(result.Result);
        return new SubServiceHealth(name, true, success, message, url.Result, version);
    }
}
