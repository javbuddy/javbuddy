using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents;

/// <summary>One page of Activity &gt; History. <see cref="Page"/> is the page actually returned (1-based), which
/// may differ from the one asked for when that was outside the current range.</summary>
public sealed record TorrentHistoryPage(IReadOnlyList<TorrentDownload> Rows, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public interface ITorrentHistoryService
{
    Task<TorrentHistoryPage> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Activity &gt; History's read: every finished (removed or sorted) download stays reachable, newest grab
/// first with the Id as tiebreaker so equal timestamps page deterministically, read untracked one page at a time.</summary>
public class TorrentHistoryService(IDbContextFactory<AppDbContext> dbFactory) : ITorrentHistoryService
{
    public async Task<TorrentHistoryPage> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Max(1, pageSize);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var history = db.TorrentDownloads
            .AsNoTracking()
            .Where(d => d.RemovedFromClientAt != null || d.SortedAt != null);

        var total = await history.CountAsync(ct);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var current = Math.Clamp(page, 1, lastPage);

        var rows = await history
            .OrderByDescending(d => d.GrabbedAt)
            .ThenByDescending(d => d.Id)
            .Skip((current - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new TorrentHistoryPage(rows, current, pageSize, total);
    }
}
