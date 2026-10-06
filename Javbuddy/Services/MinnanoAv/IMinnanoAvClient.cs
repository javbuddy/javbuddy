namespace Javbuddy.Services.MinnanoAv;

public interface IMinnanoAvClient
{
    Task<IReadOnlyList<MinnanoAvSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default);

    Task<MinnanoAvPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default);

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
