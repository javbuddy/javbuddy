using Javbuddy.Data;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>Resolves the local video file the player streams for a movie, so playback
/// works without Jellyfin. The movie's main file (see MovieVideoFiles) or one of its versions
///, and only when it lies inside a configured library root — the stream endpoint never takes a path from the request.</summary>
public interface IMovieStreamService
{
    /// <summary>Full path of the movie's main local video file, or null when there's none or it
    /// resolves outside every library root.</summary>
    Task<string?> GetMainFilePathAsync(int movieId, CancellationToken ct = default);

    /// <summary>Full path of one of the movie's versions, or null when fileId isn't one
    /// of the movie's files, isn't on disk, or resolves outside every library root.</summary>
    Task<string?> GetFilePathAsync(int movieId, int fileId, CancellationToken ct = default);

    /// <summary>The VR / 3D format (see <see cref="VrFormat"/>) of the version <paramref name="fileId"/>, or of
    /// the movie's main file when null; null for flat video or an unknown file.</summary>
    Task<string?> GetVrTypeAsync(int movieId, int? fileId = null, CancellationToken ct = default);

    /// <summary>The movie's versions the player can switch between, primary first.</summary>
    Task<IReadOnlyList<MovieVersionOption>> GetVersionsAsync(int movieId, CancellationToken ct = default);
}

/// <summary>One of a movie's versions in the player's picker.</summary>
public sealed record MovieVersionOption(int FileId, string Label, bool IsPrimary);

public sealed class MovieStreamService(IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibraryClient) : IMovieStreamService
{
    /// <summary>The player's stream URL for a movie: its main file, or the version fileId.</summary>
    public static string StreamUrl(int movieId, int? fileId = null) =>
        fileId is null ? $"/api/movies/{movieId}/stream" : $"/api/movies/{movieId}/stream?fileId={fileId}";

    public async Task<string?> GetMainFilePathAsync(int movieId, CancellationToken ct = default) =>
        await InsideRootsAsync(await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct), ct);

    public async Task<string?> GetFilePathAsync(int movieId, int fileId, CancellationToken ct = default) =>
        await InsideRootsAsync(await MovieVideoFiles.FindAsync(dbFactory, localLibraryClient, movieId, fileId, ct), ct);

    public async Task<string?> GetVrTypeAsync(int movieId, int? fileId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var files = db.MovieFiles.AsNoTracking().Where(f => f.MovieId == movieId);
        if (fileId is { } id) return await files.Where(f => f.Id == id).Select(f => f.VrType).FirstOrDefaultAsync(ct);

        // The main file is the primary version (see MovieVideoFiles.FindMainAsync), else the first by name.
        var main = (await files.Select(f => new { f.FileName, f.IsPrimary, f.VrType }).ToListAsync(ct))
            .OrderByDescending(f => f.IsPrimary)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return main is not null
            ? main.VrType
            : await db.Movies.AsNoTracking().Where(m => m.Id == movieId).Select(m => m.VrType).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<MovieVersionOption>> GetVersionsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var files = await db.MovieFiles.AsNoTracking().Where(f => f.MovieId == movieId).ToListAsync(ct);
        return files
            .OrderByDescending(f => f.IsPrimary)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MovieVersionOption(f.Id, MovieVersionParser.FormatVersionLabel(f), f.IsPrimary))
            .ToList();
    }

    private async Task<string?> InsideRootsAsync(MovieVideoFile? file, CancellationToken ct)
    {
        if (file is null) return null;

        var roots = await localLibraryClient.GetRootPathsAsync(ct);
        return IsUnderAnyRoot(file.Path, roots) ? Path.GetFullPath(file.Path) : null;
    }

    /// <summary>Whether path resolves to somewhere inside one of roots, after normalizing both — so
    /// neither ".." segments nor a sibling folder sharing a root's name prefix ("/media/jav2" against
    /// root "/media/jav") gets through.</summary>
    public static bool IsUnderAnyRoot(string path, IEnumerable<string> roots)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (full.StartsWith(fullRoot, comparison)) return true;
        }
        return false;
    }

    /// <summary>The Content-Type a video file is served with, by extension. Browsers sniff the
    /// container themselves, so this only needs to be close enough for them to try.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".wmv" => "video/x-ms-wmv",
        ".ts" => "video/mp2t",
        _ => "application/octet-stream",
    };
}
