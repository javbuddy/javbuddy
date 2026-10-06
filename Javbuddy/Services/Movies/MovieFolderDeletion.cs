using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;

namespace Javbuddy.Services.Movies;

/// <summary>Deleting a movie's whole local folder from disk, shared by Movie Detail's Delete
/// and Cleanup's delete so both get the same library-root guard.</summary>
public static class MovieFolderDeletion
{
    /// <summary>The movie's local folder, if one resolves and exists — refused (with an error) unless
    /// it sits directly under one of the configured library roots, so a recursive delete can never
    /// reach a root itself or anything outside the library.</summary>
    public static async Task<(string? Folder, string? Error)> ResolveAsync(ILocalLibraryClient localLibraryClient, string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, null);

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(code, ct);
        if (folder is null || !Directory.Exists(folder)) return (null, null);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var parent = Path.GetDirectoryName(fullFolder);
        var roots = await localLibraryClient.GetRootPathsAsync(ct);
        var underRoot = parent is not null && roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), parent, comparison));
        return underRoot
            ? (fullFolder, null)
            : (null, $"Refusing to delete \"{folder}\": it isn't a folder directly inside a configured local library root.");
    }

    /// <summary>Resolves the movie's folder (see <see cref="ResolveAsync"/>) and deletes it
    /// recursively, retrying a "Directory not empty" failure for a few seconds (see
    /// <see cref="RetryingDirectoryDelete"/>). Returns the error to show, or null on success —
    /// including when no folder resolves, since there's then nothing on disk to delete.</summary>
    public static async Task<string?> DeleteAsync(ILocalLibraryClient localLibraryClient, string? code, CancellationToken ct = default)
    {
        var (folder, error) = await ResolveAsync(localLibraryClient, code, ct);
        if (error is not null || folder is null) return error;
        try
        {
            await RetryingDirectoryDelete.DeleteAsync(folder, RetryingDirectoryDelete.DefaultRetryDelays, ct: ct);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not delete the movie's folder: {ex.Message}";
        }
    }
}
