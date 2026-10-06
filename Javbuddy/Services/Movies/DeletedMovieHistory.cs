using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public static class DeletedMovieHistory
{
    public static async Task RecordAsync(AppDbContext db, Movie movie, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(movie.Code)) return;

        var normalized = CodeNormalization.Normalize(movie.Code);
        var deleted = await db.DeletedMovies.SingleOrDefaultAsync(m => m.NormalizedCode == normalized, ct);
        if (deleted is null)
        {
            deleted = new DeletedMovie { NormalizedCode = normalized };
            db.DeletedMovies.Add(deleted);
        }

        deleted.Code = movie.Code;
        deleted.CanonicalKey = CodeNormalization.GetCanonicalKey(movie.Code);
        deleted.Title = movie.Title;
        deleted.MetaTitle = movie.MetaTitle;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.PreviousStatus = movie.Status;
    }

    public static async Task ClearAsync(AppDbContext db, string code, CancellationToken ct = default)
    {
        var normalized = CodeNormalization.Normalize(code);
        var canonical = CodeNormalization.GetCanonicalKey(code);
        var deleted = await db.DeletedMovies
            .Where(m => m.NormalizedCode == normalized || m.CanonicalKey == canonical)
            .ToListAsync(ct);
        db.DeletedMovies.RemoveRange(deleted);
    }

    public static async Task<List<DeletedMovie>> GetMatchesAsync(AppDbContext db, IEnumerable<string> codes, CancellationToken ct = default)
    {
        var candidates = codes.Where(code => !string.IsNullOrWhiteSpace(code)).ToArray();
        if (candidates.Length == 0) return [];

        var normalized = candidates.Select(CodeNormalization.Normalize).Distinct().ToArray();
        var canonical = candidates.Select(CodeNormalization.GetCanonicalKey).Distinct().ToArray();
        return await db.DeletedMovies.AsNoTracking()
            .Where(m => normalized.Contains(m.NormalizedCode) || canonical.Contains(m.CanonicalKey))
            .ToListAsync(ct);
    }
}
