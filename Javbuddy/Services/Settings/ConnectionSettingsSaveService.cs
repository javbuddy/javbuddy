using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Settings;

public interface IConnectionSettingsSaveService
{
    Task<JavinizerSettings> GetJavinizerAsync(CancellationToken ct = default);
    Task<ProwlarrSettings> GetProwlarrAsync(CancellationToken ct = default);
    Task<JellyfinSettings> GetJellyfinAsync(CancellationToken ct = default);
    Task<LocalLibrarySettings> GetLocalLibraryAsync(CancellationToken ct = default);
    Task<QBittorrentSettings> GetQBittorrentAsync(CancellationToken ct = default);

    /// <summary>Saves only Jellyfin's library selection (comma-joined names), creating the row when
    /// connection settings come from env vars and none exists yet.</summary>
    Task SaveJellyfinLibrarySelectionAsync(IEnumerable<string> libraryNames, CancellationToken ct = default);

    Task SaveJavinizerAsync(JavinizerSettings settings, CancellationToken ct = default);
    Task SaveProwlarrAsync(ProwlarrSettings settings, CancellationToken ct = default);
    Task SaveJellyfinConnectionAsync(JellyfinSettings settings, CancellationToken ct = default);
    Task SaveLocalLibraryAsync(LocalLibrarySettings settings, CancellationToken ct = default);
    Task SaveQBittorrentAsync(QBittorrentSettings settings, CancellationToken ct = default);
}

/// <summary>Backs the Settings &gt; Connections page's 5 per-integration "Save" buttons, each a
/// single-row upsert onto its own settings table (see <see cref="SingleRowSettingsRepository{TSettings}"/>).
/// Also serves each section's current row (or unsaved defaults) and Jellyfin's separate
/// library-selection save, which only touches that one field.</summary>
public class ConnectionSettingsSaveService(IDbContextFactory<AppDbContext> dbFactory) : IConnectionSettingsSaveService
{
    private readonly SingleRowSettingsRepository<JavinizerSettings> javinizer = new SingleRowSettingsRepository<JavinizerSettings>(dbFactory, db => db.JavinizerSettings, x => x.Id,
            (existing, form) =>
            {
                existing.BaseUrl = form.BaseUrl;
                existing.ExternalUrl = form.ExternalUrl;
                existing.ApiToken = form.ApiToken;
            });
    private readonly SingleRowSettingsRepository<ProwlarrSettings> prowlarr = new SingleRowSettingsRepository<ProwlarrSettings>(dbFactory, db => db.ProwlarrSettings, x => x.Id,
            (existing, form) =>
            {
                existing.BaseUrl = form.BaseUrl;
                existing.ExternalUrl = form.ExternalUrl;
                existing.ApiKey = form.ApiKey;
            });
    private readonly SingleRowSettingsRepository<JellyfinSettings> jellyfin = new SingleRowSettingsRepository<JellyfinSettings>(dbFactory, db => db.JellyfinSettings, x => x.Id,
            (existing, form) =>
            {
                existing.Enabled = form.Enabled;
                existing.BaseUrl = form.BaseUrl;
                existing.ExternalUrl = form.ExternalUrl;
                existing.ApiKey = form.ApiKey;
            });
    private readonly SingleRowSettingsRepository<LocalLibrarySettings> localLibrary = new SingleRowSettingsRepository<LocalLibrarySettings>(dbFactory, db => db.LocalLibrarySettings, x => x.Id,
            (existing, form) => existing.RootPaths = form.RootPaths);
    private readonly SingleRowSettingsRepository<QBittorrentSettings> qbittorrent = new SingleRowSettingsRepository<QBittorrentSettings>(dbFactory, db => db.QBittorrentSettings, x => x.Id,
            (existing, form) =>
            {
                existing.BaseUrl = form.BaseUrl;
                existing.ExternalUrl = form.ExternalUrl;
                existing.Username = form.Username;
                existing.Password = form.Password;
                existing.Category = form.Category;
            });

    public Task SaveJavinizerAsync(JavinizerSettings settings, CancellationToken ct = default) => javinizer.SaveAsync(settings, ct);
    public Task SaveProwlarrAsync(ProwlarrSettings settings, CancellationToken ct = default) => prowlarr.SaveAsync(settings, ct);
    public Task SaveJellyfinConnectionAsync(JellyfinSettings settings, CancellationToken ct = default) => jellyfin.SaveAsync(settings, ct);
    public Task SaveLocalLibraryAsync(LocalLibrarySettings settings, CancellationToken ct = default) => localLibrary.SaveAsync(settings, ct);
    public Task SaveQBittorrentAsync(QBittorrentSettings settings, CancellationToken ct = default) => qbittorrent.SaveAsync(settings, ct);

    public Task<JavinizerSettings> GetJavinizerAsync(CancellationToken ct = default) => javinizer.GetAsync(() => new JavinizerSettings(), ct);
    public Task<ProwlarrSettings> GetProwlarrAsync(CancellationToken ct = default) => prowlarr.GetAsync(() => new ProwlarrSettings(), ct);
    public Task<JellyfinSettings> GetJellyfinAsync(CancellationToken ct = default) => jellyfin.GetAsync(() => new JellyfinSettings(), ct);
    public Task<LocalLibrarySettings> GetLocalLibraryAsync(CancellationToken ct = default) => localLibrary.GetAsync(() => new LocalLibrarySettings(), ct);
    public Task<QBittorrentSettings> GetQBittorrentAsync(CancellationToken ct = default) => qbittorrent.GetAsync(() => new QBittorrentSettings(), ct);

    public Task SaveJellyfinLibrarySelectionAsync(IEnumerable<string> libraryNames, CancellationToken ct = default)
    {
        var joined = string.Join(",", libraryNames);
        return jellyfin.UpdateAsync(() => new JellyfinSettings(), row => row.SelectedLibraryNames = joined, ct);
    }
}
