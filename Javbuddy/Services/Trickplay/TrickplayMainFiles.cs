using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>A movie's main video file as trickplay sees it: the MovieFile row MovieVideoFiles would
/// stream (the primary version, else the first by case-insensitive name) and its probe data.</summary>
public sealed record TrickplayMainFile(int MovieId, string Code, string? JellyfinItemId, string? FileName, double? DurationSeconds, int? Width, int? Height)
{
    /// <summary>Null until the file has been probed (see TrickplayIdentity.For).</summary>
    public string? Identity => FileName is null ? null : TrickplayIdentity.For(FileName, DurationSeconds, Width, Height);
}

public static class TrickplayMainFiles
{
    public static async Task<TrickplayMainFile?> LoadAsync(AppDbContext db, int movieId, CancellationToken ct = default) =>
        (await LoadAsync(db, db.Movies.Where(m => m.Id == movieId), ct)).FirstOrDefault();

    /// <summary>Every movie with a local video file, for the backfill.</summary>
    public static Task<List<TrickplayMainFile>> LoadAllWithLocalVideoAsync(AppDbContext db, CancellationToken ct = default) =>
        LoadAsync(db, db.Movies.Where(m => m.LocalFileSizeBytes != null), ct);

    private static async Task<List<TrickplayMainFile>> LoadAsync(AppDbContext db, IQueryable<Models.Movie> movies, CancellationToken ct)
    {
        var rows = await movies.AsNoTracking()
            .Where(m => m.Code != null && m.Code != "")
            .Select(m => new
            {
                m.Id,
                m.Code,
                m.JellyfinItemId,
                Files = m.MovieFiles.Select(f => new { f.FileName, f.IsPrimary, f.DurationSeconds, f.Width, f.Height }).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(m =>
        {
            // Ordered in memory so the name comparison matches MovieVideoFiles exactly.
            var main = m.Files
                .OrderByDescending(f => f.IsPrimary)
                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return new TrickplayMainFile(m.Id, m.Code!, m.JellyfinItemId, main?.FileName, main?.DurationSeconds, main?.Width, main?.Height);
        }).ToList();
    }
}
