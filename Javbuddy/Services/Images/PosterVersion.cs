using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Images;

/// <summary>The "?v=" on a movie's /image-cache poster URL: the same inputs give the
/// same short token, so an unchanged poster keeps one URL the browser paints from cache, while a
/// changed one gets a new URL rather than the browser's cached old image.</summary>
public static class PosterVersion
{
    /// <param name="cachedAt">The latest UpdatedAt of the movie's cached poster rows — rewritten by a crop.</param>
    /// <param name="coverUrl">The movie's remote cover URL — changed by a metadata refresh.</param>
    /// <param name="source">The poster file in the library folder, when the caller has it — changed by a
    /// crop or an outside edit.</param>
    public static string Compute(DateTime? cachedAt, string? coverUrl, FileInfo? source = null)
    {
        var stamp = string.Join('|',
            source is { Exists: true } ? $"{source.LastWriteTimeUtc.Ticks}-{source.Length}" : "",
            cachedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            coverUrl ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)).AsSpan(0, 4));
    }

    /// <summary>Fills <see cref="Movie.PosterVersion"/> on already-loaded movies with one query for
    /// all their cached posters.</summary>
    public static async Task ApplyAsync(AppDbContext db, IReadOnlyCollection<Movie> movies, CancellationToken ct = default)
    {
        var codes = movies.Select(m => m.Code).OfType<string>().ToList();
        var postersCachedAt = await db.CachedImages
            .Where(c => c.Role == LocalImageCacheService.RolePoster && c.Index == 0 && codes.Contains(c.Code))
            .GroupBy(c => c.Code)
            .Select(g => new { Code = g.Key, UpdatedAt = g.Max(c => c.UpdatedAt) })
            .ToDictionaryAsync(x => x.Code, x => x.UpdatedAt, ct);
        foreach (var movie in movies)
        {
            DateTime? cachedAt = movie.Code is not null && postersCachedAt.TryGetValue(movie.Code, out var at) ? at : null;
            movie.PosterVersion = Compute(cachedAt, movie.MetaCoverUrl);
        }
    }
}
