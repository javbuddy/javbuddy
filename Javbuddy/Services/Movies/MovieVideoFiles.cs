using Javbuddy.Data;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>A movie's main local video file.</summary>
public sealed record MovieVideoFile(string Code, string Path);

/// <summary>Finds a movie's main local video file the way VideoRepairService does: the movie's
/// local folder, then its primary version (else the first version, else MediaVideoFileName), else
/// the first video in the folder. Shared by the scene chapter import and scene
/// media generation.</summary>
public static class MovieVideoFiles
{
    public static async Task<MovieVideoFile?> FindMainAsync(
        IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibraryClient, int movieId, CancellationToken ct = default)
    {
        string code;
        string? fileName;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies.AsNoTracking().Include(m => m.MovieFiles).FirstOrDefaultAsync(m => m.Id == movieId, ct);
            if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
            {
                return null;
            }

            code = movie.Code;
            fileName = movie.MovieFiles
                .OrderByDescending(f => f.IsPrimary)
                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FileName)
                .FirstOrDefault() ?? movie.MediaVideoFileName;
        }

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(code, ct);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        var path = !string.IsNullOrWhiteSpace(fileName) ? System.IO.Path.Combine(folder, fileName) : null;
        if (path is null || !File.Exists(path))
        {
            path = MovieVersionParser.FindVideoFiles(folder).FirstOrDefault();
        }
        return path is null ? null : new MovieVideoFile(code, path);
    }

    /// <summary>One specific version of a movie: the MovieFile fileId, only when it
    /// belongs to movieId and exists in the movie's local folder. Never falls back to another file.</summary>
    public static async Task<MovieVideoFile?> FindAsync(
        IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibraryClient, int movieId, int fileId, CancellationToken ct = default)
    {
        string? code;
        string? fileName;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var file = await db.MovieFiles.AsNoTracking()
                .Where(f => f.Id == fileId && f.MovieId == movieId)
                .Select(f => new { f.Movie.Code, f.FileName })
                .FirstOrDefaultAsync(ct);
            (code, fileName) = (file?.Code, file?.FileName);
        }
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(fileName)) return null;

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(code, ct);
        if (folder is null) return null;

        var path = System.IO.Path.Combine(folder, fileName);
        return File.Exists(path) ? new MovieVideoFile(code, path) : null;
    }
}
