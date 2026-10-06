using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public sealed record DeletedMoviePage(IReadOnlyList<DeletedMovie> Movies, int TotalCount);

public interface IDeletedMovieService
{
    Task<DeletedMoviePage> GetPageAsync(int page, CancellationToken ct = default);
    Task PurgeAsync(int? id, CancellationToken ct = default);
}

public sealed class DeletedMovieService(IDbContextFactory<AppDbContext> dbFactory, MovieChangeNotifier notifier) : IDeletedMovieService
{
    public const int PageSize = 50;

    public async Task<DeletedMoviePage> GetPageAsync(int page, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.DeletedMovies.CountAsync(ct);
        var movies = await db.DeletedMovies.AsNoTracking()
            .OrderByDescending(m => m.DeletedAt).ThenByDescending(m => m.Id)
            .Skip(Math.Max(0, page) * PageSize).Take(PageSize).ToListAsync(ct);
        return new DeletedMoviePage(movies, count);
    }

    public async Task PurgeAsync(int? id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.DeletedMovies.AsQueryable();
        if (id.HasValue) query = query.Where(m => m.Id == id.Value);
        var count = await query.ExecuteDeleteAsync(ct);
        if (count > 0) notifier.NotifyChanged();
    }
}
