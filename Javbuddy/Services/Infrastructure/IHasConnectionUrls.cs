namespace Javbuddy.Services.Infrastructure;

/// <summary>Implemented by the DB-backed settings entities for each external integration
/// (JavinizerSettings, JellyfinSettings, ProwlarrSettings, QBittorrentSettings). Lets
/// <see cref="ApiClientBase{TSettings}"/> resolve/format base and external URLs generically
/// instead of every client reimplementing the same trim-and-fallback logic.</summary>
public interface IHasConnectionUrls
{
    string? BaseUrl { get; }
    string? ExternalUrl { get; }
}
