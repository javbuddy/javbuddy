namespace Javbuddy.Services.Warashi;

public interface IWarashiClient
{
    Task<IReadOnlyList<WarashiSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default);

    Task<WarashiPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default);

    Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct = default);

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
